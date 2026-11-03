#!/usr/bin/env python3
"""Offline structural/evidence checks; not an AMP runtime validation."""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def validate(root=ROOT):
    evidence = json.loads((root / 'docs/amp-key-evidence.json').read_text())['sources']
    repository = json.loads((root / 'amp/manifest.json').read_text())
    assert set(repository) == set(evidence['manifest.json']['keys']), 'Unverified repository manifest key'
    assert repository['repotype'] == 'AppTemplates'
    assert repository['origin'] == repository['url'] == '', 'Local install must not fetch a remote repository'
    lines = (root / 'amp/scrapmechanic.kvp').read_text().splitlines()
    pairs = [line.split('=', 1) for line in lines if line and not line.startswith('#')]
    kvp = dict(pairs)
    assert len(kvp) == len(pairs), 'Duplicate KVP keys'
    assert set(kvp) <= set(evidence['abiotic-factor.kvp']['keys']), 'Unverified AMP KVP key'
    for value in kvp.values():
        if value.startswith('@IncludeJson['):
            name = value[len('@IncludeJson['):-1]
            json.loads((root / 'amp' / name).read_text())
    settings = json.loads((root / 'amp' / kvp['Meta.ConfigManifest']).read_text())
    meta = json.loads((root / 'amp' / kvp['Meta.MetaConfigManifest']).read_text())
    seed = json.loads((root / 'config/host-settings.example.json').read_text())
    assert {s['FieldName'] for s in settings} == set(seed), 'Settings/seed mismatch'
    assert len({s['FieldName'] for s in settings}) == len(settings)
    for setting in settings:
        assert set(setting) <= set(evidence['abiotic-factorconfig.json']['keys']), 'Unverified setting key'
        assert setting['ParamFieldName'] == setting['FieldName']
        actual, default = str(seed[setting['FieldName']]), setting['DefaultValue']
        assert actual.lower() == default.lower(), f'Default mismatch: {setting["FieldName"]}'
    for mapping in meta:
        assert set(mapping) <= set(evidence['puckmetaconfig.json']['keys']), 'Unverified metaconfig key'
        assert mapping['ConfigType'] == 'json' and mapping['AutoMap'] is True
    assert meta[0]['ConfigFile'] == 'host-settings.json'
    assert kvp['App.ExitMethod'] == 'String' and kvp['App.ExitString'] == 'stop'
    assert kvp['App.MonitorChildProcessName'] == ''
    assert kvp['Limits.AutoRetryCount'] == '0'
    assert kvp['App.ApplicationReadyMode'] == 'RegexMatch'
    ready = kvp['Console.AppReadyRegex']
    assert re.fullmatch(ready, 'SM_PROCESS_RUNNING multiplayer_readiness=unverified')
    assert not re.fullmatch(ready, 'agent started')
    assert json.loads((root / 'amp/scrapmechanicupdates.json').read_text()) == []
    assert json.loads((root / 'amp/scrapmechanicports.json').read_text()) == []
    assert int(kvp['App.ExitTimeout']) > seed['ShutdownTimeoutSeconds'] + seed['StoppedSettleSeconds']
    for name in ['agent', 'client']:
        json.loads((root / f'config/{name}.example.json').read_text())
    return len(kvp), len(settings)


if __name__ == '__main__':
    keys, fields = validate()
    print(f'PASS: {keys} evidenced AMP keys; {fields} mapped settings; references and safety invariants.')
