#!/usr/bin/env python3
"""Create/verify a curated portable review ZIP. Does not install or publish anything."""
import argparse
import hashlib
import json
import re
import zipfile
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parents[1]
FILES = (
    'LICENSE', 'NOTICE.md', 'START-HERE.md', 'README.md', 'INSTALL.md',
    'TESTING.md', 'TROUBLESHOOTING.md', '.gitignore', 'global.json',
    'Directory.Build.props', 'ScrapMechanic.slnx', '.github/workflows/build.yml',
    'docs/RESEARCH.md', 'docs/amp-key-evidence.json', 'docs/MAINTAINER-REVIEW.md',
    'docs/SUBMISSION-DRAFT.md', 'docs/VALIDATION.md', 'docs/PACKAGING.md',
    'amp/manifest.json', 'amp/scrapmechanic.kvp', 'amp/scrapmechanicconfig.json',
    'amp/scrapmechanicmetaconfig.json', 'amp/scrapmechanicports.json',
    'amp/scrapmechanicupdates.json', 'config/agent.example.json',
    'config/client.example.json', 'config/host-settings.example.json',
    'scripts/Build.ps1', 'scripts/Test.ps1', 'scripts/Install-Template.ps1',
    'scripts/Install-Host.ps1', 'scripts/Remove-Host.ps1', 'scripts/Package.ps1',
    'scripts/validate-template.py', 'scripts/package-release.py',
)
BINARIES = (
    'ScrapMechanic.Host.dll', 'ScrapMechanic.Host.Core.dll',
    'ScrapMechanic.Host.deps.json', 'ScrapMechanic.Host.runtimeconfig.json',
)
PRIVATE_PATTERNS = (
    rb'/Users/[A-Za-z0-9_.-]+/',
    rb'(?i)[A-Z]:\\Users\\(?!GameUser\\|Example\\)[^\\\s]+\\',
    rb'\b192\.168\.\d{1,3}\.\d{1,3}\b',
    rb'(?i)authorization\s*[:=]\s*bearer\s+[a-z0-9_-]{16,}',
    rb'(?i)["\'](?:password|sessionID|access_token)["\']\s*:\s*["\'][^"\']+["\']',
)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def scan(name, data):
    for pattern in PRIVATE_PATTERNS:
        if re.search(pattern, data):
            # Never echo a potentially sensitive matching value.
            raise ValueError(f'Private deployment information detected in {name}')


def read_regular(path):
    if path.is_symlink() or not path.is_file():
        raise ValueError(f'Required regular file missing or linked: {path.name}')
    return path.read_bytes()


def payload(root, publish, report):
    files = {name: read_regular(root / name) for name in FILES}
    for directory in ('src', 'tests'):
        for path in sorted((root / directory).rglob('*')):
            relative = path.relative_to(root)
            if any(p in {'bin', 'obj', '__pycache__', '.git'} for p in relative.parts):
                continue
            if path.suffix in {'.cs', '.csproj', '.py'}:
                files[relative.as_posix()] = read_regular(path)
    if not any(name.startswith('src/') for name in files):
        raise ValueError('Source tree is missing')
    unexpected = {p.name for p in publish.iterdir()} - set(BINARIES)
    if unexpected:
        raise ValueError('Publish directory must contain only the four portable release files; rebuild without debug symbols')
    for name in BINARIES:
        files['artifacts/publish/' + name] = read_regular(publish / name)
    files['PACKAGE-VERIFICATION.md'] = read_regular(report)
    # Do not redistribute the private chronological deployment audit or Git history.
    files['docs/TEST-RESULTS.md'] = (
        '# Public validation summary\n\n'
        'See [VALIDATION.md](VALIDATION.md) for executed Windows/live checks and\n'
        'remaining coverage. See [the package report](../PACKAGE-VERIFICATION.md)\n'
        'for this packaging run. Private deployment notes are excluded.\n'
    ).encode()
    for name, data in files.items():
        scan(name, data)
    return files


