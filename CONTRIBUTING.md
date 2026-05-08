# Contributing to pharma-data-guard-21cfr

Thank you for your interest in contributing. This is a regulated-industry
endpoint security tool, so contribution standards are tighter than a
typical open-source project. Read this guide before opening a PR.

## Ways to contribute

- **Bug reports** — file a public issue (non-security) with reproduction steps
- **Security reports** — use the private channel in [`SECURITY.md`](SECURITY.md)
- **Pull requests** — bug fixes, defensive-layer extensions, localisation
- **Documentation** — README clarifications, validation-pack improvements
- **Localisation** — context-menu prefix entries for additional languages

## Bug reports

When filing a bug report, include:

- **OS version**: Windows 7 SP1 / 10 / 11, build number
- **Commit hash** or release version of `pharma-data-guard-21cfr`
- **Reproduction steps** — minimal, deterministic
- **Audit-log excerpt** — last 20–50 lines from
  `%ProgramData%\PharmaDataGuard\pharma-data-guard.log` (with HMAC lines
  preserved so we can verify chain integrity)
- **Configuration** — relevant settings from
  `%ProgramData%\PharmaDataGuard\config.xml`
- **AV / EDR** running on the test machine, if any (Defender, CrowdStrike,
  Sophos, etc. — interactions are common)

## Pull requests

### Guidelines

1. **Keep PRs focused.** One logical change per PR. Mixed PRs (e.g. layer
   change + style refactor) are hard to review against regulatory criteria.
2. **Match the existing code style.** This project targets C# 5 because
   the legacy .NET Framework 4.0.30319 MSBuild path requires it. Do **not**
   introduce:
   - Expression-bodied members (`=> ...`)
   - Auto-property initializers (`{ get; set; } = value`)
   - `out var` / `out int x` inline declarations
   - Null-conditional operator (`?.`)
   - String interpolation (`$"..."`)
   - Digit separators (`100_000`)
3. **No new dependencies** without prior discussion. The project deliberately
   has zero NuGet dependencies and uses only the .NET Framework 4.8
   base-class library.
4. **Add an OQ test case** in `test-harness.ps1` if your PR changes a
   defensive layer. Pattern: `OQ-XX | <human-readable description> | <pass condition>`.
5. **Update the README** if your PR changes user-facing behaviour, the
   configuration schema, or the audit-log format.
6. **Do not touch the HMAC chain format** without an explicit, reviewed
   migration plan. Existing deployed audit logs must remain verifiable.

### Code style

- Indent 4 spaces, no tabs
- Brace-on-new-line for type/method declarations (Allman style, matches surrounding code)
- `private` / `internal` for everything that doesn't need to be public
- P/Invoke declarations grouped at the top of the class that uses them
- `try` / `catch` around any external call that could throw at runtime
  (especially in hook callbacks — uncaught exceptions in low-level hooks
  crash the process)

### Defensive-layer changes

Changes to any of the five defensive layers (KeyboardHook, ClipboardGuard,
ContextMenuGuard, FileGuard, MouseGuard) must:

- Preserve the existing audit-log category names (`KEYBOARD`,
  `CLIPBOARD`, `MENU`, `FILEGUARD`, `DRAGDROP`, etc.) — auditors may
  have automated tooling that relies on them
- Not introduce additional log categories without README documentation
- Be tested on Windows 7 SP1, 10, and 11 if the change touches Win32 APIs
  whose behaviour differs across versions
- Include a short note in the PR description on the threat-model impact

## Localisation

To add support for a regional language to the **context-menu prefix
matcher** (Layer 3):

1. Open [`PharmaDataGuard/Core/ContextMenuGuard.cs`](PharmaDataGuard/Core/ContextMenuGuard.cs)
2. Find the `ForbiddenPrefixes` array
3. Append the translations for the forbidden actions (Copy, Cut, Paste,
   Delete, Move to, Send to, Share, Upload, Open with, Export, Save as,
   Save a copy, Print, Rename) for your language
4. Add a comment naming the script (Devanagari / Tamil / Telugu / etc.)
5. Test on a real Windows install with the corresponding display
   language so we know Windows actually returns those exact strings via
   `GetMenuString`

Currently included scripts: English, Hindi, Gujarati. Wanted: Marathi,
Tamil, Telugu, Kannada, Punjabi, Bengali, Odia, Malayalam.

## Building

See the [Building from source](README.md#building-from-source) section
of the README for prerequisites and build commands.

To run tests after a build:

```cmd
test-harness.ps1
```

This generates `OQ-Report.md` with pass/fail rows for each test case.
A passing OQ run is required for any PR that touches a defensive layer.

## Code of conduct

This project adheres to the [Contributor Covenant](CODE_OF_CONDUCT.md).
By participating, you are expected to uphold this code. Report
unacceptable behaviour to the maintainers via the security contact in
[`SECURITY.md`](SECURITY.md).

## License

By contributing, you agree that your contributions will be licensed
under the Apache License 2.0 (see [`LICENSE`](LICENSE)).
