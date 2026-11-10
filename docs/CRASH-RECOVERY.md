# Optional crash recovery

Available from **0.1.0-review.4**, disabled by default. Test on a disposable world
before enabling it for regular hosting. Only the helper performs automatic
recovery; keep AMP's `Limits.AutoRetryCount=0` and remove any competing crash
restart automation. A scheduled daily graceful restart is a separate operation.

## Policy and settings

Configure these in AMP's Scrap Mechanic category **while the game is stopped**:

| AMP setting | JSON field | Default | Bounds |
|---|---|---|---|
| Restart after a game crash | `AutoRestartOnCrash` | false | true/false |
| Crash restart delay | `CrashRestartDelaySeconds` | 30 seconds | 5–600 |
| Maximum crash restart attempts | `CrashRestartMaxAttempts` | 3 | 1–10 |
| Crash restart rolling window | `CrashRestartWindowSeconds` | 900 seconds | 60–86400, at least the delay |

Settings are captured for the running game. Editing the checkbox does not cancel
an already scheduled recovery: **use AMP Stop**. Stop cancels the wait persistently
and takes any required stopped archive. A retry already doing an archive or startup
finishes that serialized operation before a queued Stop is handled. Stop then
closes any relaunched game gracefully. Backup duration plus startup can therefore
delay the Stop response; do not replace it with forced termination.

After an observed unexpected game exit, the helper records `unexpected_exit` and
`crash_recovery_scheduled`, waits the configured delay, and confirms all game
processes are gone. It creates and verifies an archive, checks Steam/executable
prerequisites, then dispatches one launch. Process observation is still not a
multiplayer health check. JSON logs remain in `Data/logs` and are never automatically
deleted or rewritten; the crash archive contains saves, not copies of those logs.

Crash archives go under **`<BackupDirectory>/crashes/<source-id>/`**. They use the
same unique names, integrity checks and per-source retention count as normal
backups, but cannot prune normal backup folders. They may contain inconsistent
or incomplete saves. A verified archive proves bytes were preserved, not that
the game committed its last changes. Crash archiving is mandatory even when
`BackupOnStop` is false. There is no automatic restore or save editing.

The attempt timestamps and pending countdown are kept in `Data/state.json`.
Restarting the agent cannot reset the budget. Three automatic attempts within
fifteen minutes are allowed by default; another observed crash exhausts it and
requires operator recovery. Successful process startup does not reset that count.
Old attempts expire from the rolling window, but exhaustion does not silently
resume after time passes. Future timestamps count if the system clock moves back.

AMP's bridge stays alive through the countdown and recovery. The console prints
`SM_CRASH_RECOVERY_WAITING` and structured status with `RecoveryWaiting`, a null
game PID and the due time. AMP's main tile may still say Running because it tracks
the bridge. A new game PID and `SM_PROCESS_RUNNING` show process return; join from
another Steam client to establish multiplayer readiness. A terminal recovery
failure with no game process makes the bridge exit with a failure code.

## When recovery stops

- Backup failure, unresolved launch, failed stop, process conflict or inaccessible
  process inspection blocks automatic relaunch.
- Steam missing or a failed recovery launch stops recovery and records the error.
  It does not spend all remaining attempts retrying an uncertain Steam request.
- Normal Stop/Restart and explicit emergency termination do not trigger recovery.
- An initial launch that never reaches its observation interval is not treated as
  a running-game crash. Its pending-launch interlock remains in effect.
- A hung game whose process still exists is not automatically killed or restarted.
- The helper cannot recover changes the game never saved, guarantee Steam is
  authenticated, or recover a crash while the agent itself was offline. A pending
  countdown can resume after agent restart; an interrupted archive/dispatch needs
  inspection. This is not unattended boot/login automation.

Inspect `crash_recovery_failed`, `operation_failed` and the status detail. Fix the
reported issue. If `LaunchUnresolved` is present, first inspect Steam for delayed
launches/dialogs and follow [TROUBLESHOOTING.md](../TROUBLESHOOTING.md). Once all game
processes are stopped and no launch is pending, run a stopped `backup` and then
Start. This clears the failure interlock, not the rolling attempt history. Never
delete state.json to reset attempts. Keep the existing good backups if a restore
is needed; follow the manual staging/restore procedure in [INSTALL.md](../INSTALL.md).

## Upgrade an existing review.3 installation

The complete review ZIP includes this guide, source, tests and the four portable
helper files. A binary-only upgrade safely leaves recovery off, but will not add
the four settings to an existing AMP instance's interface.

