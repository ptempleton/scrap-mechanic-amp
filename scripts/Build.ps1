[CmdletBinding()]
param([switch]$SelfContained, [switch]$Portable)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$publishDirectory = Join-Path $projectRoot 'artifacts/publish'
$temporaryPublish = Join-Path $projectRoot ('artifacts/publish-' + [guid]::NewGuid().ToString('N') + '.tmp')
$projectFile = Join-Path $projectRoot 'src/ScrapMechanic.Host/ScrapMechanic.Host.csproj'
$buildArguments = @('publish', $projectFile, '-c', 'Release', '-o', $temporaryPublish,
    '--nologo', '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false')
if ($SelfContained) {
    if ($Portable) { throw 'Choose SelfContained or Portable, not both.' }
    $buildArguments += @('-r', 'win-x64', '--self-contained', 'true')
} else {
    $buildArguments += @('--self-contained', 'false')
    if ($Portable) { $buildArguments += '-p:UseAppHost=false' }
}
try {
    & dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) { throw 'Build failed; previous publish output was retained.' }
    # Do not leave stale apphosts/runtime files when switching between publish modes.
    if (Test-Path -LiteralPath $publishDirectory) { Remove-Item -LiteralPath $publishDirectory -Recurse -Force }
    Move-Item -LiteralPath $temporaryPublish -Destination $publishDirectory
} finally {
    if (Test-Path -LiteralPath $temporaryPublish) { Remove-Item -LiteralPath $temporaryPublish -Recurse -Force }
}
Write-Host "Published to $publishDirectory. Copy the complete directory, not just the executable."
