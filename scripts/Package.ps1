[CmdletBinding()]
param(
    [string]$Version = '0.1.0-review.2',
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/releases')
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$revision = & git -C $projectRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Package from a Git checkout with committed source.' }
$changes = & git -C $projectRoot status --porcelain --untracked-files=normal
if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Commit or remove source changes before packaging.' }
$python = Get-Command python -ErrorAction SilentlyContinue
if (!$python) { $python = Get-Command python3 -ErrorAction Stop }
& (Join-Path $PSScriptRoot 'Test.ps1')
$staging = Join-Path $projectRoot ('artifacts/package-' + [guid]::NewGuid().ToString('N'))
$publish = Join-Path $staging 'publish'
New-Item -ItemType Directory -Path $publish -Force | Out-Null
$project = Join-Path $projectRoot 'src/ScrapMechanic.Host/ScrapMechanic.Host.csproj'
$common = @('-c', 'Release', '--nologo', '-m:1', '-nodeReuse:false',
    '-p:UseSharedCompilation=false', '-p:UseAppHost=false', '-p:DebugType=None', '-p:DebugSymbols=false')
# Force recompilation: an incremental publish can retain earlier PDB path references.
& dotnet build $project --no-incremental @common
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
& dotnet publish $project --self-contained false -o $publish @common
if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }
& dotnet (Join-Path $publish 'ScrapMechanic.Host.dll') --help
if ($LASTEXITCODE -ne 0) { throw 'Published launcher help smoke test failed.' }
$report = Join-Path $staging 'PACKAGE-VERIFICATION.md'
@"
# Package verification

Built by scripts/Package.ps1 from committed revision $revision.
Version: $Version. UTC: $([DateTime]::UtcNow.ToString('u')).

- scripts/Test.ps1 completed successfully on this builder. Its console lists any
  skipped platform-dependent tests; a successful script does not imply they ran.
- Fresh Release build and portable publish completed successfully.
- The published launcher --help command exited successfully.
- Package creation verifies its inventory, hashes, safe paths and documentation
  links. See docs/VALIDATION.md for prior Windows/game evidence and open checks.

This packaging run does not install or test the game on a live host.
"@ | Set-Content -LiteralPath $report -Encoding utf8
& $python.Source (Join-Path $PSScriptRoot 'package-release.py') build `
    --publish-directory $publish --verification-report $report `
    --output-directory $OutputDirectory --version $Version --source-revision $revision
if ($LASTEXITCODE -ne 0) { throw 'Packaging or archive verification failed.' }
Write-Host 'Review ZIP and SHA-256 file created. Nothing was installed, uploaded, or published.'
