# Troubleshooting

Start with `status --config client.json`, the AMP console and
`C:\ScrapMechanicHost\Data\logs\host-YYYYMMDD.jsonl`. JSON records include UTC time,
event and details. Keep logs when investigating failures. Logs rotate by UTC day;
automatic log retention is not implemented.

## AMP template is missing or the bridge cannot start

Verify the six template files (including `manifest.json`) are immediately under
`<ADS instance>\Plugins\ADSModule\DeploymentTemplates\LOCAL-ScrapMechanic-main`.
Register `LOCAL/ScrapMechanic:main` in Configuration -> Instance Deployment ->
Configuration Repositories, preserving existing entries. Refresh the catalogue
and browser. On AMP 2.8.0.10 the folder alone was insufficient; registration was
required. Confirm it is a Windows non-container instance of the custom template.
Check `Meta.ConfigManifest`/`Meta.MetaConfigManifest` references and the deployed
`GenericModule.kvp`. The offline validator checks keys, not AMP runtime loading.

Copy the complete publish output into `<game instance>\scrapmechanic`, including
the core DLL, `.deps.json` and `.runtimeconfig.json`. For portable output, the
installer changes `App.ExecutableWin` to the installed `dotnet.exe` and prepends
`ScrapMechanic.Host.dll` to the bridge arguments. Make that change only with the
AMP instance stopped, then restart the instance. Confirm .NET 10 x64 is installed.

`host-settings.json` must exist before AMP's AutoMap can update it. The installer
seeds it. All twelve fields are mapped to this file, not to game save files.

## Pipe connection timeout or access denied

- Confirm the game user is logged in and the agent scheduled task is running.
  Check Task Scheduler Last Run Result and agent logs. Run the same agent command
  manually in the game user's normal desktop to see startup errors if needed,
  after stopping the existing agent while no game is running.
- `agent.json` must contain the Windows SID of the **actual game AMP process**.
  ADS identity, an AMP web username, and the current administrator may differ.
- `client.json` must contain the game user's SID as `AgentIdentitySid`; the client
  rejects a pipe owned by a different account. Both files need the same PipeName.
- The game user needs read access to generated settings and write access to its
  data/backup directories. AMP needs its usual access to the bridge directory.
- A denied third-party account is expected. Do not grant Everyone pipe access or
  change the service into an elevated interactive service as a workaround.

Request timeouts mean the outcome is uncertain; an operation already in progress
continues safely on the agent. Inspect status/logs before retrying. In particular,
a long backup may exceed AMP's bridge exit timeout. The game remains stopped and
the persistent interlock prevents a new start until the operation succeeds.

## Game does not appear or stays on loading screen

The supplied experiment used a loading-screen host successfully. That screen alone
is neither a failure nor readiness evidence. Check the process and join remotely.

Confirm Steam is already running from the configured directory in the same desktop
session, signed in, and has completed updates. Check first-run dialogs and license
availability. The agent never submits credentials and does not silently select
another Steam profile. Confirm `GameExecutable` names the real `ScrapMechanic.exe`.

Steam mode uses `-applaunch 387990`; executable mode starts the configured binary
with Steam already present. URI mode sends `steam://run/387990//<encoded arguments>/`
to the configured Steam executable. URI arguments and confirmation behavior must
be tested against your Steam client; use Steam mode first.

After a timeout, Steam might still launch later. The persistent `LaunchUnresolved`
interlock blocks another launch and backups. Inspect Steam and cancel every queued
dialog/download launch. Once no game exists, explicitly clear it:

```powershell
.\ScrapMechanic.Host.exe acknowledge-launch --confirm --config .\client.json
```

A late game process can instead be observed with Start, or closed with Stop.
Never delete `Data\state.json` merely to bypass a launch/backup interlock. Malformed
state fails closed; preserve it and logs before any manual repair.

## Graceful shutdown fails

