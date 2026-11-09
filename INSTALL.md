# Installation

Use a disposable Survival world for the first installation. Do not run a start or
restore test against production saves. Installation scripts do not launch the game,
write its saves, purchase anything, or install a game through SteamCMD.

## 1. Prepare the Windows host

1. Install AMP and your licensed Steam copy of Scrap Mechanic normally.
2. Sign in to Windows as the account that will own the game session. Start Steam,
   authenticate, and finish game updates/first-run dialogs. Keep Steam and the game
   at normal privilege; an elevated game may reject the agent's window messages.
3. In Steam, use **Manage -> Browse local files** to locate the actual
   `ScrapMechanic.exe` (commonly `Release\ScrapMechanic.exe`). Record the Steam
   installation directory separately; a Steam library can be on another drive.
4. Clear Steam's Launch Options so the game opens normally; AMP supplies its own
   dedicated-host arguments. Create/load a disposable Survival world normally,
   then close the game. Verify
   the intended last-save selection. `-last_save` is controlled by the game and can
   select a different world if someone plays another save later.
5. Find the disposable world's directory under the game user's
   `%APPDATA%\Axolot Games\Scrap Mechanic\User\User_<SteamID>\Save\Survival`.
   Confirm this on the actual installation; the software treats it as a discovery
   convention. Set an explicit directory when multiple Steam profiles exist.
6. Keep the game user logged in. Configure sleep/display behavior yourself as
   appropriate for hosting. Disconnecting Parsec and logging out are different actions.

## 2. Build or use the portable build

The review ZIP already contains the portable build in `artifacts\publish`.
Install the **.NET 10 x64 runtime**, retain that complete directory, and continue
with section 3. `Install-Host.ps1` detects the DLL-only build and configures
`dotnet.exe` automatically. No compilation is needed. Installation was exercised
with Windows PowerShell 5.1; the build/test/package workflow uses PowerShell 7.
See [START-HERE.md](START-HERE.md) and [NOTICE.md](NOTICE.md) for package contents
and integrity checks.

To build from source instead:

Copy this project to Windows. In PowerShell 7 with the **.NET 10 x64 SDK** and
Python 3 on PATH:

```powershell
cd C:\Source\ScrapMechanic
.\scripts\Test.ps1
.\scripts\Build.ps1
```

Output: `artifacts\publish\`. Keep **every file** in that folder together.
The default build includes a Windows apphost `.exe` and uses the installed .NET 10
runtime. `Build.ps1 -SelfContained` adds a downloaded runtime for a machine without
.NET. `Build.ps1 -Portable` omits the platform-specific apphost.

The portable DLL build can be compiled on a Mac and used on Windows with the
**.NET 10 x64 runtime**. Point `Install-Host.ps1 -PublishDirectory` to that directory;
the installer selects `dotnet.exe` when no Windows `.exe` is present. AMP still
launches a real executable, as required by the Generic Module architecture.

The source includes a GitHub Actions Windows/Linux build/test workflow. Check
[its run results](https://github.com/ptempleton/scrap-mechanic-amp/actions)
for the revision you use; CI does not validate multiplayer or live saves.

## 3. Install the local AMP template

Use AMP's instance manager/instance information to locate the **ADS controller's
instance directory** on disk, not the game instance. Verify that it contains
`Plugins\ADSModule\DeploymentTemplates`. Paths differ between AMP installations;
do not assume the example below is your actual datastore.

In an administrative PowerShell session, from this project:

```powershell
.\scripts\Install-Template.ps1 -AdsInstancePath 'C:\AMPDatastore\Instances\ADS01' -WhatIf
.\scripts\Install-Template.ps1 -AdsInstancePath 'C:\AMPDatastore\Instances\ADS01'
```

This copies the six files in `amp\`, including repository metadata, into:

```text
<ADS instance>\Plugins\ADSModule\DeploymentTemplates\LOCAL-ScrapMechanic-main\
```

The `LOCAL...-main` convention is documented in the
[official Generic Module guide](https://github.com/CubeCoders/AMP/wiki/Configuring-the-'Generic'-AMP-module#adding-custom-templates-to-amp).
No remote repository URL or download endpoint is needed for this local install.
The included `manifest.json` uses the official repository-manifest fields; its
origin and URL are empty because this is a local source.

In **Configuration -> Instance Deployment -> Configuration Repositories**, add
`LOCAL/ScrapMechanic:main`, preserving the existing CubeCoders entry. This maps to
the directory above. **This registration is required on AMP 2.8.0.10:** merely
copying the files, refreshing the catalogue, or restarting ADS did not list them
in the live test. Registering the local source and calling the documented
`ADSModule/RefreshAppCache` API made the template appear. Reload the browser after
registration; use **Fetch Latest** if the catalogue needs refreshing.

Create a **Windows, non-container** instance using
**Local - / Scrap Mechanic Survival (Interactive Steam)**. Set **After Creation**
to **Do Nothing** and turn **Start instance on boot** off. Leave the application stopped;
do not enable start-on-boot or automatic retry. Record this new instance's on-disk
directory. It should contain `GenericModule.kvp`.

The empty update manifest intentionally performs no game download. If the template
is not listed, confirm both the folder and the registered local source. Catalogue
discovery and instance creation were verified on a Windows AMP 2.8.0.10 controller
on 2026-11-02; the interactive Steam agent still requires separate installation.

## 4. Install the agent and bridge

Determine the **Windows account running the game AMP instance process**. This is
not an AMP web login. Inspect the AMP service's **Log On** tab and the game instance
process owner; ADS and game instances can use different accounts. These commands
help inventory services and the currently logged-in account:

```powershell
Get-CimInstance Win32_Service | Where-Object PathName -Match 'AMP' |
    Select-Object Name, StartName, PathName
