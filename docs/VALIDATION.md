# Shareable validation record

Evidence dates: 2026-11-02 through 2026-11-10. This summary excludes private account names, addresses,
Steam IDs, authentication/session material, raw logs and world data. Detailed
operator notes remain outside the review archive.

## Environment

- Windows 11, AMD Ryzen 7 6800U/Radeon 680M, 32 GB installed RAM, HDMI dummy plug.
- AMP 2.8.0.10, existing licensed Steam copy of Scrap Mechanic.
- .NET SDK 10.0.403 and .NET runtime 10.0.14 x64 on the Windows host.
- Separate ordinary game-user desktop session and AMP service; actual AMP-to-agent
  operations succeeded. Agent/game session was 1. The controller used NetworkService;
  a separate inspection of the child AMP process owner was not captured.
- Exact Steam client and game build numbers were not recorded.

## Executed results

| Check | Result and scope |
|---|---|
| Windows Release build and framework-dependent win-x64 publish | Passed on the target Windows host. |
| Windows portable test cases | 39/39 passed; Unix symlink case is inapplicable. |
| Windows native checks | Four passed: same-user pipe round trip/ACL, wrong-owner rejection, broad-access rejection, synthetic native-window WM_CLOSE/process identity/exit. |
| Earlier macOS portable tests | 40/40 passed; Windows-only tests explicitly skipped there. |
| Template validator | Passed: 44 evidenced KVP keys and 12 mapped settings. This is not an AMP runtime schema proof. |
| Original Python template regressions | 4/4 passed. Packaging tests added later are reported in the package verification report. |
| Local template installation/catalogue/instance | Passed with explicit `LOCAL/ScrapMechanic:main` registration. |
| AMP controls/settings | Start, Console and all 12 configuration fields visible after refreshed login. |
| Actual AMP-to-agent communication | Status, backup, start and stop succeeded across the service/interactive boundary. |
| Steam launch | Passed with default Steam launch mode and existing authentication. |
| Duplicate-start prevention | Additional start returned the same existing game PID. Full conflict/recovery matrix remains pending. |
| Multiplayer | Operator joined Survival from another machine and confirmed the intended world. Host remained on loading screen. |
| Graceful stop and backup | Several AMP Stop operations completed with no game PID and a verified new backup. Normal stop plus backup took about 5–6 seconds in observed trials. No force termination was requested. |
| Persistence | One valid trial passed by operator observation after player logout and Stop/Start. An earlier trial used an unsuitable marker and is inconclusive. WM_CLOSE is not proven to explicitly flush saves. |
| AMP Schedule Restart | Console showed successful stopped backup followed by a new game PID; operator observed game exit/relaunch. A daily 04:00 trigger was enabled by the operator; its unattended firing was not observed. |
| Normal AMP Restart | Operator separately confirmed the normal Restart action was tested on 2026-11-03. |
| Intended-world handoff | After testing, the operator selected the intended world, AMP launched it, and the operator confirmed joining it. |

## Interrupted-start recovery and prerequisite fix, 2026-11-09

After a power interruption, the existing agent answered in the interactive Windows
session but rejected AMP Start with `LaunchUnresolved`. The operator confirmed
Steam was open with no pending launch/dialog and had manually played and exited
the game. Running the documented `acknowledge-launch --confirm` command returned
Stopped, then AMP Start succeeded. The same game process remained observed for
over a minute. Multiplayer and world persistence after this recovery were not
rechecked by the automation.

Code inspection found that earlier builds recorded a pending launch before
checking Steam availability and executable prerequisites. A preparation failure
could therefore leave a false unresolved-launch lock without dispatching a launch.
The first failed attempt's agent log was not captured, so that precise trigger
is not proven for the reported outage.

Version 0.1.0-review.3 moves those checks before recording the lock. The macOS suite
passed 41 portable C# tests and 10 Python tests, including a new test using persisted
state: missing Steam fails without dispatch/lock, and retry succeeds after Steam
becomes available and the agent is recreated. Dispatched-launch exceptions and
timeouts continue to preserve their locks. Windows CI results are linked in the
release notes.

The review.3 portable binaries were installed on the Windows host on 2026-11-09
after a successful graceful stop and stopped-world backup. All four release files
in both the agent and bridge directories matched the package hashes. The retained
native Windows apphosts passed their help smoke tests, the new agent answered
Stopped, and configuration/persistent-state file hashes were unchanged. Rollback
copies of the replaced binaries were retained.

After restarting the AMP instance, AMP Start succeeded and the same game process
remained Running for over a minute. The operator subsequently reported that everything looked correct.
That general confirmation was not a new controlled persistence trial. The missing-Steam case has automated regression
coverage; a deliberate live missing-Steam or power-interruption trial was not run.

The operator reported 0% GPU utilization for the game process. This is an
observation, not a performance guarantee or evidence that no graphics device is
required. The Windows user must remain signed in.

## Coverage still open

- Restore a real test-world archive and validate in-game state; live retention pruning.
- Unattended reboot/login recovery and operation with no logged-in game session.
  Manual recovery after the reported power interruption is recorded above.
- Actual delayed stop/large-backup timing under AMP. `App.ExitTimeout=900` did not
  survive AMP's configuration rewrite; short success does not settle this issue.
- Game crash and bridge-loss recovery; unavailable/full backup location; startup
  dialogs/timeouts; duplicate/conflicting processes; emergency policy on disposable data.
- Unapproved Windows account and remote named-pipe negative access tests in the
  installed deployment; minimum AMP web-role permissions.
- Thirty-minute Parsec disconnect trial. The operator says the host does not
  sleep; this was treated as an optional operational confirmation, not a passed test.
- Additional persistence markers/timing, especially while players remain connected.
- Settings-edit regeneration and unchanged active stop/backup settings during a run.
- Optional Steam URI/direct-executable modes; Linux/Proton support is absent.

## Crash recovery development, 2026-11-10

Review.4 adds opt-in agent-owned crash recovery. On macOS, 63/63 portable C#
tests and 10/10 Python tests passed, with a clean build (zero warnings/errors).
The template validator passed 44 evidenced AMP keys and 16 mapped settings.
New tests exercise persisted retry limits, window expiry and clock rollback,
concurrent polls, cancellation, crash archive retention isolation, interrupted
recovery, Steam/backup/conflict failures, and bridge continuity. Windows-only
checks were explicitly skipped on macOS. The release notes record CI results.

This feature has not yet been tested against a real crashing game or enabled on
the production world. The new four AMP settings require deployment/UI validation.
See [CRASH-RECOVERY.md](CRASH-RECOVERY.md) for the disposable-world test.

## Package build versus deployed build

The installed review.3 ZIP contains portable DLLs compiled on macOS with debug symbols
omitted. Those exact four release files are now deployed in both Windows binary
directories, using the existing native apphosts and installed .NET 10 runtime.
The separate update operation and its live verification are recorded above;
running the package builder itself does not install or change the game host.
The review.4 package is a separate candidate; it has not been deployed by this
development run.

See `PACKAGE-VERIFICATION.md` in the generated archive for the packaging run's
executed checks. See [../TESTING.md](../TESTING.md) for the acceptance procedure.
