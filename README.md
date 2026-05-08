# Pharma-Data-Guard-21-CFR
A pharma compliance tool that disables copy, paste, cut &amp; delete operations at the OS level to ensure 21 CFR Part 11 data integrity. Protects GxP electronic records from unauthorized modification or deletion. FDA-compliant endpoint protection for pharmaceutical environments.

## Table of contents

- [What it does](#what-it-does)
- [Who it's for](#who-its-for)
- [Why this exists](#why-this-exists)
- [Defensive layers](#defensive-layers)
- [How each guard works](#how-each-guard-works)
- [Tray menu](#tray-menu)
- [Audit log](#audit-log)
- [Configuration](#configuration)
- [Building from source](#building-from-source)
- [Running for the first time](#running-for-the-first-time)
- [Password management](#password-management)
- [Deployment](#deployment)
- [Validation (21 CFR Part 11 / EU GMP Annex 11)](#validation-21-cfr-part-11--eu-gmp-annex-11)
- [Anti-virus and EDR](#anti-virus-and-edr)
- [Threat model](#threat-model)
- [Architecture](#architecture)
- [Win32 API surface](#win32-api-surface)
- [Registry, filesystem, and process footprint](#registry-filesystem-and-process-footprint)
- [Uninstall](#uninstall)
- [Known issues](#known-issues)
- [Roadmap](#roadmap)
- [FAQ](#faq)
- [Status and disclaimer](#status-and-disclaimer)
- [Contributing](#contributing)
- [License](#license)
- [Keywords for search](#keywords-for-search)
- [References](#references)

---

## What it does

`pharma-data-guard-21cfr` is a focused, **single-purpose pharma compliance
endpoint lockdown** — not a generic data-loss-prevention (DLP) suite.
Launched elevated, it runs silently in the system tray and applies
**five concurrent defensive layers** at the operating-system level:

- **Blocks copy / cut / paste keystrokes** (`Ctrl+C`, `Ctrl+V`, `Ctrl+X`, etc.) system-wide
- **Wipes the clipboard** every time anything writes to it (right-click Copy, drag-drop, COM automation, PowerShell `Set-Clipboard`)
- **Greys out Copy / Cut / Paste / Delete / Send To / Save As / Print / Rename** in every Windows right-click context menu
- **Locks configured pharmaceutical record folders** with NTFS DENY ACEs that block deletion even by Administrators
- **Cancels file drag-drop** from File Explorer / Desktop / Open & Save dialogs to any destination
- **Disables `PrintScreen`, `Win+Shift+S` (Snipping Tool), `Alt+PrintScreen`, `Win+PrintScreen`** for screen-capture protection

Every blocked operation is recorded in a cryptographically chained, **tamper-evident
audit trail** stored at `%ProgramData%\PharmaDataGuard\pharma-data-guard.log`.
A standalone command-line **log verifier** re-computes the HMAC-SHA256 chain
and returns a deterministic exit code (0 = intact, 1 = tampered, 2 = I/O
error) — directly suitable for inclusion in **QA validation binders** and
**21 CFR Part 11 compliance evidence**.

It is **not** generic DLP, **not** an antivirus, and **not** an EDR. Its scope
is deliberately narrow: a pharmaceutical workstation, during an audit, locked
against the specific actions that an inspection forbids, with a verifiable
record of every block.

---

## Who it's for

- **Pharmaceutical companies** (Indian and global) preparing for **FDA**,
  **EMA**, **MHRA**, **CDSCO**, **WHO PQ**, or customer **GMP audits**
- **Medical-device** and **biotech** organizations subject to **21 CFR
  Part 820** and **Part 11**
- **Contract-research organizations (CROs)** and **clinical-trial sites**
  during **source-data review** and monitor visits
- **Pharmacovigilance** and **regulatory-affairs** teams handling
  controlled electronic records
- **Regulated-finance**, **government records**, and **examination**
  workstations with similar data-exfiltration exposure

This tool is **not** designed to be a daily-driver lockdown — it changes
Windows shell behaviour (legacy context menu fallback, drag-drop
suppression) in ways that are appropriate for an audit kiosk but
disruptive for general productivity workstations.

---

## Why this exists

Pharmaceutical and **GxP-regulated** companies have historically locked
down audit workstations using:

- **AutoHotkey scripts** that block keyboard shortcuts
- **VB / VBA macros** inside Office applications
- **Batch files that disable Task Manager** via the `DisableTaskMgr` registry key
- Ad-hoc **PowerShell** tools

None of these approaches are defensible for **21 CFR Part 11** or
**EU GMP Annex 11** regulatory deployment:

| Approach | Failure mode |
|---|---|
| AutoHotkey scripts | Killable via Task Manager / End Task; trivially bypassed by elevated user |
| VB / VBA macros in Office | Only protect within Office; right-click in Explorer is wide open |
| Disable-paste-only registry tweaks | Don't cover drag-drop, context menus, COM, PowerShell `Set-Clipboard` |
| Batch files setting `DisableTaskMgr=1` | Persistent system change, no audit trail, no per-event log |
| Manual "watch the screen" supervision | Not scalable, no evidence after the fact |

None produce a **tamper-evident audit trail** acceptable for
**21 CFR Part 11 §11.10(e)** ("Use of secure, computer-generated,
time-stamped audit trails to independently record … operator entries
and actions that create, modify, or delete electronic records").

`pharma-data-guard-21cfr` replaces the patchwork with **OS-level enforcement**
plus an HMAC-chained log designed for independent verification, supporting
**ALCOA+ data integrity** principles end-to-end.

---

## Defensive layers

Five layers run concurrently. Each is independent — disabling one does not
affect the others. All are active by default with the supplied configuration.

| # | Layer | Mechanism | What it stops |
|---|---|---|---|
| 1 | **Keyboard** | `WH_KEYBOARD_LL` low-level keyboard hook | `Ctrl+C` / `Ctrl+V` / `Ctrl+X`, `Ctrl+Insert` / `Shift+Insert`, `Shift+Delete`, `Delete` (alone), `PrintScreen`, `Win+Shift+S`, `Win+PrintScreen`, `Alt+PrintScreen`. Optionally: `Ctrl+A`, `Ctrl+S`, `Ctrl+P`. |
| 2 | **Clipboard** | `AddClipboardFormatListener` + immediate `EmptyClipboard` on every change | Right-click Copy, Edit-menu Copy, drag-and-drop into a clipboard format, COM automation, PowerShell `Set-Clipboard`, any program that writes to the clipboard |
| 3 | **Context menu** | 50 ms timer that finds the visible `#32768` popup, walks its items via `MN_GETHMENU`, calls `EnableMenuItem(MF_GRAYED \| MF_DISABLED)` on forbidden items | Greys out **Copy / Cut / Paste / Delete / Move to / Send to / Share / Upload / Open with / Export / Save as / Save a copy / Print / Rename** and their Hindi (कॉपी, काटें, चिपकाएं, हटाएं, मिटाएं) and Gujarati (કૉપિ, પેસ્ટ, કાઢી) translations, in any application's right-click menu |
| 4 | **File system** | NTFS explicit-DENY ACEs on configured paths, with SDDL backup before modification | `Shift+Del`, `del`, `Remove-Item`, drag-to-Recycle-Bin, file-manager delete, even `runas /user:Administrator del` — kernel evaluates the explicit DENY before any ALLOW, including for Administrators. **Protects pharmaceutical electronic records from unauthorized deletion or modification.** |
| 5 | **Drag-drop** | Two parallel signals: `SetWinEventHook` for `EVENT_OBJECT_DRAGSTART` / `EVENT_SYSTEM_DRAGDROPSTART` and `WH_MOUSE_LL` filtered to shell-source windows. On detection, injects `VK_ESCAPE` via `SendInput` + posts `WM_CANCELMODE` to the source root, *and* swallows further `WM_MOUSEMOVE` events until `WM_LBUTTONUP` so the OLE drag-drop pipeline never accumulates enough motion to enter `DoDragDrop` | File drag from File Explorer / Desktop / Open & Save common dialogs to any destination, including same-window moves |

Plus three lifecycle features that make the layers trustworthy:

- **Modern context menu fallback** — disables the Windows 11 modern XAML
  menu host (CLSID `{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}`) so right-click
  everywhere routes through the legacy `#32768` popup that Layer 3 can grey.
- **Sibling watchdog** — a paired `PharmaDataGuard.exe` process (started
  with `--watchdog <parent-pid>`) monitors the main instance every second
  and respawns it if killed by an unauthorised actor. The main process
  kills the watchdog cleanly during an authorised exit so the program
  actually stops.
- **Password-gated exit** — PBKDF2-HMAC-SHA1 with 100 000 iterations,
  16-byte per-installation random salt, 32-byte derived hash. Constant-time
  comparison. Required to exit, add a protected path, or change the
  password.

---

## How each guard works

This section is for security reviewers, contributors, QA validators, and
inspectors who want to know exactly what `pharma-data-guard-21cfr` does to
the operating system.

### Layer 1 — Keyboard hook

Installed via `SetWindowsHookEx(WH_KEYBOARD_LL = 13, ...)`. The callback
runs on the UI thread of the main process. The delegate is held as an
instance field (`_proc`) to prevent garbage collection while native code
holds the function pointer — a common cause of `AccessViolation` crashes
in naively-written hooks.

For each `WM_KEYDOWN` / `WM_SYSKEYDOWN`, the callback reads the modifier
state via `GetAsyncKeyState(VK_CONTROL / VK_SHIFT / VK_MENU / VK_LWIN /
VK_RWIN)` and matches the virtual-key code against a fixed block list.

**Always-blocked combinations** (relevant to **copy-protection** and
**screen-capture protection**):

```
PrintScreen
Win+Shift+S
Win+PrintScreen
Alt+PrintScreen
Ctrl+C, Ctrl+X, Ctrl+V       ← copy / cut / paste
Ctrl+Insert, Shift+Insert    ← legacy copy / paste
Shift+Delete                 ← bypass-Recycle-Bin permanent delete
Delete (alone)
```

**Conditionally blocked** via `BlockSelectAll`, `BlockSave`, `BlockPrint`
flags in `config.xml`:

```
Ctrl+A, Ctrl+S, Ctrl+P
```

When a combination matches, the callback returns `(IntPtr)1` to swallow
the event (it never reaches the foreground application) and writes a
`BLOCK | KEYBOARD` line to the audit log including the foreground process
name + PID for forensic context.

### Layer 2 — Clipboard guard

A `NativeWindow` is created with `Parent = HWND_MESSAGE (-3)` so it has
no taskbar presence. `AddClipboardFormatListener` registers it for
`WM_CLIPBOARDUPDATE = 0x031D`.

When the message arrives, `WipeClipboard` is invoked. It first calls
`CountClipboardFormats()` — if zero, the notification was caused by the
guard's own previous `EmptyClipboard` and is ignored. Without this check,
each wipe fires another `WM_CLIPBOARDUPDATE`, which triggers another
wipe, in an infinite feedback loop.

If non-empty, `OpenClipboard → EmptyClipboard → CloseClipboard` runs with
up to 5 retries spaced 10 ms apart for clipboard-lock contention. Logging
is throttled to one `BLOCK | CLIPBOARD` line per second.

Result: **no copy operation can leave usable data on the clipboard**, no
matter how it was triggered (keystroke, right-click, drag-drop, COM,
PowerShell, custom code).

### Layer 3 — Context menu guard

A 50 ms timer calls `FindWindow("#32768", null)` — the Win32 class for
popup menus. If found and `IsWindowVisible`, the guard sends
`MN_GETHMENU = 0x01E1` to retrieve the underlying `HMENU` and walks items
by position with `GetMenuItemCount` + `GetMenuString`.

Each menu instance is processed exactly **once** per appearance — the
HMENU is cached in `_lastSeenHMenu`. Item text is normalised (ampersand
accelerators stripped, trailing `\t<accelerator>` removed), then
case-insensitively matched against the **forbidden-prefix list**:

| Script | Prefixes |
|---|---|
| English | copy, cut, paste, delete, move to, send to, share, upload, open with, export, save as, save a copy, print, rename |
| Hindi | कॉपी, काटें, चिपकाएं, हटाएं, मिटाएं |
| Gujarati | કૉપિ, પેસ્ટ, કાઢી |

Matches receive `EnableMenuItem(hMenu, position, MF_BYPOSITION | MF_GRAYED | MF_DISABLED)`.
The item visually greys and clicks/keystrokes on it are ignored by the
menu's input handler.

To extend localisation (Marathi, Tamil, Telugu, Kannada, Punjabi,
Bengali), append to `ContextMenuGuard.ForbiddenPrefixes`.

### Layer 4 — File system ACL (electronic-records protection)

When `Lock(path)` is called for each entry in `ProtectedPaths`:

1. The current security descriptor is captured as **SDDL** and saved to
   `%ProgramData%\PharmaDataGuard\AclBackup\<safe-key>.sddl` — the
   recovery mechanism if an authorised exit fails before the ACE is
   removed.
2. A `FileSystemAccessRule` is added with these rights set as **Deny**:
   ```
   Delete | DeleteSubdirectoriesAndFiles | Write | ChangePermissions | TakeOwnership
   ```
   for the `Everyone` SID (`S-1-1-0`), with inheritance flags
   `ContainerInherit | ObjectInherit` for directories.
3. For files, the `ReadOnly` attribute is also set as a UI hint.

NTFS evaluates explicit DENY ACEs **before** explicit ALLOW ACEs. **Even
an Administrator cannot delete the file** without first removing the ACE
— and the ACE itself denies `ChangePermissions` and `TakeOwnership`, so
removing it requires the original DACL still in place, i.e. the program
must be running.

This is the **electronic-records-protection** mechanism for **GxP
documents**, **batch records**, **lab notebooks**, **clinical-trial
source data**, and any other regulated artefact that must not be
modified or deleted during an audit.

### Layer 5 — Drag-drop guard

Two parallel signals:

**Signal A — accessibility events.** `SetWinEventHook(EVENT_OBJECT_DRAGSTART
= 0x8021, EVENT_SYSTEM_DRAGDROPSTART = 0x000E, WINEVENT_OUTOFCONTEXT |
WINEVENT_SKIPOWNPROCESS)` catches drag starts from UIA/MSAA-emitting
sources before any visual feedback.

**Signal B — `WH_MOUSE_LL` low-level mouse hook.** Tracks `WM_LBUTTONDOWN`
/ `WM_MOUSEMOVE` / `WM_LBUTTONUP`. When `LBUTTONDOWN` lands on a window
whose root class is in the **shell-file allowlist**:

```
CabinetWClass     — File Explorer windows
ExploreWClass     — legacy Explorer
Progman           — Desktop
WorkerW           — Desktop wallpaper layer
#32770            — Common file dialogs
SHELLDLL_DefView  — file list view inside Explorer
DirectUIHWND      — DirectUI inside Explorer
SysListView32     — listview inside Open/Save dialogs
SysTreeView32     — tree inside Open/Save dialogs
```

…the guard records the start position. As soon as the cursor moves more
than `GetSystemMetrics(SM_CXDRAG = 68)` or `SM_CYDRAG = 69)` pixels
(default = 4 each):

1. Cancel sequence runs once: `SendInput(VK_ESCAPE down + up)` followed
   by `PostMessage(rootHwnd, WM_CANCELMODE = 0x001F, ...)`.
2. Every subsequent `WM_MOUSEMOVE` for that gesture returns `(IntPtr)1`,
   **swallowing the event**. The motion never reaches the kernel input
   queue, so OLE drag-drop cannot accumulate enough movement to enter
   `DoDragDrop`. The drag is killed at the source.
3. State resets on `WM_LBUTTONUP`. Normal mouse motion resumes.

Text selection in editors, scrollbar dragging, window dragging by title
bar, and right-click drag-drop in non-shell apps are unaffected because
their source class is not in the allowlist.

**Known UX trade-off**: marquee-rectangle file selection in Explorer
(drag in empty space to select multiple files) is also blocked, because
it originates on the same `DirectUIHWND` / `SysListView32` background as
a file drag and is indistinguishable at the hook level. Workaround:
`Ctrl+click` / `Shift+click` multi-select.

### Modern context menu fallback

The Windows 11 default right-click menu is a XAML island, not a Win32
`#32768` popup, so Layer 3 cannot reach it. The guard disables the modern
menu host so Explorer falls back to the legacy popup. One registry write
under HKCU:

```
HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32
   (Default) = ""    [REG_SZ, empty string]
```

The original value is captured before the write. On authorised exit, the
original value is restored. For the change to take effect on a running
Explorer, `explorer.exe` is restarted (taskbar disappears for ~1 second).

### Watchdog

The main process spawns one sibling at startup:

```
PharmaDataGuard.exe --watchdog <parent-pid>
```

The sibling enters `RunAsWatchdog(parentPid)`, polling
`Process.GetProcessById(parentPid).HasExited` every 1 second. If the
parent dies, it relaunches a fresh main process from
`MainModule.FileName` and exits.

Authorised exit calls `Watchdog.StopAndKillWatchdog()` **first**, which
kills the tracked sibling and any other `PharmaDataGuard.exe` process —
otherwise the watchdog detects the main exit and relaunches the program
within 1 second.

### Password protection

- **Hash function**: PBKDF2-HMAC-SHA1 (`Rfc2898DeriveBytes` in .NET).
- **Iterations**: 100 000.
- **Salt**: 16 bytes from `RandomNumberGenerator.Create()`,
  per-installation, base64-encoded in `config.xml`.
- **Derived hash**: 32 bytes, base64-encoded in `config.xml`.
- **Comparison**: constant-time XOR-accumulator across all bytes, no
  early return.

The plaintext password is read once into a managed `string` in the
dialog text box and discarded when the dialog closes. **Never written to
disk, never logged.**

Password complexity (enforced in both first-run and change-password
dialogs):

```
length ≥ 12
contains at least one uppercase letter
contains at least one lowercase letter
contains at least one digit
contains at least one symbol [^A-Za-z0-9]
not equal to the factory default "PharmaGuard@123"
not equal to the current password (change-password only)
```

Both dialogs allow at most **5 wrong attempts** before auto-closing.
Each attempt is logged as `WARN | AUTH | wrong attempt N/5`.

---

## Tray menu

The tray icon is the Windows shield (`SystemIcons.Shield`). Right-click
reveals:

| Menu item | Action | Requires password |
|---|---|---|
| **Pharma Data Guard — ACTIVE** | Disabled label | — |
| *separator* | | |
| **Add protected path…** | Folder browser. Selected folder gets the NTFS DENY ACE | ✅ |
| **Open audit log** | Opens `%ProgramData%\PharmaDataGuard\pharma-data-guard.log` in Notepad | No |
| **Change password…** | Opens the change-password dialog (current + new + confirm) | ✅ (current) |
| *separator* | | |
| **Exit (requires password)** | Authorised exit: stops all guards, kills watchdog, restores Explorer menu policy, removes ACEs, exits | ✅ |

Closing the tray icon's parent (hidden) form via `Alt+F4` or any other
`UserClosing` reason is **rejected** — only authorised exit via the tray
menu can stop the program.

---

## Audit log

### Path

```
%ProgramData%\PharmaDataGuard\pharma-data-guard.log
```

UTF-8 encoded, one event per line, append-only by design.

### Format

```
[<UTC timestamp>] <LEVEL> | <category> | user=<DOMAIN\user> | <payload> | h=<base64-hmac>
```

Example:

```
[2026-05-06T11:22:18.391Z] INFO  | LIFECYCLE | user=AzureAD\jdoe | Pharma Data Guard starting | h=v6uRhtFvVoPjDMAyLzFz25dIpHCwmIjmPzJuMa9Gtdo=
[2026-05-06T11:22:42.038Z] BLOCK | KEYBOARD  | user=AzureAD\jdoe | what=Ctrl+C ctx=excel.exe (pid=4812) | h=NYbw1VxsTOJVZK1rAiKGUA5+txglDV8bpN08wtHGPgA=
[2026-05-06T11:22:51.219Z] BLOCK | DRAGDROP  | user=AzureAD\jdoe | what=drag cancelled (mouseMove) ctx=dx=2 dy=7 | h=I4cOVB+KwmUL3Cz/Fo8cC13iuthndGvzRSDnsVifpIs=
[2026-05-06T11:24:19.501Z] BLOCK | MENU      | user=AzureAD\jdoe | what=grayed ctx=item=Copy | h=KizsVQjLilSVUZpZWIurIFzQdrg6AnK2ofmt53ykVn0=
```

| Field | Notes |
|---|---|
| Timestamp | ISO-8601 UTC, millisecond precision (**Contemporaneous** under ALCOA+) |
| Level | `INFO`, `WARN`, `ERROR`, `BLOCK` |
| Category | `BOOT`, `LIFECYCLE`, `KEYBOARD`, `CLIPBOARD`, `MENU`, `FILEGUARD`, `MOUSE`, `DRAGDROP`, `WATCHDOG`, `POLICY`, `MODERNMENU`, `AUTH`, `FATAL` |
| User | `WindowsIdentity.GetCurrent().Name` (**Attributable** under ALCOA+) |
| Payload | Free-form text; pipe `\|` characters in the payload are escaped to `/` |
| HMAC | base64(`HMAC-SHA256(prev_hash + line_body, key)`) (**Original / Accurate** under ALCOA+) |

### Hash chain semantics

Each line's HMAC input is `previous_hash + line_body`. The first line
uses an empty string as the previous hash. Tampering with any byte of
any line breaks **only that line's** hash — the next line uses the
*recorded* hash as its previous, not the recomputed one, so the chain
does not cascade-fail. The verifier reports the first mismatch and keeps
scanning to count the total.

### Verifier

```
PharmaDataGuard.LogVerifier.exe [<log-path>] [<machine-name>]
```

Both arguments default to the current machine's
`%ProgramData%\PharmaDataGuard\pharma-data-guard.log` and
`Environment.MachineName`. To verify an archived log produced on a
different machine, supply the original machine name (which the HMAC key
includes).

Output:

```
OK: 1247 lines verified.
```

or

```
FAIL: 3 tampered line(s) found.
First mismatch: line 942 — expected K5t2u6+aRy8l… got AAAAAAAAAAAA…
```

Exit codes:

| Code | Meaning |
|---|---|
| `0` | Every line intact |
| `1` | Tampering detected |
| `2` | I/O error |

Suitable for direct inclusion in **QA validation binders**, **21 CFR
Part 11 compliance evidence packages**, and **automated audit-prep
pipelines** (CI / scheduled tasks).

---

## Configuration

Path: `%ProgramData%\PharmaDataGuard\config.xml`. Created automatically
on first launch with all defaults. XML-serialised, atomic writes.

### Schema

```xml
<?xml version="1.0" encoding="utf-8"?>
<PharmaDataGuardConfig xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <UnlockPasswordHash>base64-32-bytes</UnlockPasswordHash>
  <UnlockPasswordSalt>base64-16-bytes</UnlockPasswordSalt>
  <ProtectedPaths>
    <Path>C:\PharmaSource\BatchRecords</Path>
    <Path>D:\AuditTemp\WorkingFiles</Path>
  </ProtectedPaths>
  <BlockSelectAll>false</BlockSelectAll>
  <BlockSave>false</BlockSave>
  <BlockPrint>false</BlockPrint>
  <EnableWatchdog>true</EnableWatchdog>
  <BlockFileDragDrop>true</BlockFileDragDrop>
  <EnableLegacyContextMenuFallback>true</EnableLegacyContextMenuFallback>
  <RestartExplorerOnPolicyChange>true</RestartExplorerOnPolicyChange>
</PharmaDataGuardConfig>
```

### Settings reference

| Element | Type | Default | Effect |
|---|---|---|---|
| `UnlockPasswordHash` | base64 string | (set on first launch) | PBKDF2-derived hash |
| `UnlockPasswordSalt` | base64 string | (random) | Per-installation salt |
| `ProtectedPaths` | array of strings | `[]` | Directories / files to apply NTFS DENY ACE to |
| `BlockSelectAll` | bool | `false` | Block `Ctrl+A` system-wide |
| `BlockSave` | bool | `false` | Block `Ctrl+S` system-wide |
| `BlockPrint` | bool | `false` | Block `Ctrl+P` system-wide |
| `EnableWatchdog` | bool | `true` | Sibling watchdog respawns the program if killed |
| `BlockFileDragDrop` | bool | `true` | Cancel any drag originating from a shell-file window |
| `EnableLegacyContextMenuFallback` | bool | `true` | Disable the Win11 modern menu host |
| `RestartExplorerOnPolicyChange` | bool | `true` | Restart Explorer after applying / restoring the menu policy |

The factory-default password is `PharmaGuard@123`. The first-run dialog
refuses to keep it.

---

## Building from source

### Prerequisites

| Need | Why | How |
|---|---|---|
| Windows 10 / 11 | Compiler host | — |
| .NET Framework **4.8** | Target framework | Pre-installed on Win10 1903+ / Win11 |
| .NET Framework 4.8 **targeting pack** | Reference assemblies | [Microsoft download](https://dotnet.microsoft.com/download/dotnet-framework/net48) → "Developer Pack" |
| MSBuild ≥ 15.0 | Build engine | Visual Studio Build Tools 2019/2022 (free) |
| WiX Toolset v3.11 or v3.14 | MSI build (optional) | `choco install wixtoolset` |

If only the legacy `.NET Framework 4.0.30319` MSBuild is available, the
project still builds (no Roslyn-only language features are used).

### Build

```cmd
build.cmd
```

Output:

```
PharmaDataGuard\bin\Release\PharmaDataGuard.exe                        ~48 KB
PharmaDataGuard\bin\Release\PharmaDataGuard.exe.config
PharmaDataGuard.LogVerifier\bin\Release\PharmaDataGuard.LogVerifier.exe ~7 KB
```

### Build the MSI

```cmd
cd installer
build-msi.cmd
```

Output: `installer\PharmaDataGuard.msi`. Per-machine install,
`%ProgramFiles%\PharmaDataGuard\` install path, autostart at login via
`HKLM\…\Run`.

---

## Running for the first time

1. Build, or copy `PharmaDataGuard.exe` + `PharmaDataGuard.LogVerifier.exe`
   to the target machine.
2. Right-click `PharmaDataGuard.exe` → **Run as administrator** (or accept
   the UAC prompt on double-click).
3. **First-Run Setup** dialog appears: enter and confirm a strong
   administrator password. Do not lose it — there is no in-app reset.
4. The tray shield icon appears (drag it from the Win11 chevron overflow
   onto the always-visible taskbar if needed).
5. Balloon tip confirms: *"Copy / Paste / Delete are locked for audit."*
6. Explorer restarts once (~1 second) to apply the modern-menu policy.

The program is now active. Try `Ctrl+C` in any application — nothing
copies. Right-click in Explorer — Copy / Cut / Paste / Delete are greyed
out. Drag a file — the file does not lift off.

To exit: tray → **Exit (requires password)**.

---

## Password management

### First launch
`FirstRunDialog` opens automatically when the stored hash matches the
factory default. Cannot be bypassed.

### Unlock (used for Add path, Change password, Exit)
`UnlockDialog` — single password field, 5-attempt limit, constant-time
hash comparison.

### Changing the password
Tray → **Change password…** → enter current → enter new (twice). Same
complexity rules as first-run, plus must differ from current. On
success: new salt generated, PBKDF2 re-derived, `config.xml` rewritten
atomically, `INFO | AUTH | Administrator password changed` logged.

### Forgot the password
There is **no in-app recovery** by design. Procedure:

```powershell
# from an elevated shell
Get-Process -Name PharmaDataGuard | Stop-Process -Force
del /q "%ProgramData%\PharmaDataGuard\config.xml"
# relaunch the exe; first-run dialog reappears
```

This loses the `ProtectedPaths` list. The audit log and `AclBackup\`
directory are preserved.

---

## Deployment

### Per-machine MSI (recommended for kiosks)

```cmd
msiexec /i installer\PharmaDataGuard.msi /qn
```

Installs to `%ProgramFiles%\PharmaDataGuard\`, sets the `HKLM\…\Run`
autostart, registers the upgrade code for clean future updates.

### As a Windows service (LocalSystem)

```cmd
install-service.cmd
sc start PharmaDataGuard
```

Auto-recovery configured: restart in 5 seconds on each of the first
three failures.

### Manual / portable

Copy the EXEs anywhere, launch as administrator. No installer required.

---

## Validation (21 CFR Part 11 / EU GMP Annex 11)

`pharma-data-guard-21cfr` is designed to **support** validation by the
deploying organization, not to be pre-certified. Compliance
determinations are the QA / regulatory function's responsibility.

### Validation workflow

1. **IQ (Installation Qualification)**: deploy via MSI on a signed-off
   build-machine image. Capture: file hashes, code-sign certificate
   fingerprint, MSI version, deployment timestamp.

2. **OQ (Operational Qualification)**: run `test-harness.ps1`. Generates
   `OQ-Report.md` with pass/fail rows for each test:

   | Test ID | What it proves |
   |---|---|
   | PRE-01 | Process is running |
   | OQ-01 | Clipboard wipes after .NET `Clipboard.SetText` |
   | OQ-03 | Clipboard wipes after PowerShell `Set-Clipboard` |
   | OQ-05 | NTFS DENY ACE blocks `Remove-Item` |
   | OQ-06 | `DisableTaskMgr=1` policy applied |
   | OQ-07 | Watchdog respawns after `Stop-Process` |
   | OQ-09 | Log verifier returns `0` for intact log and `1` after byte tamper |

3. **PQ (Performance Qualification)**: deploy on the production audit
   kiosk. Audit dry-run with operators. Confirm zero unexpected AV/EDR
   alerts.

### Audit-day operator procedure

1. Operator launches the program (or it autostarts at login via the MSI).
2. First-run? Set the strong password.
3. Operator adds the audit's specific source-data folders via tray →
   Add protected path…
4. Auditor performs work. Every block is logged.
5. After audit: tray → Exit (requires password).
6. Run `PharmaDataGuard.LogVerifier.exe` to confirm log integrity.
7. Run `test-harness.ps1` to generate `OQ-Report.md`.
8. Archive in the validation binder:
   - `pharma-data-guard.log`
   - `OQ-Report.md`
   - Verifier exit code (0 = intact)
   - Code-sign certificate details
   - Operator + QA signatures on the OQ report

### 21 CFR Part 11 §11.10 mapping

| Sub-clause | Contribution |
|---|---|
| §11.10(a) System validation | Open source; full source, build, OQ harness available |
| §11.10(b) Accurate copies | Out of scope — the program constrains operations, does not produce records |
| §11.10(c) Protection of records | Layer 4 NTFS DENY ACEs prevent deletion / modification |
| §11.10(d) Limiting access | Layer 1 / 2 / 5 limit exfiltration channels |
| **§11.10(e) Audit trail** | **HMAC-SHA256-chained log + standalone verifier** |
| §11.10(g) Authority checks | Password-gated exit and configuration changes |
| §11.10(h) Device checks | Out of scope — host hardening |
| §11.10(k) Documentation control | Out of scope — DMS responsibility |

### EU GMP Annex 11 mapping

| Section | Contribution |
|---|---|
| §6 Accuracy checks | Out of scope |
| §7.1 Data integrity | Layer 4 prevents unauthorised deletion / modification |
| §9 Audit trails | HMAC-chained log + verifier |
| §12 Security | Password-gated administrative actions |
| §15 Change control | `config.xml` operator-initiated changes; password change events logged |

### ALCOA+ data-integrity mapping

| Principle | Contribution |
|---|---|
| **A**ttributable | `WindowsIdentity.GetCurrent().Name` on every log line |
| **L**egible | Plain-text UTF-8 log; ISO-8601 timestamps; standardised format |
| **C**ontemporaneous | Events logged synchronously, ms-precision UTC |
| **O**riginal | The log file IS the original record; HMAC chain makes copies verifiable |
| **A**ccurate | Block events report the exact key combination, foreground process, PID |
| **C**omplete | Every blocked action is logged; no sampling |
| **C**onsistent | Hash chain enforces order; chronological timestamps |
| **E**nduring | File is append-only; not deleted by uninstall |
| **A**vailable | Standard filesystem path; standalone verifier executable |

---

## Anti-virus and EDR

The program's behaviour is, by design, **indistinguishable from malware
in isolation**:

| Behavior | What it looks like to an EDR |
|---|---|
| `SetWindowsHookEx(WH_KEYBOARD_LL)` | Keylogger |
| `SetWindowsHookEx(WH_MOUSE_LL)` | Mouse logger |
| `AddClipboardFormatListener` + `EmptyClipboard` | Clipboard hijacker |
| Hook callback returns 1 (swallows input) | Input-blocking malware |
| NTFS Deny ACE on user data | Ransomware |
| `Process.Kill(explorer.exe)` + restart | Defense evasion |
| `DisableTaskMgr` registry write | Tampering with security tools |
| Watchdog respawn | Persistence mechanism |
| Hidden form, no taskbar | Stealth indicator |
| Singleton mutex | Malware single-instance pattern |
| `requireAdministrator` + unsigned | Privilege escalation by unverified publisher |

### Strategy

The path that works for any regulated-industry endpoint security tool:

1. **Code-sign** all binaries with an **EV code-signing certificate**
   issued to your organization. The GitHub Actions workflow has the
   signing block wired — supply `SIGNING_CERT_PFX_BASE64` and
   `SIGNING_CERT_PASSWORD` as repository secrets.
2. **Submit the signed hash** to vendor reputation services
   (Microsoft SmartScreen at `microsoft.com/wdsi/filesubmission`,
   CrowdStrike, Sophos, SentinelOne enterprise allowlist portals).
3. **Allowlist by certificate or hash** in the customer's AV / EDR
   console *before* deployment to the kiosk.
4. **Document** as a managed application in the customer's CMDB and
   SOC runbook so audit-time IOA alerts are recognized and stood down
   rather than escalated.

### Per-vendor expectations (unsigned, fresh build)

| Vendor | At-rest | Runtime |
|---|---|---|
| Microsoft Defender | Medium-High | High (SmartScreen + cloud MAPS) |
| CrowdStrike Falcon | Low | High (behavioral IOAs) |
| Sophos Intercept X | Medium | High (CryptoGuard + behavioral) |
| SentinelOne | Low | High (behavioral) |
| Cylance / BlackBerry Protect | Medium-High | Medium (static ML) |
| Trellix EDR | Low | Medium |

The annual cost of an EV cert ($300–500) is far less than one SOC-analyst
hour spent investigating "is this malware."

---

## Threat model

### What the program defends against

- A non-malicious user attempting to copy / cut / paste / delete / drag
  / screen-capture audit-time data using the standard Windows UI surface
- A user who tries to terminate the program via Task Manager,
  `taskkill`, or PowerShell while in an authorised user session
- A user who attempts to bypass the audit period by forcing a crash —
  watchdog respawn within 1 second
- After-the-fact log tampering — HMAC chain detects every modified line

### What it does NOT defend against

| Vector | Rationale |
|---|---|
| Kernel-level attacker (malicious driver) | User-mode software cannot defend below the kernel |
| In-page browser context menus (Chromium "Copy", "Save image as") | Browser draws its own menu inside the page |
| External hardware capture (phone, HDMI capture, screen-share) | Out of scope — addressed by audit-room policy (no phones, no auxiliary monitors, no external storage) |
| Determined administrator with a second elevated session | Administrator can do anything user-mode software can prevent |
| DLL injection by a peer-elevated process | Sufficient to NOP the hook callbacks |
| OCR of the screen by a coordinating peer process | The program does not affect screen rendering |
| Network-based exfiltration (covert channels) | Out of scope — host-DLP problem |

### Recommended environmental controls

For a defensible **pharma audit kiosk**, combine with:

- **Host hardening**: BitLocker, Secure Boot, AppLocker / WDAC / Smart App Control
- **Network restriction**: kiosk on isolated VLAN, firewall locked
- **Physical controls**: locked audit room, no phones / cameras, no auxiliary storage
- **AV / EDR**: deployed and the program allowlisted (see above)
- **DLP**: complementary network-DLP if data exfiltration is the primary risk
- **Audit-room SOP**: documented operator and auditor procedures

---

## Architecture

### Repository layout

```
pharma-data-guard-21cfr/
├── PharmaDataGuard.sln
├── build.cmd
├── install-service.cmd
├── uninstall.cmd
├── test-harness.ps1
├── README.md
├── LICENSE
│
├── PharmaDataGuard/                      ← main tray application
│   ├── PharmaDataGuard.csproj
│   ├── app.manifest                      ← UAC, OS compat, DPI
│   ├── App.config
│   ├── Program.cs                        ← entry, mutex, watchdog dispatch
│   ├── MainForm.cs                       ← hidden form, tray, orchestration
│   ├── Properties/AssemblyInfo.cs
│   ├── Core/
│   │   ├── KeyboardHook.cs               ← Layer 1
│   │   ├── ClipboardGuard.cs             ← Layer 2
│   │   ├── ContextMenuGuard.cs           ← Layer 3
│   │   ├── FileGuard.cs                  ← Layer 4 (electronic-records protection)
│   │   ├── MouseGuard.cs                 ← Layer 5
│   │   ├── PolicyManager.cs              ← DisableTaskMgr
│   │   ├── ModernMenuPolicy.cs           ← Win11 menu host CLSID
│   │   ├── Watchdog.cs                   ← sibling-process supervisor
│   │   ├── AuditLogger.cs                ← HMAC-chained file appender
│   │   └── AppConfig.cs                  ← XML-serialised settings
│   └── UI/
│       ├── UnlockDialog.cs
│       ├── FirstRunDialog.cs
│       └── ChangePasswordDialog.cs
│
├── PharmaDataGuard.LogVerifier/          ← standalone verifier
│   ├── PharmaDataGuard.LogVerifier.csproj
│   └── Program.cs
│
├── installer/
│   ├── PharmaDataGuard.wxs               ← WiX v3 MSI source
│   └── build-msi.cmd
│
└── .github/workflows/
    └── build.yml                          ← MSBuild + WiX + optional signing
```

### Process model

```
PharmaDataGuard.exe (main, parent of watchdog)
├── Holds Global\PharmaDataGuard_Singleton mutex
├── UI thread: tray, hooks, timers
├── Background thread: Watchdog.SelfWatchLoop
└── spawns →
    └── PharmaDataGuard.exe --watchdog <main-pid>
        └── single thread: monitor parent, relaunch on death, exit self
```

### Single-instance enforcement

```
Global\PharmaDataGuard_Singleton
```

Cross-session named mutex. `Global\` prefix ensures uniqueness across
user sessions (matters for service deployments).

---

## Win32 API surface

Full list of Win32 functions called via P/Invoke. Useful for security
review and AV-vendor allowlist requests.

| API | Module | Layer | Purpose |
|---|---|---|---|
| `SetWindowsHookEx` | user32 | KB, Mouse | Install low-level hooks |
| `UnhookWindowsHookEx` | user32 | KB, Mouse | Remove hooks |
| `CallNextHookEx` | user32 | KB, Mouse | Pass events through |
| `GetModuleHandle` | kernel32 | KB, Mouse | Hook module handle |
| `GetAsyncKeyState` | user32 | KB | Modifier key state |
| `GetForegroundWindow` | user32 | KB | Foreground window |
| `GetWindowThreadProcessId` | user32 | KB, Mouse | Process attribution |
| `AddClipboardFormatListener` | user32 | Clipboard | Subscribe to changes |
| `RemoveClipboardFormatListener` | user32 | Clipboard | Unsubscribe |
| `OpenClipboard` | user32 | Clipboard | Acquire clipboard |
| `EmptyClipboard` | user32 | Clipboard | Wipe |
| `CloseClipboard` | user32 | Clipboard | Release |
| `CountClipboardFormats` | user32 | Clipboard | Detect already-empty |
| `FindWindow` | user32 | Menu | Locate `#32768` popup |
| `IsWindowVisible` | user32 | Menu | Filter invisible |
| `SendMessage` | user32 | Menu | `MN_GETHMENU` query |
| `GetMenuItemCount` | user32 | Menu | Walk items |
| `GetMenuString` | user32 | Menu | Read item text |
| `GetMenuState` | user32 | Menu | Filter separators |
| `EnableMenuItem` | user32 | Menu | Apply `MF_GRAYED \| MF_DISABLED` |
| `SetWinEventHook` | user32 | Drag | `EVENT_OBJECT_DRAGSTART` listener |
| `UnhookWinEvent` | user32 | Drag | Remove listener |
| `WindowFromPoint` | user32 | Drag | Class detection at click point |
| `GetAncestor` | user32 | Drag | Root window resolution |
| `GetClassName` | user32 | Drag | Class against shell-file allowlist |
| `GetSystemMetrics` | user32 | Drag | `SM_CXDRAG` / `SM_CYDRAG` thresholds |
| `SendInput` | user32 | Drag | Inject `VK_ESCAPE` |
| `PostMessage` | user32 | Drag | `WM_CANCELMODE` |

Plus standard managed APIs:

- `System.Security.AccessControl.FileSystemAccessRule` (Layer 4)
- `Microsoft.Win32.Registry.CurrentUser` (Policy + ModernMenu)
- `System.Diagnostics.Process` (Watchdog + Explorer restart)
- `System.Security.Cryptography.HMACSHA256` (audit log)
- `System.Security.Cryptography.Rfc2898DeriveBytes` (PBKDF2 password)
- `System.Security.Cryptography.RandomNumberGenerator` (salt)

---

## Registry, filesystem, and process footprint

### Files written

| Path | Lifecycle | Content |
|---|---|---|
| `%ProgramData%\PharmaDataGuard\config.xml` | Created on first run, updated on tray actions | XML settings + password hash + salt |
| `%ProgramData%\PharmaDataGuard\pharma-data-guard.log` | Append-only, never deleted by the program | Lifecycle and BLOCK events |
| `%ProgramData%\PharmaDataGuard\AclBackup\<key>.sddl` | One file per protected path | Original SDDL before DENY ACE applied |
| `%ProgramFiles%\PharmaDataGuard\` | MSI install only | Three binaries |

### Registry keys touched

| Key | Hive | Lifecycle | Purpose |
|---|---|---|---|
| `Software\Microsoft\Windows\CurrentVersion\Policies\System\DisableTaskMgr` | HKCU | Set to `1` at start, restored on exit | Block Task Manager kill UI |
| `Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32 (Default)` | HKCU | Set to `""` at start, restored on exit | Disable Win11 modern context menu |
| `Software\Microsoft\Windows\CurrentVersion\Run\PharmaDataGuard` | HKLM | Set by MSI install, removed by uninstall | Auto-start at login |

### Processes spawned

| Process | When | Why |
|---|---|---|
| `PharmaDataGuard.exe --watchdog <pid>` | Once at start | Sibling supervisor |
| `explorer.exe` (kill + auto-restart) | Once at start, once at exit | Apply / restore modern-menu policy |
| `notepad.exe` | When user clicks "Open audit log" | Display the log |

### Network

The program makes **no network connections**. Does not phone home, does
not check for updates, does not transmit logs. All communication is
local file I/O.

---

## Uninstall

```cmd
uninstall.cmd /force
```

`/force` parses `config.xml` and removes the DENY ACEs from each
protected path before deleting the config.

The script:
1. `taskkill /F /IM PharmaDataGuard.exe`
2. `sc stop PharmaDataGuard` + `sc delete PharmaDataGuard` if installed as service
3. Removes DENY ACEs from `ProtectedPaths` (with `/force`)
4. Deletes `HKLM\…\Run\PharmaDataGuard`
5. Deletes `HKCU\…\Policies\System\DisableTaskMgr`
6. Deletes `HKCU\…\CLSID\{86ca1aa0-…}` (modern-menu suppression)
7. Deletes `%ProgramData%\PharmaDataGuard\config.xml`
8. Deletes `%ProgramData%\PharmaDataGuard\AclBackup\`

The audit log at `%ProgramData%\PharmaDataGuard\pharma-data-guard.log`
is **preserved** for forensic continuity.

---

## Known issues

| Issue | Cause | Workaround |
|---|---|---|
| Marquee-rectangle file selection in Explorer is blocked | Drag-drop guard cannot distinguish file-drag from selection-drag | `Ctrl+click` / `Shift+click` to multi-select |
| Explorer flickers / restarts at launch and exit | Modern-menu policy requires Explorer reload | Set `RestartExplorerOnPolicyChange=false`; policy takes effect at next login |
| `MSB3644` warning when building with legacy MSBuild | .NET 4.8 reference assemblies missing | Install .NET 4.8 Developer Pack |
| AV (Defender / CrowdStrike / Sophos) flags / blocks the binary | Behaviour overlaps malware indicators | Code-sign + submit + allowlist (see [Anti-virus and EDR](#anti-virus-and-edr)) |
| Right-click in browser pages shows Copy / Save image as | Browser-internal menus are Chromium widgets | Out of scope; combine with browser policy controls |

---

## Roadmap

In rough priority order:

1. **Process self-DACL** (replace `DisableTaskMgr`). Apply a kernel-level
   DACL that denies `PROCESS_TERMINATE` to non-admin users. Significantly
   lower AV alerting than the registry policy.
2. **HMAC key sealing**. Move the audit-log key to a TPM-sealed store or
   an external signing service for environments with sophisticated adversaries.
3. **Listview hit-test in drag-drop guard**. Distinguish "click on a
   file item" from "click in empty listview area" so marquee select works.
4. **Browser context-menu coverage** (research-grade). Browser extension
   or WebView2 accessibility integration.
5. **Localisation expansion**. Marathi, Tamil, Telugu, Kannada, Punjabi,
   Bengali context-menu prefixes.
6. **IQ/OQ/PQ documentation pack**. Word/PDF templates for QA submission.

---

## FAQ

**Q: Can I run without admin privileges?**
A: No. The manifest requires `requireAdministrator`. Low-level hooks,
ACL writes, and registry policy all need it.

**Q: Does it slow down the system?**
A: Not measurably on modern hardware. Hot paths are tight; disk I/O is
the audit log only.

**Q: Will it interfere with the auditor's work?**
A: No, by design. The auditor reads data on-screen — the program blocks
*operations* on that data, not reading.

**Q: Can multiple instances run on the same machine?**
A: No. The `Global\PharmaDataGuard_Singleton` mutex enforces
single-instance system-wide.

**Q: Does the watchdog mean I can never close the program?**
A: No. The watchdog only respawns on *unexpected* termination. On
authorised exit (tray → Exit + password), the main process kills the
watchdog before exiting itself.

**Q: Does it work with Remote Desktop / Citrix / VDI?**
A: Yes for the host session; remote clipboard/keyboard behaviour
depends on the remote-protocol redirection settings.

**Q: Is this a replacement for AV / DLP?**
A: No. It is a **focused pharma compliance endpoint lockdown** for the
specific actions a 21 CFR Part 11 audit forbids on a workstation.
Deploy alongside AV and DLP, not instead.

**Q: Does it work on Windows Server?**
A: Untested. The manifest declares Win 7 / 8 / 8.1 / 10 / 11 only.

---

## Status and disclaimer

This is functioning, tested software intended for production audit
deployment.

It is **not pre-certified** as compliant with any specific regulation —
**21 CFR Part 11**, **EU GMP Annex 11**, **MHRA**, **WHO PQ**, **GDPR**,
**HIPAA**, or otherwise. Compliance determinations are the responsibility
of the deploying organization's QA, regulatory, and security functions.
The codebase, audit-log format, and verifier are designed to **support**
such certification.

The author(s) make no warranty as to fitness for any particular regulatory
purpose. Use at your own risk and validate before relying on it for
regulated work. See `LICENSE` for full terms.

---

## Contributing

Issues and pull requests are welcome. See [`CONTRIBUTING.md`](CONTRIBUTING.md)
for the full guide.

Quick summary:

- **Bug reports**: include OS version, commit hash, audit-log excerpt
  (with HMAC lines), and reproduction steps.
- **Pull requests**: keep them focused. Add a corresponding `OQ-XX` test
  case in `test-harness.ps1` if the change is in a defensive layer.
- **Localisation**: add prefixes to `ContextMenuGuard.ForbiddenPrefixes`
  with a comment naming the script.
- **Security issues**: do **not** file public issues — see
  [`SECURITY.md`](SECURITY.md) for private reporting.

Code style: match the surrounding C# 5 style (no expression-bodied
members, no auto-property initializers).

This project follows the [Contributor Covenant 2.1](CODE_OF_CONDUCT.md).

---

## License

**Apache-2.0.** See [`LICENSE`](LICENSE) for full text and [`NOTICE`](NOTICE)
for attribution.

Copyright © 2026 [Sriji Computers](https://github.com/SrijiComputers).

The Apache-2.0 license includes a patent grant clause, which is
appropriate for industrial and **pharma compliance** use.

Related project files:

- [`SECURITY.md`](SECURITY.md) — vulnerability reporting policy
- [`CONTRIBUTING.md`](CONTRIBUTING.md) — contribution guidelines
- [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md) — Contributor Covenant 2.1

---

## Keywords for search

`21 CFR Part 11`, `21 CFR Part 11 compliance`, `21 CFR Part 11 software`,
`21 CFR Part 11 data integrity`, `21 CFR Part 11 audit trail`,
`pharma compliance`, `pharmaceutical compliance software`, `pharma data
integrity`, `pharmaceutical electronic records`,
`GxP compliance`, `GMP compliance`, `GLP compliance`, `GxP data
integrity`, `GxP electronic records protection`,
`ALCOA`, `ALCOA+`, `ALCOA plus`, `ALCOA principles`,
`FDA compliance`, `FDA 21 CFR Part 11`, `FDA endpoint protection`,
`EMA compliance`, `EU GMP Annex 11`, `MHRA data integrity`, `WHO PQ`,
`copy protection`, `disable copy paste`, `block copy paste cut delete`,
`prevent file deletion Windows`, `block clipboard Windows`,
`block screen capture Windows`, `block PrintScreen`, `disable Snipping Tool`,
`endpoint lockdown`, `endpoint protection pharma`, `endpoint security
pharma`, `pharmaceutical kiosk security`,
`audit kiosk`, `audit workstation lockdown`, `regulatory audit kiosk`,
`pharma audit lockdown`, `pharma audit endpoint`,
`tamper-evident audit log`, `HMAC audit log`, `cryptographic audit
trail`, `21 CFR Part 11 audit trail software`,
`data loss prevention pharma`, `DLP pharma`, `pharmaceutical DLP`,
`Windows endpoint protection`, `Windows lockdown software`,
`open source pharma compliance`, `open source 21 CFR Part 11`,
`Indian pharma compliance`, `pharma kiosk India`,
`AutoHotkey replacement pharma`, `VB macro replacement pharma`,
`replace Pharma copy paste delete script`.

---

## References

### Regulations

- [21 CFR Part 11 — Electronic Records; Electronic Signatures](https://www.ecfr.gov/current/title-21/chapter-I/subchapter-A/part-11) — particularly **§11.10(e) audit-trail requirements**
- [EU GMP Annex 11 — Computerised Systems](https://health.ec.europa.eu/system/files/2016-11/annex11_01-2011_en_0.pdf)
- [MHRA GxP Data Integrity Definitions and Guidance for Industry](https://assets.publishing.service.gov.uk/media/5be7a0b340f0b67d51b8c4f1/MHRA_GxP_data_integrity_guide_March_edited_Final.pdf)
- [WHO Annex 5 — Guidance on Good Data and Record Management Practices](https://www.who.int/publications/m/item/annex-5-trs-no-996)
- [PIC/S PI 041-1 Good Practices for Data Management and Integrity in Regulated GMP/GDP Environments](https://picscheme.org/docview/4234)

### Win32 / .NET documentation

- [SetWindowsHookEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexw)
- [Low-Level Keyboard Procedure (`KeyboardProc`)](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc)
- [AddClipboardFormatListener](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-addclipboardformatlistener)
- [SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook)
- [Drag-and-Drop accessibility events](https://learn.microsoft.com/en-us/windows/win32/winauto/event-constants)
- [Access Control Lists](https://learn.microsoft.com/en-us/windows/win32/secauthz/access-control-lists)

### Cryptography

- [PBKDF2 (RFC 2898)](https://datatracker.ietf.org/doc/html/rfc2898)
- [HMAC (RFC 2104)](https://datatracker.ietf.org/doc/html/rfc2104)

### Industry context

- [Microsoft: Context-menu handlers](https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers)
- [WiX Toolset v3 documentation](https://wixtoolset.org/docs/v3/)
- [Smart App Control & SmartScreen reputation](https://learn.microsoft.com/en-us/windows/security/threat-protection/microsoft-defender-smartscreen/microsoft-defender-smartscreen-overview)
