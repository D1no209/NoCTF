import copy
from pathlib import Path
import unittest
from unittest.mock import patch
from types import SimpleNamespace
from update_image import application_containers, validate_existing_config, deploy


def fixture():
    container = {"Config": {"Entrypoint": ["dotnet", "NoCTF.Host.dll"], "Env": ["KEY=original"], "Labels": {
        "com.docker.compose.project": "deploy", "com.docker.compose.service": "backend",
        "com.docker.compose.project.environment_file": "/root/NoCTF/.env,/root/NoCTF/monitoring.env"}},
        "Mounts": [{"Type": "volume", "Name": "deploy_uploads", "Source": "/var/lib/docker/volumes/deploy_uploads/_data",
                    "Destination": "/app/uploads", "RW": True}]}
    config = {"services": {"backend": {"environment": {"KEY": "original"}, "volumes": [
        {"type": "volume", "source": "uploads", "target": "/app/uploads"}]}}, "volumes": {"uploads": {"name": "deploy_uploads"}}}
    return container, config


class DeploymentSafetyTests(unittest.TestCase):
    def test_ci_rejects_the_production_host_before_any_deployment_io(self):
        with patch("update_image.socket.gethostname", return_value="dino209"):
            with self.assertRaisesRegex(RuntimeError, "CI must never deploy to production"):
                deploy(SimpleNamespace(manual_production=False))

    def test_manual_production_flag_cannot_target_a_different_host(self):
        with patch("update_image.socket.gethostname", return_value="test-server"):
            with self.assertRaisesRegex(RuntimeError, "verified production host"):
                deploy(SimpleNamespace(manual_production=True))

    def test_selects_only_application_services_in_the_exact_config_root(self):
        app, _ = fixture()
        foreign = copy.deepcopy(app)
        foreign["Config"]["Labels"]["com.docker.compose.project.environment_file"] = "/root/Another/.env"
        exporter = copy.deepcopy(app)
        exporter["Config"]["Labels"]["com.docker.compose.service"] = "prometheus"
        self.assertEqual(application_containers([app, foreign, exporter], Path("/root/NoCTF")), [app])

    def test_rejects_multiple_projects_or_replicas(self):
        app, _ = fixture()
        with self.assertRaises(RuntimeError):
            application_containers([app, app], Path("/root/NoCTF"))
        other = copy.deepcopy(app)
        other["Config"]["Labels"]["com.docker.compose.project"] = "another"
        with self.assertRaises(RuntimeError):
            application_containers([app, other], Path("/root/NoCTF"))

    def test_accepts_the_exact_existing_named_volume(self):
        app, config = fixture()
        validate_existing_config(config, [app])

    def test_rejects_empty_directory_replacement_or_different_volume(self):
        app, config = fixture()
        for mount in [{"type": "bind", "source": "/root/NoCTF/data/uploads", "target": "/app/uploads"},
                      {"type": "volume", "source": "new_empty_uploads", "target": "/app/uploads"}]:
            config["services"]["backend"]["volumes"] = [mount]
            with self.assertRaises(RuntimeError):
                validate_existing_config(config, [app])

    def test_accepts_existing_bind_mount_without_converting_it(self):
        app, config = fixture()
        app["Mounts"][0].update(Type="bind", Source="/srv/noctf/uploads")
        config["services"]["backend"]["volumes"][0].update(type="bind", source="/srv/noctf/uploads")
        validate_existing_config(config, [app])

    def test_rejects_removed_mount_or_changed_access_mode(self):
        app, config = fixture()
        config["services"]["backend"]["volumes"][0]["read_only"] = True
        with self.assertRaises(RuntimeError):
            validate_existing_config(config, [app])
        config["services"]["backend"]["volumes"] = []
        with self.assertRaises(RuntimeError):
            validate_existing_config(config, [app])

    def test_environment_mismatch_does_not_disclose_secret_values(self):
        app, config = fixture()
        config["services"]["backend"]["environment"]["KEY"] = "secret-replacement"
        with self.assertRaises(RuntimeError) as error:
            validate_existing_config(config, [app])
        self.assertIn("KEY", str(error.exception))
        self.assertNotIn("secret-replacement", str(error.exception))


if __name__ == "__main__":
    unittest.main()
