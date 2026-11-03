# Building a review package

The package builder uses an explicit file list plus C#/project/test source files.
It excludes private `docs/TEST-RESULTS.md`, Git history, runtime configuration,
saves, logs, debug symbols, prior archives and unrelated files. The public audit
entry points to [VALIDATION.md](VALIDATION.md). A content scan rejects common
private deployment paths, LAN addresses and credential/session fields; this is
an additional check, not a general-purpose secret-detection guarantee.

## Normal checkout

Install .NET 10 SDK, PowerShell 7, Python 3 and Git. Commit source changes first.

```powershell
.\scripts\Package.ps1 -Version '0.1.0-review.2'
```

The script runs the automated suite, forces a fresh symbol-free Release build,
publishes portable DLLs, runs `--help`, and creates/validates the archive in
`artifacts/releases`. It runs on Windows or macOS/Linux; game operations remain
Windows-only. Native Windows tests are skipped on other platforms and a packaging
success does not mean the newly built archive has been live-tested on Windows.

An existing version ZIP/checksum is never overwritten. Use a new version or a
new output directory for another run. The manifest identifies the source commit,
runtime prerequisite, license, exact file inventory, lengths and SHA-256 hashes.
The manifest itself is covered by the external archive checksum. Hashes are not
publisher signatures. Identical inputs produce identical ZIP bytes.

For a checkout whose Git metadata lives outside the work tree, set `GIT_DIR` and
`GIT_WORK_TREE` to the intended metadata/work-tree paths before running the script.
The source archive omits Git history; initialize a fresh review repository and
commit the extracted source if you want to use this wrapper there.

## Explicit packaging

To package an already-tested, fresh portable build, create a truthful Markdown
verification report and pass the full committed source revision:

```powershell
python scripts/package-release.py build `
  --publish-directory artifacts/review-publish `
  --verification-report artifacts/package-verification.md `
  --version 0.1.0-review.2 `
  --source-revision FULL_40_CHARACTER_COMMIT
python scripts/package-release.py verify artifacts/releases/ScrapMechanic-AMP-0.1.0-review.2.zip
Get-FileHash artifacts/releases/ScrapMechanic-AMP-0.1.0-review.2.zip -Algorithm SHA256
```

The Python builder packages and verifies; it does not compile, run the test suite,
or independently prove that supplied binaries came from the stated commit. The
PowerShell wrapper performs the build/test steps. Keep build/test records with
the release. Native/self-contained EXE publishing remains available via
`scripts/Build.ps1`, but this review packager deliberately requires only the four
portable files and the separate .NET 10 runtime to keep redistribution simple.

No download URL, GitHub release, pull request, email or forum post is created by
either packaging script. See [MAINTAINER-REVIEW.md](MAINTAINER-REVIEW.md) before
offering the package to CubeCoders.
