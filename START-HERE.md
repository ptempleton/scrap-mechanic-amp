# Scrap Mechanic AMP review package

Version: **0.1.0-review.3**. Windows-only community integration, MIT license.
This is a working prototype for CubeCoders review, not an official AMP release.

This update fixes false pending-launch locks when Steam or executable prerequisites
are missing. Existing installations must update both the agent and AMP bridge
binaries; see **Removal and updates** in [INSTALL.md](INSTALL.md).

Get the complete ZIP and checksum from the
[GitHub releases page](https://github.com/ptempleton/scrap-mechanic-amp/releases).
The repository and GitHub's automatic source archives contain source only;
the separately attached review ZIP includes the built Windows helper.

## What is included

- `amp/`: the five application template files and a local repository manifest.
- `artifacts/publish/`: prebuilt portable launcher DLLs, dependency metadata and
  runtime configuration. Install the **.NET 10 x64 runtime** on Windows; no SDK
  is needed to use these binaries. There is intentionally no `.exe` in this build:
  the installer configures AMP and the agent task to run `dotnet.exe` with the DLL.
- `src/`, `tests/`, `scripts/`: complete source, tests, build/package scripts,
  Windows installation and conservative removal scripts.
- `config/`: examples with placeholder Windows account SIDs.
- `INSTALL.md`, `TESTING.md`, `TROUBLESHOOTING.md`: installation, acceptance and
  operation/recovery instructions, including manual backup restore.
- `docs/MAINTAINER-REVIEW.md`: architecture and differences from upstream rules.
- `docs/SUBMISSION-DRAFT.md`: a proposal to send to CubeCoders after review.
- `docs/VALIDATION.md`: shareable evidence and remaining test coverage.
- `PACKAGE-MANIFEST.json`: source revision and per-file SHA-256 hashes.

Private deployment notes, Git history, host settings, saves, credentials, build
machine paths, debug symbols and the .NET runtime are excluded. The package's
`docs/TEST-RESULTS.md` points to the public evidence summary.

## Try it on Windows

1. Install AMP, .NET 10 x64 runtime, Steam and a licensed copy of Scrap Mechanic.
   Sign in to Steam as the normal Windows game user.
2. Check the ZIP against its adjacent `.sha256` file with `Get-FileHash -Algorithm
   SHA256`. Extract to a new directory. With Python 3, optionally run
   `python scripts/package-release.py verify PATH-TO-THE-ORIGINAL-ZIP` to check
   all archived files against the package manifest.
3. Start the game normally, load a disposable Survival world, and exit. Leave
   Steam Launch Options blank: AMP supplies `-dedicated_server -last_save`.
4. Follow [INSTALL.md](INSTALL.md), starting at **Install the local AMP template**.
   The included build is already at the installer's default `artifacts/publish`
   location. Windows PowerShell 5.1 is sufficient for installation; administrator
   rights are needed to register the logon task and set file access.
5. Register `LOCAL/ScrapMechanic:main` in ADS, create the instance stopped, stop
   the AMP instance itself during agent installation, then install for the actual
   game user and AMP **Windows** account. An AMP web username is not a Windows SID.
6. Configure paths in AMP, start the user-session agent task, test its `status`
   command, and click AMP Start. Verify a second player can connect.

The game user must remain logged in to Windows with Steam running. AMP's Running
label observes a process; CPU/memory charts describe the bridge and player counts
are not implemented. Stop requests WM_CLOSE and, by default, creates a verified
backup after process exit. Neither exit nor backup verification proves a save flush.

## For CubeCoders

Start with [the maintainer review](docs/MAINTAINER-REVIEW.md) and
[the proposal](docs/SUBMISSION-DRAFT.md). Publishing this community repository
does not submit it to CubeCoders; the proposal remains unsent. The five lowercase application files in `amp/`
are the prospective template contribution; this whole source package is not meant
to be copied into the top level of AMPTemplates. The separate agent and its
installation/distribution requirements need maintainer agreement first.

## Rebuild

Use a clean checkout on Windows with .NET 10 x64 SDK, PowerShell 7 and Python 3:

```powershell
.\scripts\Test.ps1
.\scripts\Build.ps1
```

That produces a native Windows apphost with framework-dependent DLLs. To
reproduce the portable review package, see [docs/PACKAGING.md](docs/PACKAGING.md).
The source includes a Windows/Linux CI definition. Consult the
[Actions results](https://github.com/ptempleton/scrap-mechanic-amp/actions)
for the outcome of each run. CI does not exercise Steam or a live game world.
