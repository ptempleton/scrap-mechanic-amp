[CmdletBinding(SupportsShouldProcess)]
param([Parameter(Mandatory)][string]$AdsInstancePath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$templates = Join-Path $AdsInstancePath 'Plugins/ADSModule/DeploymentTemplates'
if (!(Test-Path -LiteralPath $templates -PathType Container)) { throw "Not an ADS instance deployment-template directory: $templates" }
$target = Join-Path $templates 'LOCAL-ScrapMechanic-main'
if (Test-Path -LiteralPath $target) { throw "Template directory already exists; preserve/review it before installing: $target" }
if ($PSCmdlet.ShouldProcess($target, 'Install local Scrap Mechanic template')) {
    New-Item -ItemType Directory -Path $target | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $projectRoot 'amp') -File | Copy-Item -Destination $target
    Write-Host "Installed $target. In ADS Configuration -> Instance Deployment -> Configuration Repositories, add LOCAL/ScrapMechanic:main. Preserve existing entries, refresh the catalogue/browser, then create the Windows template instance with start-on-boot disabled and After Creation set to Do Nothing."
}
