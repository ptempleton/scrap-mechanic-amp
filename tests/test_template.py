import importlib.util
import json
import shutil
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('validator', ROOT / 'scripts/validate-template.py')
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)


class TemplateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        for name in ('amp', 'docs', 'config'):
            shutil.copytree(ROOT / name, self.root / name)

    def tearDown(self):
        self.temp.cleanup()

    def test_template(self):
        self.assertEqual(validator.validate(self.root), (44, 12))

    def test_invented_key_rejected(self):
        path = self.root / 'amp/scrapmechanic.kvp'
        path.write_text(path.read_text() + '\nApp.InventedSafetySwitch=True\n')
        with self.assertRaises(AssertionError):
            validator.validate(self.root)

    def test_force_kill_rejected(self):
        path = self.root / 'amp/scrapmechanic.kvp'
        path.write_text(path.read_text().replace('App.ExitMethod=String', 'App.ExitMethod=Kill'))
        with self.assertRaises(AssertionError):
            validator.validate(self.root)

    def test_setting_drift_rejected(self):
        path = self.root / 'config/host-settings.example.json'
        data = json.loads(path.read_text())
        data['Arguments'] = '-different'
        path.write_text(json.dumps(data))
        with self.assertRaises(AssertionError):
            validator.validate(self.root)


if __name__ == '__main__':
    unittest.main()
