[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$InstancePath,
    [Parameter(Mandatory)][string]$GameUser,
    [Parameter(Mandatory)][string]$AmpUser,
    [string]$InstallDirectory = 'C:\ScrapMechanicHost',
    [string]$PublishDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/publish'),
    [string]$TaskName = 'ScrapMechanicHost-Agent'
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Install-Host requires Windows.' }
if (!(Test-Path -LiteralPath (Join-Path $InstancePath 'GenericModule.kvp'))) { throw 'InstancePath must be the new, stopped Generic Module instance directory.' }
if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) { throw 'Task already exists; this installer does not overwrite it.' }
if (Test-Path -LiteralPath $InstallDirectory) { throw 'InstallDirectory must be new to avoid replacing an existing host.' }
if (Get-Process -Name ScrapMechanic -ErrorAction SilentlyContinue) { throw 'Close Scrap Mechanic before installation.' }
$gameSid = ([Security.Principal.NTAccount]$GameUser).Translate([Security.Principal.SecurityIdentifier]).Value
$ampSid = ([Security.Principal.NTAccount]$AmpUser).Translate([Security.Principal.SecurityIdentifier]).Value
$projectRoot = Split-Path $PSScriptRoot -Parent
$applicationRoot = Join-Path $InstancePath 'scrapmechanic'
foreach ($name in @('client.json', 'host-settings.json', 'ScrapMechanic.Host.dll', 'ScrapMechanic.Host.exe')) {
    if (Test-Path -LiteralPath (Join-Path $applicationRoot $name)) { throw "Refusing to overwrite $name in the instance." }
}
$hasExecutable = Test-Path -LiteralPath (Join-Path $PublishDirectory 'ScrapMechanic.Host.exe')
if (!(Test-Path -LiteralPath (Join-Path $PublishDirectory 'ScrapMechanic.Host.dll'))) { throw 'Publish output missing; run Build.ps1 first.' }
if (!$hasExecutable) {
    $dotnetExe = (Get-Command dotnet -ErrorAction Stop).Source
    if (!(& $dotnetExe --list-runtimes | Select-String '^Microsoft.NETCore.App 10\.')) { throw 'Portable publish requires the .NET 10 runtime.' }
}
function Set-PrivateDirectoryAcl([string]$Path, [bool]$AllowAmpRead) {
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $inherit = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    foreach ($sid in @($gameSid, 'S-1-5-18', 'S-1-5-32-544') | Select-Object -Unique) {
        $rule = [Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]$sid,
            [Security.AccessControl.FileSystemRights]::FullControl, $inherit,
            [Security.AccessControl.PropagationFlags]::None, [Security.AccessControl.AccessControlType]::Allow)
        $acl.AddAccessRule($rule)
    }
    if ($AllowAmpRead -and $ampSid -ne $gameSid) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]$ampSid,
            [Security.AccessControl.FileSystemRights]::ReadAndExecute, $inherit,
            [Security.AccessControl.PropagationFlags]::None, [Security.AccessControl.AccessControlType]::Allow))
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}
if (!$PSCmdlet.ShouldProcess("$InstallDirectory; $applicationRoot; scheduled task $TaskName", 'Install interactive host and bridge')) { return }
New-Item -ItemType Directory -Path $InstallDirectory | Out-Null
Set-PrivateDirectoryAcl $InstallDirectory $false
$agentBin = New-Item -ItemType Directory -Path (Join-Path $InstallDirectory 'bin')
$data = New-Item -ItemType Directory -Path (Join-Path $InstallDirectory 'Data')
$backup = New-Item -ItemType Directory -Path (Join-Path $InstallDirectory 'Backups')
Copy-Item -Path (Join-Path $PublishDirectory '*') -Destination $agentBin.FullName -Recurse
New-Item -ItemType Directory -Path $applicationRoot -Force | Out-Null
Copy-Item -Path (Join-Path $PublishDirectory '*') -Destination $applicationRoot -Recurse
# The game user reads the settings AMP writes. Retain AMP's existing parent ACL;
# add only read/execute on this new application subdirectory.
$appAcl = Get-Acl -LiteralPath $applicationRoot
$appAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]$gameSid,
    'ReadAndExecute', 'ContainerInherit, ObjectInherit', 'None', 'Allow'))
Set-Acl -LiteralPath $applicationRoot -AclObject $appAcl
$settings = Get-Content -LiteralPath (Join-Path $projectRoot 'config/host-settings.example.json') -Raw | ConvertFrom-Json
$settings.BackupDirectory = $backup.FullName
$settings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $applicationRoot 'host-settings.json') -Encoding utf8
$client = @{ PipeName = 'ScrapMechanicHost'; AgentIdentitySid = $gameSid; RequestTimeoutSeconds = 1800 }
$client | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $applicationRoot 'client.json') -Encoding utf8
$agentConfig = Join-Path $InstallDirectory 'agent.json'
@{ PipeName = 'ScrapMechanicHost'; AmpIdentitySid = $ampSid; SettingsFile = (Join-Path $applicationRoot 'host-settings.json');
    DataDirectory = $data.FullName; AllowEmergencyTermination = $false } | ConvertTo-Json | Set-Content -LiteralPath $agentConfig -Encoding utf8
if ($hasExecutable) {
    $taskExecutable = Join-Path $agentBin.FullName 'ScrapMechanic.Host.exe'
    $taskArguments = 'agent --config "' + $agentConfig + '"'
} else {
    $taskExecutable = $dotnetExe
    $taskArguments = '"' + (Join-Path $agentBin.FullName 'ScrapMechanic.Host.dll') + '" agent --config "' + $agentConfig + '"'
    $kvpPath = Join-Path $InstancePath 'GenericModule.kvp'
    Copy-Item -LiteralPath $kvpPath -Destination ($kvpPath + '.before-scrapmechanic-portable')
    $kvp = Get-Content -LiteralPath $kvpPath
    $kvp = $kvp -replace '^App.ExecutableWin=.*$', ('App.ExecutableWin=' + $dotnetExe)
    $kvp = $kvp -replace '^App.CommandLineArgs=.*$', 'App.CommandLineArgs="ScrapMechanic.Host.dll" bridge --config client.json'
    $kvp | Set-Content -LiteralPath $kvpPath -Encoding utf8
}
$action = New-ScheduledTaskAction -Execute $taskExecutable -Argument $taskArguments -WorkingDirectory $agentBin.FullName
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $GameUser
$principal = New-ScheduledTaskPrincipal -UserId $GameUser -LogonType Interactive -RunLevel Limited
$taskSettings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $taskSettings -Description 'Scrap Mechanic interactive Steam agent. Does not start the game at logon.' | Out-Null
@{ TaskName = $TaskName; InstancePath = $InstancePath; GameUser = $GameUser; AmpUser = $AmpUser } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $InstallDirectory 'installation.json') -Encoding utf8
Write-Host 'Installed. Review host settings in AMP, then start the scheduled task while the game user is logged in. No game was launched.'
Write-Host 'If using portable output, restart the stopped AMP instance to reload GenericModule.kvp.'
