# Draft proposal to CubeCoders

Suggested title: **Scrap Mechanic Survival on Windows: working AMP prototype with interactive Steam agent**

Hi CubeCoders team,

We have a working custom AMP Generic Module integration for Scrap Mechanic
Survival on Windows and would like your feedback on whether there is an
appropriate community home for it.

It uses an existing licensed Steam installation with
`-dedicated_server -last_save`. A small MIT-licensed C#/.NET agent runs in the
logged-in Windows game session, while AMP uses a console bridge and a restricted
local named pipe to control it. This lets an AMP service start the game, request
WM_CLOSE shutdown, observe exit, create verified stopped-world backups, and restart.

On Windows 11 / Ryzen 6800U / Radeon 680M with AMP 2.8.0.10, we have exercised
Steam launch, remote Survival play, graceful stop, backup, relaunch, and AMP's
scheduled Restart action. The operator confirmed persistence in a test-world
trial after player logout. Windows automated tests passed 39 portable cases and
four native checks, including pipe ACL behavior and synthetic WM_CLOSE.

We recognize this differs from the normal AMPTemplates submission model. It is
Windows-only, requires an authenticated Steam desktop session, and deliberately
does not download or update the game through SteamCMD. The update manifest is
empty. The agent requires separate installation, and Running means process
detected rather than proven multiplayer readiness. Extended failure, restore,
reboot, and disconnect tests remain documented follow-up work.

The [public repository](https://github.com/ptempleton/scrap-mechanic-amp) and
[release package](https://github.com/ptempleton/scrap-mechanic-amp/releases)
include source, portable launcher binaries,
installation/removal scripts, example configuration, template-key evidence,
test plans/results, checksums and MIT licensing. It contains no game files,
saves, credentials or private deployment history. Development was AI-assisted;
automated and live tests are documented separately from unexecuted checks.

Would you be interested in reviewing this as an advanced community integration?
In particular, would you prefer a separately maintained companion project and
local-template guide, or a route toward AMPTemplates after agreeing on the
Steam-session prerequisite and companion distribution?

One implementation detail that would benefit from guidance: AMP 2.8.0.10 did not
retain `App.ExitTimeout` when rewriting the Generic Module configuration. Normal
shutdown/restart passed, but we have not verified a long stop/backup timeout in AMP.

Thanks for considering it.

---

This is an unsent draft. Share the repository and release links above when you
choose to contact CubeCoders; the release ZIP has a companion SHA-256 checksum.
