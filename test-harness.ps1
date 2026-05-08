#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$LogPath = (Join-Path $env:ProgramData 'PharmaDataGuard\pharma-data-guard.log'),
    [string]$Verifier = (Join-Path $PSScriptRoot 'PharmaDataGuard.LogVerifier\bin\Release\PharmaDataGuard.LogVerifier.exe'),
    [string]$AppExe = (Join-Path $PSScriptRoot 'PharmaDataGuard\bin\Release\PharmaDataGuard.exe'),
    [string]$ReportPath = (Join-Path $PSScriptRoot 'OQ-Report.md')
)

$ErrorActionPreference = 'Stop'

function Test-Result {
    param([string]$Id, [string]$Name, [bool]$Pass, [string]$Detail)
    [pscustomobject]@{
        Id = $Id; Name = $Name; Pass = $Pass; Detail = $Detail
    }
}

$results = @()

# Pre-flight
$proc = Get-Process -Name PharmaDataGuard -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) {
    Write-Warning "PharmaDataGuard is not running. Start it before running OQ tests."
    $results += Test-Result 'PRE-01' 'PharmaDataGuard running' $false 'process not found'
} else {
    $results += Test-Result 'PRE-01' 'PharmaDataGuard running' $true ("pid=" + $proc.Id)
}

# OQ-01: Set-Clipboard via .NET API
try {
    [System.Windows.Forms.Clipboard]::SetText('sentinel-OQ01') 2>$null
} catch { Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Clipboard]::SetText('sentinel-OQ01') }
Start-Sleep -Milliseconds 300
$got = ''
try { $got = [System.Windows.Forms.Clipboard]::GetText() } catch { $got = '' }
$results += Test-Result 'OQ-01' 'Clipboard wiped after .NET SetText' ([string]::IsNullOrEmpty($got)) ("got='" + $got + "'")

# OQ-03: Set-Clipboard cmdlet
Set-Clipboard -Value 'sentinel-OQ03'
Start-Sleep -Milliseconds 300
$got = (Get-Clipboard -ErrorAction SilentlyContinue)
$results += Test-Result 'OQ-03' 'Clipboard wiped after Set-Clipboard cmdlet' ([string]::IsNullOrEmpty($got)) ("got='" + ($got -join '') + "'")

# OQ-05: NTFS DENY ACE prevents Remove-Item
$tmp = Join-Path $env:TEMP ('PDG-OQ05-' + [guid]::NewGuid().ToString('N') + '.tmp')
'soft delete me' | Out-File -FilePath $tmp -Encoding utf8
& icacls $tmp /deny "Everyone:(D,DC,WD,WDAC,WO)" 2>&1 | Out-Null
$deleted = $false
try { Remove-Item -Path $tmp -Force -ErrorAction Stop; $deleted = -not (Test-Path $tmp) } catch { $deleted = $false }
& icacls $tmp /remove:d "Everyone" 2>&1 | Out-Null
Remove-Item -Path $tmp -Force -ErrorAction SilentlyContinue
$results += Test-Result 'OQ-05' 'NTFS DENY ACE blocks Remove-Item' (-not $deleted) ("deleted=" + $deleted)

# OQ-06: DisableTaskMgr policy is set to 1
$dtm = (Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System' -Name DisableTaskMgr -ErrorAction SilentlyContinue).DisableTaskMgr
$results += Test-Result 'OQ-06' 'Policy DisableTaskMgr=1' ($dtm -eq 1) ("value=" + $dtm)

# OQ-07: Watchdog respawn
$pidBefore = if ($proc) { $proc.Id } else { -1 }
if ($pidBefore -gt 0) {
    try { Stop-Process -Id $pidBefore -Force -ErrorAction SilentlyContinue } catch {}
    Start-Sleep -Seconds 4
    $procAfter = Get-Process -Name PharmaDataGuard -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $pidBefore } | Select-Object -First 1
    $respawned = $null -ne $procAfter -and $procAfter.Id -ne $pidBefore
    $results += Test-Result 'OQ-07' 'Watchdog respawns PharmaDataGuard' $respawned ("oldPid=$pidBefore newPid=" + $(if ($procAfter) { $procAfter.Id } else { 'none' }))
} else {
    $results += Test-Result 'OQ-07' 'Watchdog respawns PharmaDataGuard' $false 'process not running'
}

# OQ-09: Log verifier intact + tampered detection
$verOk = $false; $verBad = $false; $detail = ''
if ((Test-Path $Verifier) -and (Test-Path $LogPath)) {
    $copy = Join-Path $env:TEMP ('PDG-OQ09-' + [guid]::NewGuid().ToString('N') + '.log')
    Copy-Item -Path $LogPath -Destination $copy -Force
    & $Verifier $copy $env:COMPUTERNAME | Out-Null
    $verOk = ($LASTEXITCODE -eq 0)

    if ((Get-Item $copy).Length -gt 10) {
        $bytes = [System.IO.File]::ReadAllBytes($copy)
        $bytes[ [int]([math]::Floor($bytes.Length/2)) ] = [byte](([int]$bytes[[math]::Floor($bytes.Length/2)] -bxor 0xAA) -band 0xFF)
        [System.IO.File]::WriteAllBytes($copy, $bytes)
    }
    & $Verifier $copy $env:COMPUTERNAME | Out-Null
    $verBad = ($LASTEXITCODE -eq 1)
    Remove-Item $copy -Force -ErrorAction SilentlyContinue
    $detail = "intact-exit=" + ($verOk) + " tampered-exit=1=" + ($verBad)
} else {
    $detail = 'verifier or log missing'
}
$results += Test-Result 'OQ-09' 'Log verifier exit codes (intact 0 / tampered 1)' ($verOk -and $verBad) $detail

# Build report
$lines = @()
$lines += "# Pharma Data Guard 21 CFR — OQ Report"
$lines += ""
$lines += "- Machine : " + $env:COMPUTERNAME
$lines += "- User    : " + $env:USERDOMAIN + "\" + $env:USERNAME
$lines += "- OS      : " + (Get-CimInstance Win32_OperatingSystem).Caption + " (" + (Get-CimInstance Win32_OperatingSystem).Version + ")"
$lines += "- Date    : " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')
$lines += "- Log     : " + $LogPath
$lines += ""
$lines += "## Test Results"
$lines += ""
$lines += "| Id | Test | Pass | Detail |"
$lines += "|----|------|------|--------|"
foreach ($r in $results) {
    $mark = if ($r.Pass) { "PASS" } else { "FAIL" }
    $lines += "| " + $r.Id + " | " + $r.Name + " | " + $mark + " | " + ($r.Detail -replace '\|', '/') + " |"
}
$lines += ""
$lines += "## Last 20 audit log lines"
$lines += '```'
if (Test-Path $LogPath) { $lines += (Get-Content $LogPath -Tail 20) } else { $lines += '(log not present)' }
$lines += '```'
$lines += ""
$lines += "Operator signature: ____________________"
$lines += ""
$lines += "QA signature:       ____________________"

Set-Content -Path $ReportPath -Value $lines -Encoding utf8
Write-Host ""
Write-Host ("Report written to: " + $ReportPath)

$failed = ($results | Where-Object { -not $_.Pass } | Measure-Object).Count
exit $failed