The live AMP 2.8.0.10 instance did not retain `App.ExitTimeout` when rewriting its
configuration, despite that key being present in official template examples.
Treat AMP's effective bridge exit timeout as unverified until the synthetic
stop/restart acceptance check passes. The agent's own graceful-exit timeout still
applies; stopping the bridge must never force-terminate the game. Check agent
status and logs before retrying if AMP reports a stopped bridge prematurely.

`No accessible main window` means a process was found but its main window could
not accept the request. A timeout means `WM_CLOSE` was posted but exit was not
observed in time. The agent never changes either outcome into an automatic kill.

Check for a modal dialog, hidden main window, wrong session, different elevation
or an unresponsive game. Microsoft documents that
[window messages are restricted by process integrity level](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-postmessagew).
Keep Steam, game and agent at normal privilege. Try a deliberate desktop close
after inspecting the situation. If the game later exits, run a stopped backup
before starting again; the original failed request did not certify a backup.

The graceful timeout uses settings captured when the game was observed/started.
Increasing the AMP setting while that process is running does not change the
current operation's limit. Set the next desired timeout before a fresh launch.

### Explicit emergency termination

Only when you accept possible loss of recent world changes:

1. Edit the private `agent.json` to set `AllowEmergencyTermination` to `true`.
   This single policy is reread for the command; do not kill/restart the agent to
   enable it while a game is running.
2. Issue the confirmed command from an authorized account:

   ```powershell
   .\ScrapMechanic.Host.exe emergency-stop --confirm --config .\client.json
   ```

3. Inspect logs/world state. Take a stopped recovery backup before relaunch. No
   automatic post-kill backup is labeled as a clean save.
4. Set `AllowEmergencyTermination` back to `false`.

An AMP console operator can use `emergency-stop CONFIRM` only while that local
policy is enabled. Both gates are required. Only the verified game PID is killed;
Steam and process trees are not emergency termination targets.

## Backups fail or restore behaves unexpectedly

Zero or multiple detected Survival directories requires an explicit `SaveDirectory`.
An empty directory, symlink/junction in its path, nested backup/save path, changed
file inventory, unavailable storage or running/unresolved game causes refusal.
The path is resolved in the **agent user's** profile, never the service account's
AppData. Disk-full/permission failure leaves the prior verified backups intact.

Fix the reported storage problem and retry CLI `backup` or `stop`. The saved active
settings intentionally preserve the original source and destination after failure;
free space/restore access there before retrying. Do not change world identity to
bypass an incomplete backup. Retention runs only after publishing a verified ZIP,
and only prunes matching valid project archives. A `retention_failed` record means
the new backup succeeded but old backups need attention.

Checks around file copies reduce exposure to outside changes; the agent cannot
prevent a person, Steam Cloud or unrelated program from modifying saves. Keep
manual launch and sync activity out of the backup/restore window. This is not a
filesystem snapshot or a documented game-save transaction.

For restore, verify hashes, extract into a staging directory, preserve the old save,
copy only `world` contents, and confirm the selected world in-game. Resolve Steam
Cloud conflicts deliberately. No process-level signal proves a restore succeeded.

## Parsec disconnect, reboot or monitoring confusion

Keep the Windows account logged in. Logout, sleep, display loss or driver problems
can stop hosting regardless of AMP status. After reboot, log in, start/authenticate
Steam and verify the agent task before AMP Start. Automatic login is not configured.

The named pipe is available across sessions to the explicitly authorized AMP
account. The agent must be in an interactive nonzero session. An HDMI dummy plug
does not bridge session 0 and does not remove the login requirement.

AMP Running and the process PID are observations, not a multiplayer readiness
check. CPU/memory charts belong to the bridge; use Task Manager for the game/GPU.
Unexpected game exit emits `unexpected_exit` and ends the bridge with nonzero
status. Disable any external AMP automation that would override the template's
zero automatic-retry setting. Investigate before an explicit restart.
