import importlib.util
import os
from pathlib import Path
import shutil
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('media_prepare', Path(__file__).with_name('prepare.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class MediaPreparationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='noctf-media-prepare-')
        self.root = Path(self.temporary.name)
        self.installation = self.root / 'installation'
        self.spool = self.root / 'spool'
        self.installation.mkdir(); self.spool.mkdir()
        (self.installation / 'docker-compose.yml').write_text('services: {}\n', encoding='utf-8')
        (self.installation / '.env').write_text('', encoding='utf-8')
    def tearDown(self):
        self.temporary.cleanup()
    def test_unbounded_directory_is_rejected_before_any_private_file_is_written(self):
        with patch.object(module.os.path, 'ismount', return_value=False):
            with self.assertRaises(ValueError):
                module.prepare(self.installation,self.spool,'media.example.test','192.0.2.10',256*1024*1024,2001)
        self.assertFalse((self.installation / 'config/live-solo').exists())
    @unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0, 'Private group permissions require an isolated Linux root test.')
    def test_preparation_generates_distinct_private_keys_without_starting_services_and_refuses_overwrite(self):
        os.chown(self.spool,-1,2001);os.chmod(self.spool,0o770)
        result=module.subprocess.CompletedProcess([],0,'ext4\n','')
        usage=shutil._ntuple_diskusage(128*1024*1024,0,128*1024*1024)
        with patch.object(module.os.path,'ismount',return_value=True), patch.object(module.shutil,'disk_usage',return_value=usage), patch.object(module.subprocess,'run',return_value=result) as command:
            module.prepare(self.installation,self.spool,'media.example.test','192.0.2.10',256*1024*1024,2001)
            self.assertEqual(command.call_args.args[0][-2:],['config','--quiet'])
            with self.assertRaises(ValueError):
                module.prepare(self.installation,self.spool,'media.example.test','192.0.2.10',256*1024*1024,2001)
        config=self.installation/'config/live-solo'
        self.assertEqual((config/'egress.yaml').stat().st_mode&0o777,0o640)
        self.assertEqual((config/'egress.yaml').stat().st_gid,2001)
        self.assertIn('auto_create: false',(config/'livekit.yaml').read_text())
        self.assertIn('maxmemory-policy noeviction',(config/'redis.conf').read_text())
        self.assertIn('wss://media.example.test',(self.installation/'env/live-solo/noctf.env').read_text())
        self.assertEqual(self.spool.stat().st_mode&0o777,0o770)
        staging=self.spool/'.egress-tmp'
        self.assertEqual(staging.stat().st_mode&0o7777,0o2770)
        self.assertEqual(staging.stat().st_gid,2001)
        self.assertIn('file_output_max_size: 536870912',(config/'egress.yaml').read_text())
    @unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0, 'Linux group validation')
    def test_capacity_above_the_explicit_bound_is_rejected(self):
        os.chown(self.spool,-1,2001);os.chmod(self.spool,0o770)
        usage=shutil._ntuple_diskusage(1024*1024*1024,0,1024*1024*1024)
        with patch.object(module.os.path,'ismount',return_value=True),patch.object(module.shutil,'disk_usage',return_value=usage),patch.object(module.subprocess,'run',return_value=module.subprocess.CompletedProcess([],0,'ext4\n','')):
            with self.assertRaises(ValueError):
                module.prepare(self.installation,self.spool,'media.example.test','192.0.2.10',256*1024*1024,2001)
        self.assertFalse((self.installation/'config/live-solo').exists())
    @unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0, 'Linux mount validation')
    def test_ephemeral_filesystems_and_unsafe_paths_cannot_generate_media_secrets(self):
        os.chown(self.spool,-1,2001);os.chmod(self.spool,0o770)
        with patch.object(module.os.path,'ismount',return_value=True),patch.object(module.subprocess,'run',return_value=module.subprocess.CompletedProcess([],0,'tmpfs\n','')):
            with self.assertRaises(ValueError):
                module.prepare(self.installation,self.spool,'media.example.test','192.0.2.10',256*1024*1024,2001)
        self.assertFalse((self.installation/'config/live-solo').exists())
    @unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0, 'Linux staging validation')
    def test_existing_or_linked_staging_is_rejected_before_private_configuration(self):
        os.chown(self.spool,-1,2001);os.chmod(self.spool,0o770)
        staging=self.spool/'.egress-tmp'
        staging.symlink_to(self.root/'missing-target',target_is_directory=True)
        usage=shutil._ntuple_diskusage(128*1024*1024,0,128*1024*1024)
        with patch.object(module.os.path,'ismount',return_value=True),patch.object(module.shutil,'disk_usage',return_value=usage),patch.object(module.subprocess,'run',return_value=module.subprocess.CompletedProcess([],0,'ext4\n','')):
            with self.assertRaises(ValueError):
                module.prepare(self.installation,self.spool,'media.example.test','192.0.2.10',256*1024*1024,2001)
        self.assertTrue(staging.is_symlink())
        self.assertFalse((self.installation/'config/live-solo').exists())

if __name__=='__main__': unittest.main()
