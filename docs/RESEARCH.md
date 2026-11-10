# Research and architecture

Research date: 2026-11-01. AMPTemplates revision inspected:
`36d02437561729635ca5f07574d7fb58e7c41bfc` (2026-10-28).

## AMP integration

The [official Generic Module guide](https://github.com/CubeCoders/AMP/wiki/Configuring-the-'Generic'-AMP-module)
documents executable paths, argument substitution, standard input/output control,
`WM_CLOSE`, settings manifests, and local template directories. Its linked schemas
are maintained in another contributor's repository; working official templates are
the primary key evidence here.

The template consists of `.kvp` metadata/runtime configuration, `config.json`
settings, `metaconfig.json` file mappings, `ports.json`, and `updates.json`.
`start.json` is optional. Our updates/ports arrays are empty: Steam owns game
installation, and we have no verified Scrap Mechanic server port contract.

Every KVP key used is present in the pinned
[Abiotic Factor template](https://github.com/CubeCoders/AMPTemplates/blob/36d02437561729635ca5f07574d7fb58e7c41bfc/abiotic-factor.kvp).
JSON AutoMap follows the pinned
[Puck metaconfig](https://github.com/CubeCoders/AMPTemplates/blob/36d02437561729635ca5f07574d7fb58e7c41bfc/puckmetaconfig.json).
`amp-key-evidence.json` records the inspected key sets. The validator checks our
keys and references, not AMP's runtime behavior. AMP must still load the template
on Windows during acceptance testing.

## Why a session agent

Windows services run in session 0 and cannot directly interact with the user's
desktop. Microsoft recommends a separate user-session process and secured IPC:
[Interactive Services](https://learn.microsoft.com/en-us/windows/win32/services/interactive-services).
AMP can launch a Windows executable; this is not evidence that a service can
launch Steam into the correct logged-in desktop. Direct game management is only
plausible if AMP itself runs interactively in that same session; it is not the
supported installation here.

One .NET executable has three roles:

1. A scheduled task runs `agent` with the game user's interactive token.
2. AMP runs `bridge`, which sends requests to the agent and exposes a text console.
3. A one-shot CLI sends start/stop/status/restart/backup commands.

The agent and game are outside AMP's process tree. AMP's stop string requests
`WM_CLOSE` through the agent; only actual process exit allows a successful stop.
If AMP kills a timed-out bridge, the game is left running. Starting another bridge
reconnects to the same agent. No child-process tracking regex points AMP at the game.
AMP CPU/memory charts therefore describe the bridge, not the game.

The pipe grants access only to the game user and one configured AMP identity,
and explicitly denies network logons. No TCP listener, password, Steam credential,
or elevated interactive service is needed. See
[pipe ACL guidance](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights)
and [.NET pipe ACL API](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.namedpipeserverstreamacl.create).
The AMP account is trusted to operate the game user's host; restrict AMP console,
configuration, and file-manager permissions accordingly.

## Persistence and scope

The supplied desktop experiment proves remote Survival play and that some world
changes survived a restart. It does not prove Alt+F4 flushes saves, nor performance
on the 6800U/680M. WM_CLOSE is a graceful exit request, not a save acknowledgement.
There is no claimed game RCON, save command, player query, or multiplayer-ready probe.

On 2026-11-02, the user confirmed that the Scrap Mechanic process shows **0% GPU
utilization** in the hosting setup. This updates the earlier GPU figure and is
recorded as user-confirmed runtime evidence. Desktop/session requirements remain
a separate part of the architecture.

The agent discovers `User_*/Save/Survival` beneath the game user's roaming
`Axolot Games/Scrap Mechanic/User` directory. This is a candidate convention,
not a verified save API. Multiple candidates require an explicit path. An operator
must confirm the selected directory with a disposable world before real use.
Backups copy the entire selected directory, including sidecars and subdirectories.

Only stopped backups are supported. AMP's own file backup is not relied on for
external user-profile saves. It may separately archive completed backup ZIPs if
the operator configures it to include their directory. Restart aborts on failed
shutdown or failed requested backup. Automatic crash restart is disabled by default. Optional bounded recovery is owned
by the agent, with persisted attempt history and separate post-crash archives;
AMP automatic retries remain disabled. See [CRASH-RECOVERY.md](CRASH-RECOVERY.md).

This is a private Windows integration, not a claim of official AMP support or an
upstream-ready template. The [upstream submission rules](https://github.com/CubeCoders/AMPTemplates#sharing-templates)
include installation and platform requirements this existing-Steam design does
not fulfill.
