[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$pythonCommand = Get-Command python -ErrorAction SilentlyContinue
if (!$pythonCommand) { $pythonCommand = Get-Command python3 -ErrorAction Stop }
& dotnet build (Join-Path $projectRoot 'ScrapMechanic.slnx') --nologo -m:1 -nodeReuse:false -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& dotnet (Join-Path $projectRoot 'tests/ScrapMechanic.Host.Tests/bin/Debug/net10.0/ScrapMechanic.Host.Tests.dll')
if ($LASTEXITCODE -ne 0) { throw 'Launcher tests failed.' }
& $pythonCommand.Source (Join-Path $PSScriptRoot 'validate-template.py')
if ($LASTEXITCODE -ne 0) { throw 'Template checks failed.' }
& $pythonCommand.Source -m unittest discover -s (Join-Path $projectRoot 'tests') -p 'test_*.py'
if ($LASTEXITCODE -ne 0) { throw 'Template regression tests failed.' }
