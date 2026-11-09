# Testing and acceptance

The complete game acceptance cases below are **not yet passed**. Windows automated
tests, agent installation, AMP-to-agent status, Steam launch, normal AMP Stop/Start, AMP Schedule Restart,
and stopped-world backups have been executed successfully. The user confirmed
joining and reconnecting to the test world, and persistence in one valid trial
after player logout. See the evidence record linked below. Run game tests with a
disposable Survival world. Never infer a pass from a compile, green CI job, process PID, or
AMP Running label. Record Windows, AMP, Steam and game versions with each run.
See [docs/TEST-RESULTS.md](docs/TEST-RESULTS.md) for executed checks.

## Automated checks

With .NET 10 SDK and Python 3:

```powershell
.\scripts\Test.ps1
```

Equivalent commands on macOS/Linux:

```sh
dotnet build ScrapMechanic.slnx -m:1 -nodeReuse:false -p:UseSharedCompilation=false
dotnet tests/ScrapMechanic.Host.Tests/bin/Debug/net10.0/ScrapMechanic.Host.Tests.dll
python3 scripts/validate-template.py
python3 -m unittest discover -s tests -p 'test_*.py'
```

The executable test harness has no third-party test packages. A nonzero exit means
failure. Its accelerated fake platform exercises success, concurrency, timeouts,
late Steam launches, missing windows, process conflicts, crashes, emergency gating,
persistent state, restart ordering, and backup failure interlocks. Storage tests
use synthetic temporary files and exercise ZIP round trips, hashes, retention,
save discovery, links, unsafe entries and interrupted copies. Protocol/bridge tests
cover bounded frames, EOF and failed stops. Template regression tests deliberately
inject invented keys, forced termination and mismatched defaults.

On Windows the same harness also exercises real named-pipe ACL/protocol access,
rejects a wrong expected server owner and broad account SID, and (in an interactive
session) closes a disposable native window in a child test process with `WM_CLOSE`.
The fixture self-exits after 20 seconds if the parent fails. It never loads a game
or accesses game saves. Windows tests explicitly print SKIP when their preconditions
are absent. Cross-account access still requires the live test below.

## Live integration matrix

Before using a game world, validate AMP's actual bridge stop/restart timing with
a disposable synthetic process. AMP 2.8.0.10 did not persist `App.ExitTimeout` in
the deployed configuration; the template's 900-second budget is therefore not
proven. Exercise a delayed stop and backup, confirm the agent remains responsible
for the process if the bridge exits early, and ensure relaunch cannot overtake the
pending stop. Also verify the automation account can see the new instance's
console, start/stop controls and all Scrap Mechanic settings.

Use the normal host account, HDMI dummy plug and authenticated Steam installation
on the Ryzen 6800U/680M system. Arrange a second licensed Steam player/account on
another machine. Confirm the selected last save is disposable before every launch.

