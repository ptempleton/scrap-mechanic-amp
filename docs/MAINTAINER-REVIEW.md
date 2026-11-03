# CubeCoders maintainer review

## Proposal

Review a Windows Scrap Mechanic Survival integration built around an existing,
licensed Steam installation and a small interactive-session agent. The normal
Steam launch is `-applaunch 387990 -dedicated_server -last_save`. Real multiplayer
hosting was observed while the game remained on its loading screen. No standalone
SteamCMD dedicated-server distribution is claimed.

The template and agent are offered under MIT. This is AI-assisted development
with executed automated tests and live operator testing; it is not a claim of
independent human source review or official support.

## What has been demonstrated

On Windows 11, Ryzen 7 6800U/Radeon 680M, 32 GB installed RAM, an HDMI dummy plug,
existing authenticated Steam, and AMP 2.8.0.10:

- Local template discovery, instance creation, 12 settings and console controls.
- AMP controlling an agent in interactive session 1 through a restricted pipe.
- Steam launch, process detection, idempotent repeated start, and remote Survival
  play confirmed by the operator.
- WM_CLOSE shutdown, completed stopped backups, relaunch, and one valid world
  persistence trial after player logout.
- AMP Schedule's Restart action stopped, backed up, and relaunched the host.

See [VALIDATION.md](VALIDATION.md) for results and limits. The game was later
returned to the operator's intended world and its connection was confirmed.

## Architecture and access

AMP starts a console bridge. A separately registered Windows logon task runs the
agent as the normal game user with an interactive token and limited privilege.
Both roles use the same build. A named pipe permits the game user and configured
AMP Windows identity; network logons are denied, and clients verify the pipe owner.
No TCP management service or stored Windows/Steam password is introduced.

The agent matches executable path, desktop session, PID and creation time. Steam's
launcher PID is not treated as the game PID. No automatic force-kill or crash
restart is enabled. A local policy and explicit confirmed command are both
required for emergency termination. Administrators remain trusted.

If the bridge ends, the independent agent continues owning the game. Persistent
interlocks block launches after unresolved startup or incomplete stop/backup.
Backup ZIPs cover the selected save directory with a SHA-256 manifest; retention
is per source directory and only verified project archives qualify for pruning.

## Fit with current AMPTemplates guidance

Guidance checked 2026-11-02: [official README](https://github.com/CubeCoders/AMPTemplates/blob/main/README.md).

| Upstream expectation | Current integration |
|---|---|
| Download without application authentication, except SteamCMD | Requires an existing licensed, signed-in Steam client; no download automation. Maintainer decision needed. |
| Update operation | Empty update manifest; Steam owns installation and updates. No automatic game or agent updater. |
| Windows/Linux validation, Proton where feasible | Windows desktop session only. Linux/Proton is not implemented or tested. |
| Launch an executable, use settings manifests | Met: native EXE or `dotnet.exe` bridge; 12 mapped settings. PowerShell scripts are operator installation tools. |
| Lowercase template files at repository top level | Five lowercase files in `amp/`; copy only those after agreeing on integration scope. Local `manifest.json` is deployment metadata. |
| Ready and stopped states | Running marker indicates process observation. Multiplayer readiness is not queried; stopped requires game exit and any requested backup. |
| Tested contribution | Core paths tested on one Windows installation; remaining acceptance cases are explicitly open. |

This is not currently a drop-in catalogue submission. A separately hosted
MIT companion project with a reviewed release/download mechanism, or documentation
for an advanced local template, may be a better initial home. That is a proposal,
not an existing CubeCoders requirement or approval.

## Review issues and known limits

1. AMP 2.8.0.10 rewrote the template without retaining `App.ExitTimeout=900`.
   Short stop/restart trials passed; delayed-stop/large-backup timing and bridge
   detachment need a synthetic live test. Do not invent a replacement key.
2. The initial account had controller application permissions but child Start was
   denied. The operator made their own account super administrator and refreshed
   the session. No users or roles were changed by the implementation. A minimum
   AMP permission recipe still needs validation; superadmin is not a requirement.
3. AMP CPU/RAM metrics measure the bridge. Player counts, RCON, explicit save
   acknowledgement, game readiness and verified port/server-list discovery are absent.
4. `-last_save` follows the game's last selection. Selecting a different world
   normally changes the next host launch. Backup SaveDirectory is not world selection.
5. There is no unattended game startup before Windows login/Steam authentication.
   Steam updates or dialogs may delay a launch. Only one agent/host is supported.
6. Restore, reboot recovery, live timeout/failure handling and extended Parsec
   disconnect testing are pending. Optional direct-executable/URI modes are untested.

## Suggested next step

Ask whether CubeCoders wants to review this advanced Windows-only integration,
what distribution model they would accept for the companion, and whether the
authenticated Steam prerequisite can be accommodated. Resolve that direction
before submitting the normal five-file template PR. The community project is
hosted at [ptempleton/scrap-mechanic-amp](https://github.com/ptempleton/scrap-mechanic-amp).
No maintainer contact or AMPTemplates pull request has been made by this work.
