#!/usr/bin/env python3
"""Prepare private media configuration; never start services or alter mounts."""
import argparse
import ipaddress
import json
import os
from pathlib import Path
import secrets
import shutil
import subprocess


def prepare(installation: Path, spool: Path, domain: str, rtc_ip: str, maximum_bytes: int, media_gid: int) -> None:
    installation = installation.resolve(strict=True)
    spool = spool.resolve(strict=True)
    if installation == Path('/') or spool == Path('/') or not (installation / 'docker-compose.yml').is_file():
        raise ValueError('Use an existing dedicated NoCTF installation and media mount.')
    if '\n' in str(spool) or '\r' in str(spool) or '$' in str(spool):
        raise ValueError('The media mount path cannot contain environment interpolation or line breaks.')
    if os.name != 'posix' or not os.path.ismount(spool):
        raise ValueError('The media spool must be a dedicated Linux mount with an enforced fixed capacity.')
    filesystem = subprocess.run(['findmnt', '--noheadings', '--output', 'FSTYPE', '--target', str(spool)], capture_output=True, text=True, check=True).stdout.strip()
    if filesystem in ('tmpfs', 'ramfs', 'overlay') or not filesystem:
        raise ValueError('Production media storage must survive container and host restarts; use a dedicated persistent filesystem.')
    if media_gid <= 0 or spool.stat().st_gid != media_gid or spool.stat().st_mode & 0o020 == 0 or spool.stat().st_mode & 0o007 != 0:
        raise ValueError('Assign the dedicated media group writable spool permissions with no other-user access first.')
    if (len(domain)>253 or any(char not in 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-' for char in domain) or '.' not in domain
        or any(not label or len(label)>63 or label.startswith('-') or label.endswith('-') for label in domain.split('.'))):
        raise ValueError('Provide a valid public media DNS host.')
    ipaddress.ip_address(rtc_ip)
    storage = shutil.disk_usage(spool)
    if maximum_bytes < 64 * 1024 * 1024 or storage.total > maximum_bytes or storage.free < 32 * 1024 * 1024:
        raise ValueError('The dedicated spool capacity/free space does not meet the explicit bound.')
    config = installation / 'config/live-solo'
    environment = installation / 'env/live-solo'
    if config.exists() or environment.exists():
        raise ValueError('Media configuration already exists; inspect and update it explicitly.')
    if (installation / 'compose.live-solo.yml').exists():
        raise ValueError('The media overlay already exists; inspect it explicitly rather than overwriting it.')
    staging = spool / '.egress-tmp'
    if staging.exists() or staging.is_symlink():
        raise ValueError('The Egress staging directory already exists; inspect it explicitly.')
    staging.mkdir(mode=0o2770)
    os.chown(staging, -1, media_gid)
    os.chmod(staging, 0o2770)
    config.mkdir(parents=True, mode=0o700)
    environment.mkdir(parents=True, mode=0o700)
    os.chown(config, -1, media_gid)
    os.chmod(config, 0o750)
    key, secret, redis_secret = secrets.token_hex(16), secrets.token_hex(32), secrets.token_hex(32)
    def write(path: Path, value: str) -> None:
        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, 'w', encoding='utf-8') as stream:
            stream.write(value)
        if path.parent == config:
            os.chown(path, -1, media_gid)
            os.chmod(path, 0o640)
    write(config / 'redis.conf', 'bind 0.0.0.0\nprotected-mode yes\nrequirepass ' + redis_secret + '\nmaxmemory 512mb\nmaxmemory-policy noeviction\nappendonly no\nsave ""\n')
    write(config / 'livekit.yaml', f'port: 7880\nrtc:\n  tcp_port: 7881\n  udp_port: 7882\n  use_external_ip: false\n  node_ip: {rtc_ip}\nredis:\n  address: media-redis:6379\n  password: {redis_secret}\nkeys:\n  {key}: {secret}\nroom:\n  auto_create: false\n  max_participants: 64\nlogging:\n  level: error\n')
    write(config / 'egress.yaml', f'api_key: {key}\napi_secret: {secret}\nws_url: ws://media-sfu:7880\ninsecure: true\nredis:\n  address: media-redis:6379\n  password: {redis_secret}\nhealth_port: 8080\nlogging:\n  level: error\nsession_limits:\n  file_output_max_duration: 30m\n  file_output_max_size: 536870912\n  segment_output_max_duration: 30m\n')
    write(environment / 'noctf.env', f'LiveSolo__Media__Enabled=true\nLiveSolo__Media__ApiUrl=http://media-sfu:7880\nLiveSolo__Media__ClientUrl=wss://{domain}\nLiveSolo__Media__ApiKey={key}\nLiveSolo__Media__ApiSecret={secret}\nLiveSolo__Media__EgressHealthUrl=http://media-egress:8080\nLiveSolo__Media__EgressOutputRoot=/out\nLiveSolo__Media__CaptureSpoolPath=/out\nLiveSolo__Media__ProgramChunkSeconds=300\nLiveSolo__Media__RecordingExportLimitBytes=536870912\n')
    write(environment / 'compose.env', f'LIVE_SOLO_SPOOL_PATH={spool}\nLIVE_SOLO_MEDIA_GID={media_gid}\n')
    source = Path(__file__).resolve().parent.parent
    shutil.copyfile(source / 'compose.live-solo.yml', installation / 'compose.live-solo.yml')
    os.chmod(installation / 'compose.live-solo.yml', 0o600)
    # Provision the dedicated Egress/Host group before preparing; never widen host filesystem access here.
    result = subprocess.run(['docker', 'compose', '--project-directory', str(installation), '--env-file', str(installation / '.env'),
        '--env-file', str(environment / 'compose.env'), '-f', str(installation / 'docker-compose.yml'), '-f', str(installation / 'compose.live-solo.yml'),
        'config', '--quiet'], capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError('Media Compose validation failed. Private configuration is preserved for inspection; no service was started.')
    print(json.dumps({'prepared': True, 'capacity_bytes': storage.total, 'services_started': False}))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--installation', required=True, type=Path)
    parser.add_argument('--spool', required=True, type=Path)
    parser.add_argument('--media-host', required=True)
    parser.add_argument('--rtc-ip', required=True)
    parser.add_argument('--maximum-spool-bytes', required=True, type=int)
    parser.add_argument('--media-gid', required=True, type=int)
    arguments = parser.parse_args()
    try:
        prepare(arguments.installation, arguments.spool, arguments.media_host, arguments.rtc_ip, arguments.maximum_spool_bytes, arguments.media_gid)
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        parser.exit(1, str(error) + '\n')
