# Contributing

Thank you for your interest in improving Hamilton Labware Converter.

## Ways to contribute

- Report reproducible bugs with the relevant Windows and .NET versions.
- Suggest focused improvements or describe a missing workflow.
- Improve documentation or add example files and commands.
- Submit fixes and tested feature changes.

## Before you start

- Check existing issues and pull requests before opening a new one.
- Open an issue before large or behavior-changing work so the approach can be discussed.
- Do not include production labware files, credentials, or other confidential data in reports or patches.

## Development setup

The application targets Windows/.NET Framework 4.5 and is built as a WinForms desktop executable.

Requirements:

- Windows
- .NET Framework 4.5 or later
- PowerShell
- Hamilton VENUS for features that depend on its registered components or Assimp library

Build from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
```

The build creates `LabwareToSTL.exe` in the repository root by default.

## Testing changes

There is no automated test suite yet. At minimum, test the affected workflow on Windows:

- For export changes, test both successful and missing-model cases.
- For STL import changes, test binary and ASCII STL files when applicable.
- For template changes, verify that the generated package is usable in VENUS and retains the selected template dependencies.
- For viewer changes, verify normal, fit-view, orbit, pan, and zoom behavior.

Do not assume a change is compatible with VENUS without testing it in VENUS where possible.

## Pull requests

1. Create a focused branch from `main`.
2. Keep changes small and describe the reason for them.
3. Update the README or inline help for changed behavior.
4. Include build and manual test results in the pull request.
5. Follow the existing C# and PowerShell formatting and naming conventions.

All participants must follow the [Code of Conduct](CODE_OF_CONDUCT.md).
