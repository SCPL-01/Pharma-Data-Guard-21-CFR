# Security Policy

## Reporting a vulnerability

`pharma-data-guard-21cfr` is endpoint security software used in regulated
pharmaceutical, GxP, and clinical-trial environments. Security issues are
taken seriously and handled privately to protect deployers.

**Please do not report security issues in public GitHub issues, pull
requests, or discussions.**

To report a vulnerability, use **one** of the following channels:

1. **GitHub Private Vulnerability Reporting** (preferred) — go to the
   repository's *Security* tab → *Report a vulnerability*. This creates a
   private advisory that only the maintainers can see.
2. **Email** — `security@srijicomputers.com` *(replace with your actual
   security contact email before publishing)*.

When reporting, include:

- A description of the vulnerability and its potential impact
- Step-by-step reproduction (binary version or commit hash, OS version,
  exact configuration that reproduces the issue)
- Any relevant audit-log excerpts (with HMAC lines preserved)
- Whether you have publicly disclosed the issue elsewhere

## What's in scope

- Bypasses of any of the five defensive layers (keyboard, clipboard,
  context menu, file-system ACL, drag-drop)
- Audit-log forgery, replay, or chain-break attacks
- Authorization bypasses on the password-gated tray actions (Exit, Add
  protected path, Change password)
- Cryptographic weaknesses in the PBKDF2 password handling or HMAC chain
- Privilege-escalation paths through `pharma-data-guard-21cfr` to other
  parts of the system
- Watchdog defeats that lead to silent termination of the program

## What's out of scope

These are documented limitations, not vulnerabilities:

- Kernel-level attackers (malicious drivers, anti-malware bypass tooling)
- Browser-internal context menus (Chromium-rendered "Copy" / "Save image
  as" inside web pages)
- External hardware capture (phone cameras, HDMI capture, screen-sharing
  applications)
- Determined administrators operating from a second elevated session
- DLL injection by a peer-elevated process
- OCR of the screen by a coordinating peer process
- Network-based exfiltration (covert channels via DNS, HTTP, etc.)
- Marquee-rectangle file selection in Explorer being blocked by the
  drag-drop guard (a known UX trade-off)

These are addressed by environmental controls (BitLocker, AppLocker /
WDAC, AV / EDR, audit-room SOP) — see the [Threat model](README.md#threat-model)
section of the README.

## Response timeline

- **Acknowledgement** — within 5 business days of receipt
- **Triage and severity assessment** — within 10 business days
- **Fix or mitigation plan** — within 30 days for critical / high
  severity issues, longer for lower severity
- **Public disclosure** — coordinated with the reporter; typically after
  a fix is available and deployed users have had a reasonable update
  window (60–90 days)

## Recognition

Security researchers who report valid vulnerabilities through the
private channels above will be credited in the release notes (with the
researcher's permission) once the issue is fixed and disclosed.

## Validation context

`pharma-data-guard-21cfr` is **not pre-certified** as compliant with any
specific regulation (21 CFR Part 11, EU GMP Annex 11, MHRA, etc.).
Compliance determinations are the deploying organization's responsibility.
A vulnerability that affects the audit-log integrity property may have
direct regulatory consequences for deployers — please report such issues
with the highest priority.