def check_links(files):
    for name, data in files.items():
        if not name.endswith('.md'):
            continue
        for target in re.findall(r'\[[^\]]*\]\(([^)]+)\)', data.decode('utf-8')):
            target = target.strip('<>').split('#', 1)[0]
            if not target or '://' in target or target.startswith('mailto:'):
                continue
            parts = list(PurePosixPath(name).parent.parts)
            for part in PurePosixPath(target).parts:
                if part == '..':
                    if not parts:
                        raise ValueError(f'Link escapes archive in {name}')
                    parts.pop()
                elif part != '.':
                    parts.append(part)
            if '/'.join(parts) not in files:
                raise ValueError(f'Missing local link from {name}: {target}')


def create(root, publish, report, output, version, revision):
    if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+(?:-[a-zA-Z0-9.-]+)?', version):
        raise ValueError('Invalid release version')
    if not re.fullmatch(r'[0-9a-f]{40}', revision):
        raise ValueError('Source revision must be a full Git SHA-1')
    name = 'ScrapMechanic-AMP-' + version
    archive = output / (name + '.zip')
    checksum = output / (name + '.zip.sha256')
    if archive.exists() or checksum.exists():
        raise FileExistsError('Release already exists; use a new version or output directory')
    files = payload(root, publish, report)
    check_links(files)
    manifest = {
        'formatVersion': 1, 'name': 'ScrapMechanic-AMP', 'version': version,
        'sourceRevision': revision, 'license': 'MIT',
        'distribution': 'portable-framework-dependent',
        'platform': 'Windows x64 interactive session',
        'runtime': 'Microsoft.NETCore.App 10.x x64 (not included)',
        'signing': 'unsigned; hashes provide integrity, not publisher authentication',
        'files': {key: {'size': len(value), 'sha256': digest(value)}
                  for key, value in sorted(files.items())},
    }
    files['PACKAGE-MANIFEST.json'] = (json.dumps(manifest, indent=2) + '\n').encode()
    output.mkdir(parents=True, exist_ok=True)
    # Exclusive create prevents accidental overwrite. Stable metadata makes identical
    # source, binaries, version and report produce identical archive bytes.
    with zipfile.ZipFile(archive, 'x', compression=zipfile.ZIP_DEFLATED) as z:
        for path, data in sorted(files.items()):
            entry = zipfile.ZipInfo(name + '/' + path, date_time=(2026, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = 0o100644 << 16
            z.writestr(entry, data)
    verify(archive)
    with checksum.open('x') as f:
        f.write(digest(archive.read_bytes()) + '  ' + archive.name + '\n')
    return archive


def verify(archive):
    with zipfile.ZipFile(archive) as z:
        names = z.namelist()
        if len(names) != len(set(names)):
            raise ValueError('Duplicate archive paths')
        roots = {PurePosixPath(n).parts[0] for n in names if n}
        if len(roots) != 1:
            raise ValueError('Expected exactly one root directory')
        prefix = next(iter(roots)) + '/'
        for n in names:
            if '\\' in n or PurePosixPath(n).is_absolute() or '..' in PurePosixPath(n).parts:
                raise ValueError('Unsafe archive path')
        files = {n[len(prefix):]: z.read(n) for n in names}
        manifest = json.loads(files.pop('PACKAGE-MANIFEST.json'))
        if set(files) != set(manifest['files']):
            raise ValueError('Archive inventory differs from manifest')
        for name, data in files.items():
            expected = manifest['files'][name]
            if len(data) != expected['size'] or digest(data) != expected['sha256']:
                raise ValueError(f'Integrity mismatch: {name}')
            scan(name, data)
        check_links(files)
        return len(files)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    build = sub.add_parser('build')
    build.add_argument('--publish-directory', type=Path, required=True)
    build.add_argument('--verification-report', type=Path, required=True)
    build.add_argument('--output-directory', type=Path, default=ROOT / 'artifacts/releases')
    build.add_argument('--version', default='0.1.0-review.2')
    build.add_argument('--source-revision', required=True)
    check = sub.add_parser('verify')
    check.add_argument('archive', type=Path)
    args = parser.parse_args()
    if args.command == 'verify':
        print(f'PASS: {verify(args.archive)} manifest entries verified; safe paths and local documentation links checked')
    else:
        print(create(ROOT, args.publish_directory, args.verification_report,
                     args.output_directory, args.version, args.source_revision))


if __name__ == '__main__':
    main()
