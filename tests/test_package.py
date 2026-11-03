import importlib.util
import json
import tempfile
import unittest
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('packager', ROOT / 'scripts/package-release.py')
packager = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packager)


class PackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name) / 'source'
        for name in packager.FILES:
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('review fixture\n')
        (self.root / 'src').mkdir()
        (self.root / 'src/example.cs').write_text('// Source fixture\n')
        self.publish = Path(self.temp.name) / 'publish'
        self.publish.mkdir()
        for name in packager.BINARIES:
            (self.publish / name).write_bytes(b'fixture binary')
        self.report = Path(self.temp.name) / 'report.md'
        self.report.write_text('Synthetic package test fixture; no game test claimed.\n')
        self.out = Path(self.temp.name) / 'out'

    def tearDown(self):
        self.temp.cleanup()

    def create(self, out=None):
        return packager.create(self.root, self.publish, self.report,
                               out or self.out, '0.1.0-review.1', 'a' * 40)

    def test_curated_inventory_and_reproducibility(self):
        for name in ('docs/TEST-RESULTS.md', 'config/client.json', '.git/config',
                     'world/test.db', 'src/obj/private.cs', 'private.txt'):
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('PRIVATE-AUDIT-SENTINEL')
        archive = self.create()
        self.assertGreater(packager.verify(archive), 20)
        with zipfile.ZipFile(archive) as z:
            self.assertFalse(any(b'PRIVATE-AUDIT-SENTINEL' in z.read(n) for n in z.namelist()))
            manifest = json.loads(z.read('ScrapMechanic-AMP-0.1.0-review.1/PACKAGE-MANIFEST.json'))
            self.assertEqual(manifest['sourceRevision'], 'a' * 40)
        other = self.create(Path(self.temp.name) / 'second')
        self.assertEqual(archive.read_bytes(), other.read_bytes())
        with self.assertRaises(FileExistsError):
            self.create()

    def test_tampering_rejected(self):
        archive = self.create()
        altered = Path(self.temp.name) / 'tampered.zip'
        with zipfile.ZipFile(archive) as source, zipfile.ZipFile(altered, 'w') as dest:
            for name in source.namelist():
                data = b'changed' if name.endswith('/LICENSE') else source.read(name)
                dest.writestr(name, data)
        with self.assertRaisesRegex(ValueError, 'Integrity mismatch'):
            packager.verify(altered)

    def test_private_source_or_binary_path_rejected(self):
        path = self.root / 'README.md'
        path.write_text('Host: ' + '.'.join(('192', '168', '99', '12')))
        with self.assertRaisesRegex(ValueError, 'Private deployment'):
            self.create()
        path.write_text('safe')
        private_path = '/'.join(('', 'Users', 'example', 'build', '')).encode()
        (self.publish / packager.BINARIES[0]).write_bytes(b'PDB ' + private_path)
        with self.assertRaisesRegex(ValueError, 'Private deployment'):
            self.create()

    def test_unexpected_runtime_configuration_rejected(self):
        (self.publish / 'client.json').write_text('{}')
        with self.assertRaisesRegex(ValueError, 'only the four portable'):
            self.create()

    def test_broken_documentation_link_rejected(self):
        (self.root / 'README.md').write_text('[Missing](missing.md)')
        with self.assertRaisesRegex(ValueError, 'Missing local link'):
            self.create()

    def test_linked_source_rejected(self):
        path = self.root / 'README.md'
        path.unlink()
        try:
            path.symlink_to(self.report)
        except OSError:
            self.skipTest('Symlinks require an available OS privilege')
        with self.assertRaisesRegex(ValueError, 'linked'):
            self.create()


if __name__ == '__main__':
    unittest.main()