1. Verify/extract the ZIP. Gracefully stop the game, confirm its stopped backup,
   and stop the AMP **instance** in ADS. Then stop the agent task with no game
   process running, as described in [INSTALL.md](../INSTALL.md).
2. Preserve timestamped copies of both binary directories, `Data/state.json`,
   `<instance>/scrapmechanic/host-settings.json`, the instance's
   `GenericModule.kvp` and its deployed `configmanifest.json`. Also preserve
   the local ADS template directory. Do not replace your paths/SIDs with examples.
3. Replace the four portable helper files in **both** binary directories. Retain
   existing native apphosts for a framework-dependent installation; self-contained
   installations require a complete matching self-contained build.
4. Replace `<instance>/configmanifest.json` with the package's
   `amp/scrapmechanicconfig.json`. `Meta.ConfigManifest` names the source file in
   ADS's store; the deployed instance uses `configmanifest.json`, as described
   in the [official guide](https://github.com/CubeCoders/AMP/wiki/Configuring-the-%27Generic%27-AMP-module#configuration-manifest).
   Confirm its fields match the existing host settings before replacement.
   Update `scrapmechanicconfig.json` in ADS's
   `Plugins/ADSModule/DeploymentTemplates/LOCAL-ScrapMechanic-main` too, so future
   instances have the same fields. Preserve the instance's `GenericModule.kvp`;
   do not replace it with template defaults or reset its path/account settings.
5. Add just the following properties to the **existing** generated
   `scrapmechanic/host-settings.json`, preserving all original values:

   ```json
   "AutoRestartOnCrash": false,
   "CrashRestartDelaySeconds": 30,
   "CrashRestartMaxAttempts": 3,
   "CrashRestartWindowSeconds": 900
   ```

   These are properties inside its existing JSON object, not a complete replacement
   file. Ensure comma placement is valid. The helper supplies these defaults when
   absent, but AMP AutoMap requires the properties to expose/update them reliably.
6. Start the agent and check CLI status while the game remains stopped. Start the
   AMP instance, refresh/reopen its configuration, and confirm the four new fields
   and all original paths. Import the generated settings using AMP's settings
   import if necessary; inspect the generated JSON before starting the game.
7. Leave recovery off for a normal launch/Stop smoke test. Select the disposable
   world and perform the acceptance test below before enabling it on the good world.

The updated helper reads old state without discarding interlocks. After it writes
the extended recovery state/settings, older helpers' strict JSON parser cannot
read them. **Do not roll back just the DLLs after using the new build.** Before
any game/recovery operation, the preserved stopped snapshot can be restored with
both old binary copies and matching old settings/manifest/state. After operation,
retain current state and logs and inspect pending launches/backups before a
reviewed downgrade; restoring stale state could erase a real safety interlock.

## Disposable-world acceptance (not yet run live)

1. Stop the host and preserve a normal good-world backup. With Steam Launch Options
   blank, open the game normally, load the disposable test world, and exit normally.
   The save directory setting does not choose the world: `-last_save` does.
2. Enable crash recovery in AMP with defaults 30/3/900. Start, join from another
   client, confirm the test world, make a persistent marker and log out. Record the
   PID, settings, latest normal backup and log time. Never deliberately crash the
   production world to run this test.
3. With the disposable world still hosted, deliberately end **only ScrapMechanic.exe**
   using Task Manager. This is the test stimulus, not a helper shutdown feature.
   Expect the countdown, one separate crash archive, one new PID and no competing
   AMP launch. Verify the archive, join again and record which saved changes remain.
4. Repeat a crash and click AMP Stop during the countdown. Confirm no relaunch
   occurs after the deadline, including after restarting the stopped agent. Stop
   may archive the crashed world; normal backup history must remain unchanged.
5. In a separate test run with fresh/expired attempt history, complete three crash
   relaunches within fifteen minutes, then crash once more. Confirm a logged limit,
   no fourth automatic relaunch, and failure shown in AMP. Record timestamps; an
   earlier recovery in this same window counts toward the limit.
6. With disposable data, exercise an inaccessible backup location and missing
   Steam. Confirm recovery stops, reports the cause and preserves good backups.
   Resolve storage/Steam and use stopped backup + manual Start to recover. An
   uncertain dispatched launch must still require explicit Steam inspection.
7. Turn recovery off, Stop, select the good world normally, exit, restore desired
   settings and start through AMP. Confirm the correct world before enabling
   recovery for regular use. Record each observed result separately.

Synthetic tests cover these policies, including persisted history and separate
retention, but cannot prove Steam behavior, AMP's actual timeout handling or save
consistency. See [VALIDATION.md](VALIDATION.md) for what was actually executed.
