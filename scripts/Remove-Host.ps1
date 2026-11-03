[CmdletBinding(SupportsShouldProcess)]
param([string]$InstallDirectory = 'C:\ScrapMechanicHost')
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Remove-Host requires Windows.' }
$receipt = Get-Content -LiteralPath (Join-Path $InstallDirectory 'installation.json') -Raw | ConvertFrom-Json
if (Get-Process -Name ScrapMechanic -ErrorAction SilentlyContinue) { throw 'Stop the game gracefully and verify its exit before removal.' }
if ($PSCmdlet.ShouldProcess($receipt.TaskName, 'Disable, stop and unregister only the agent scheduled task')) {
    Disable-ScheduledTask -TaskName $receipt.TaskName | Out-Null
    Stop-ScheduledTask -TaskName $receipt.TaskName
    Unregister-ScheduledTask -TaskName $receipt.TaskName -Confirm:$false
    Write-Host 'Agent task removed. Backups, saves, configuration, logs and binaries were preserved.'
    Write-Host 'Remove the stopped AMP instance/template and retained host files manually when no longer needed.'
}