| # | Test and steps | Pass evidence | Result |
|---|---|---|---|
| 1 | Start via AMP with default Steam mode. Check Steam dialogs, arguments and game path. Repeat with direct executable and optional URI mode separately if needed. | Exactly one correct game process in the logged-in session. No repeated launch. Record whether URI prompts or ignores arguments. | PASS for default Steam mode, 2026-11-02; optional modes NOT RUN |
| 2 | Run CLI `status`; compare PID, executable, creation time and session with Task Manager. Start again and reconnect/restart only the bridge. | Same game PID, no duplicate; status says readiness unverified. Attempt a second agent: it must refuse ownership. | PARTIAL: repeated console start returned PID 16952; independent identity/bridge recovery checks pending |
| 3 | Remote player joins and plays Survival while host remains on loading screen. | Remote player can move, interact and reconnect. Record join method; do not invent ports or server-list support. | PASS by user report: joined, changed world, and reconnected; join method not recorded |
| 4 | Click AMP Stop. Also exercise CLI stop. For a timeout, use the synthetic fixture or a disposable test setup, never deliberately hang a real save. | `stop_requested` then actual process exit and `stopped`; no automatic kill. Failed close/timeouts report failure and leave a live process intact. | PARTIAL: real AMP WM_CLOSE stop and automatic backup passed; live delayed-stop test pending |
| 5 | Change a visible marker in disposable world, stop/restart through AMP and reconnect. Repeat with short/long intervals after the change. | Marker survives each recorded trial. Report observation only; do not conclude that WM_CLOSE explicitly forces a save. | PARTIAL: second trial passed after player logout; first had an unsuitable marker; timing variations pending |
| 6 | Stop with backup enabled; verify ZIP; extract to staging; compare hashes. Perform manual restore from INSTALL.md using disposable data, then join again. Set retention to 2 and create 3 backups. | Game-level restored marker matches; newest 2 verified archives remain; no overwrite, corrupt/foreign archive deletion, or live copy. | PARTIAL: verified stopped backups succeeded; game restore and live retention trial pending |
| 7 | On a disposable world only, intentionally end the game using Task Manager. Observe agent/bridge, then explicitly recover. Separately simulate an unavailable/full backup destination. | `unexpected_exit`, bridge reports exit, no automatic restart. Failed requested backup prevents AMP/CLI relaunch until resolved. | NOT RUN |
| 8 | With remote player connected, disconnect Parsec without signing out. Observe for at least 30 minutes; perform AMP stop/start from another device. | Windows session remains, GPU/display remains usable, remote play continues, stop/start works. Record power/GPU/memory behavior on the 680M. | NOT RUN |
| 9 | Run actual AMP instance as its normal Windows service account and agent as game user. Issue AMP start/stop/restart. Repeat after reboot + manual login/Steam authentication. | Service in session 0 controls game in correct desktop session via pipe. No unattended launch before login. Exact account/SID mapping recorded. | PARTIAL: actual AMP start/stop/scheduled Restart and agent session 1 verified; reboot and child process owner checks pending |

Record the existing 7800X3D/RTX 3060 Ti observations separately from these results.
The user's latest update (2026-11-02) confirms 0% GPU utilization for the Scrap
Mechanic process. Keep that runtime observation alongside the earlier gameplay
and persistence evidence; the module acceptance cases above remain pending.

## Failure and access tests

1. **No user session:** log out after stopping the game. AMP Start must fail clearly;
   it must not launch a headless/session-0 game. Log back in and start Steam/agent.
2. **Unapproved account:** run CLI status from a third, nonadministrator Windows
   account. Connection must be denied. Check that the configured AMP service
   account succeeds. Remote named-pipe access must be denied; no TCP management
   listener is installed. Local administrators are trusted system administrators,
   not a security boundary this agent can exclude.
3. **Startup preparation and timeout:** with the game stopped, close Steam or use
   a nonexistent executable path. Confirm an error without `LaunchUnresolved`;
   correct it and retry Start without acknowledgement, also after agent restart.
   Separately, make Steam require a launch confirmation in a disposable setup.
   A dispatched launch that times out must retain the lock and must not retry.
   Cancel Steam's pending launch, inspect status, then explicitly acknowledge it.
4. **Partial stop recovery:** induce backup failure on a disposable world, recreate
   the bridge, and ensure Start stays blocked. Fix storage, use CLI backup, then start.
   Verify persistent state also survives agent restart **while the game is stopped**.
5. **Conflicting process:** start a separate disposable game session manually before
   agent Start. Matching same-session process is attached; a different path/session,
   inaccessible identity, or multiple game processes must fail closed.
6. **Emergency behavior:** default confirmed emergency command must refuse. Enable
   the local policy, issue an unconfirmed command (must refuse), then explicitly
   confirm on disposable data. It must log the termination, skip automatic backup,
   and block restart until stopped recovery. Turn policy off afterwards.
7. **Bridge loss:** terminate only the AMP bridge during disposable gameplay. The
   agent and game should remain. Restart bridge and confirm the same game PID.
   Do not end the agent scheduled task while a game is running.
8. **Configuration:** change an AMP setting while stopped and inspect generated
   `host-settings.json` before Start. Confirm live edits do not change the running
   process's captured stop/backup settings. Check AMP actually loads all fields.

## Evidence to retain

For each case, record pass/fail/skipped, timestamp, software versions, both Windows
identities and sessions, launch mode, game path, selected disposable world, elapsed
times, AMP console output and agent JSON logs. Keep backup verification output and
screenshots of the in-game marker before/after restore. Do not share Steam tokens,
passwords or a production world archive in diagnostics.

Acceptance requires all nine live cases to pass on the intended configuration;
optional launch modes may remain explicitly unsupported if their own trials fail.