whoami /user
```

Stop the game AMP instance itself before setup if it is already running, so it
cannot overwrite `GenericModule.kvp` while the installer adjusts portable mode.
Keep the Windows game user logged in. Run the following as administrator, replacing
all paths and accounts with the actual values:

```powershell
.\scripts\Install-Host.ps1 `
  -InstancePath 'C:\AMPDatastore\Instances\ScrapMechanic01' `
  -GameUser 'HOSTPC\GameUser' `
  -AmpUser 'HOSTPC\AmpUser' `
  -InstallDirectory 'C:\ScrapMechanicHost' `
  -PublishDirectory '.\artifacts\publish' `
  -WhatIf
```

Run the same command without `-WhatIf` after reviewing it. For a service running
as LocalSystem, use `-AmpUser 'NT AUTHORITY\SYSTEM'` only if that is the actual
instance process identity.

The installer:

- Copies the agent build to `C:\ScrapMechanicHost\bin` and the bridge build to the
  game instance's `scrapmechanic` subdirectory.
- Creates a private agent data/configuration/backup tree for the game user,
  SYSTEM and Administrators; grants the game user read access to AMP's generated
  host settings. Existing AMP parent-directory permissions are retained.
- Resolves both Windows account SIDs and writes `agent.json` plus the bridge's
  `client.json`. The named pipe denies network logons and grants the configured
  AMP account only the access needed for client communication.
- Registers `ScrapMechanicHost-Agent` at logon using the game user's **Interactive**
  token and **Limited** privilege. No Windows password is stored. The task does
  not run when that user is logged out, and has no execution time limit.
- Seeds `host-settings.json`, required by AMP's JSON AutoMap, without launching
  the game. Existing installations/configurations are not overwritten.

Restrict AMP console, file-manager and settings permissions to trusted operators:
that configured account is authorized to control the game user's host. Use one
agent and one AMP instance for this Steam installation. Do not run a second host
or launch the game manually during lifecycle/backup operations.

## 5. Configure and start

Restart/start the game **AMP instance**, keeping its application stopped. In its
**Scrap Mechanic** configuration category, set the Steam path, executable path,
and confirmed save path. Set the backup path to the one installed above if you
used a custom `InstallDirectory`. Keep these defaults initially:

```text
Launch method: Steam
Arguments: -dedicated_server -last_save
Startup timeout: 120 seconds
Process observation: 3 seconds
Graceful exit timeout: 120 seconds
Stopped settle delay: 3 seconds
Backup retention: 10
Back up after stop: enabled
```

If needed, use AMP's import-settings support to import the seeded
`host-settings.json`. Before the first game start, inspect the generated file in
`<instance>\scrapmechanic` to confirm paths and JSON values. Stop settings are
captured with the running process; later changes take effect on the next start.

While the game user is logged in and Steam is open, start the agent task:

```powershell
Start-ScheduledTask -TaskName 'ScrapMechanicHost-Agent'
Get-ScheduledTaskInfo -TaskName 'ScrapMechanicHost-Agent'
```

Check the agent's `Data\logs\host-YYYYMMDD.jsonl` for `agent_started` and no
`agent_error`. From the game account (or actual AMP Windows account), test IPC:

```powershell
cd 'C:\AMPDatastore\Instances\ScrapMechanic01\scrapmechanic'
.\ScrapMechanic.Host.exe status --config .\client.json
```

For portable builds, replace `.\ScrapMechanic.Host.exe` with
`dotnet .\ScrapMechanic.Host.dll` in all commands. Now click **Start** in AMP.
The marker `SM_PROCESS_RUNNING multiplayer_readiness=unverified` permits AMP's
Running state. Prove actual connectivity with a second Steam player.

Click **Stop** to send `WM_CLOSE`; the bridge exits successfully only after the
game exits and its requested backup succeeds. AMP's **Restart** performs this
stop/start sequence. The console command `restart` performs it within the agent.
Avoid AMP's forced-stop action as a routine shutdown: it can stop the bridge
while leaving the game running. The independently started agent keeps observing it.

## Operations and backups

```powershell
.\ScrapMechanic.Host.exe start --config .\client.json
.\ScrapMechanic.Host.exe status --config .\client.json
.\ScrapMechanic.Host.exe stop --config .\client.json
.\ScrapMechanic.Host.exe restart --config .\client.json
.\ScrapMechanic.Host.exe backup --config .\client.json
.\ScrapMechanic.Host.exe verify-backup 'C:\ScrapMechanicHost\Backups\<source-id>\<archive>.zip'
```

`backup` requires no Scrap Mechanic process and no unresolved launch. AMP's console
is available while the bridge is running; use the CLI for a manual backup after
the application is stopped. By default every AMP stop/restart already creates one.
Validate an immediate scheduled Restart on a disposable world before enabling a
recurring trigger. AMP Schedule's Restart action passed on the tested Windows
host; unattended daily execution and the remaining failure cases are still open.

Backups are ZIPs under `Backups\<source-id>\sm-<UTC timestamp>-<GUID>.zip`. Inside,
`world\` contains the full selected directory and `_scrapmechanic\manifest.json`
contains SHA-256 hashes. Retention applies separately per save-directory identity,
only after a successful new archive. Corrupt, partial, or foreign files are not
silently deleted; inspect them manually. The archive is integrity-checked, not encrypted.

Do not use a live AMP file backup as a substitute for this stopped copy. AMP's
ordinary instance backup does not automatically include external AppData saves.
You may separately archive completed ZIPs after granting AMP read access to the
backup location. No save/quiesce command or Steam Cloud coordination is assumed.

## Safe restore

1. Choose a backup of the correct Steam profile, world and game version. First
   practice on disposable data. Prevent clients and other operators from starting
   the host during maintenance.
2. Stop the host gracefully and verify `status` has no PID. Inspect Steam for any
   pending launch. Preserve a fresh backup of the current state separately.
3. Disable the agent task's next logon trigger and stop the task **only after the
   game has exited**. Keep AMP's application stopped. Close Steam so Cloud sync
   cannot race the restore; handle any later conflict deliberately.
4. Run `verify-backup` on the chosen archive. Extract it to a **new staging folder**.
   Do not extract directly over a live save directory.
5. Preserve/rename the existing target directory. Copy the **contents of `world`**
   into a new target save directory, including sidecars and subdirectories. Do not
   copy `_scrapmechanic` into game saves. Do not merge files from different backups.
6. Restore the game user's access if needed. Start Steam, deliberately resolve
   Cloud prompts, re-enable/start the agent task, and select the intended restored
   world normally before relying again on `-last_save`.
7. Verify multiplayer access and a known world-state marker. Keep the previous
   directory until the restored world is confirmed. Process startup and matching
   archive hashes alone do not prove game-level restore success.

No automated restore command is supplied, so a launcher command cannot overwrite
production saves accidentally.

## Removal and updates

Stop the host and verify its exit. Then, as administrator:

```powershell
.\scripts\Remove-Host.ps1 -InstallDirectory 'C:\ScrapMechanicHost' -WhatIf
.\scripts\Remove-Host.ps1 -InstallDirectory 'C:\ScrapMechanicHost'
```

Removal unregisters only the recorded agent task. It preserves saves, backups,
logs, configuration and binaries. Remove the stopped AMP instance and the local
template directory through your normal AMP/filesystem procedure after retaining
any data you need. Do not delete Steam or AppData game folders as part of removal.

For updates, stop the game and agent first, preserve configuration/state/backups,
then replace both binary directories with the same build. The installer refuses
to overwrite an existing setup. Never end the agent task while its game is
running: Windows task/process-tree behavior must not be used as a shutdown method.

For a framework-dependent installation made from this project (including the
native EXE installation with .NET 10 already installed), update from the portable
review ZIP as follows:

1. Verify the release ZIP checksum and extract it into a new directory.
2. Use AMP Stop and confirm successful game exit and its stopped backup. If a
   launch/stop failure is reported, resolve it before updating.
3. Stop the AMP **instance** in ADS so its schedule cannot start the bridge during
   the update. Keep the Windows game account signed in.
4. Confirm no `ScrapMechanic` process exists, then stop the `ScrapMechanicHost-Agent`
   scheduled task. Wait for the task to leave Running before copying files.
5. Make a separate, timestamped copy of both existing binary directories:
   `C:\ScrapMechanicHost\bin` and `<instance>\scrapmechanic`. Preserve these for
   rollback. Copy the four files from the release's `artifacts\publish` into
   **both** directories, replacing only files of the same names. Retain the
   existing native `ScrapMechanic.Host.exe`, if present. Leave all JSON host/client
   configuration, `Data`, saves and backups in place; the release's `.deps.json`
   and `.runtimeconfig.json` are binary runtime metadata and must be updated.
6. Start the agent task, run CLI `status`, start the AMP instance and click Start.
   Confirm the intended world and multiplayer connection. If the new helper fails,
   keep the game and task stopped and restore the old binary files in both
   directories before restarting.

Self-contained installations should use a matching complete self-contained build
instead of this four-file procedure. Updating binaries does not clear existing
launch, stop or backup locks. See [TROUBLESHOOTING.md](TROUBLESHOOTING.md) for recovery.
