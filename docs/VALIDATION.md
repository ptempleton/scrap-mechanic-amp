# Shareable validation record

Evidence date: 2026-11-02. This summary excludes private account names, addresses,
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
| Intended-world handoff | After testing, the operator selected the intended world, AMP launched it, and the operator confirmed joining it. |

The operator reported 0% GPU utilization for the game process. This is an
observation, not a performance guarantee or evidence that no graphics device is
required. The Windows user must remain signed in.

## Coverage still open

- Restore a real test-world archive and validate in-game state; live retention pruning.
- Reboot/login recovery and operation with no logged-in game session.
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

## Package build versus deployed build

The deployed native Windows build used the same launcher implementation included
in this source tree. The review ZIP contains a fresh portable DLL build compiled
on macOS, with debug symbols omitted, for use with `dotnet.exe` on Windows. These
exact review binaries have not been installed into the live host. Packaging does
not change the running host, its schedule, world, configuration or binaries.

See `PACKAGE-VERIFICATION.md` in the generated archive for the packaging run's
executed checks. See [../TESTING.md](../TESTING.md) for the acceptance procedure.
