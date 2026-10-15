#!/usr/bin/env python3
"""Read-only, credential-free Erebor inventory for KnowledgeVine release comparisons."""
import hashlib
import json
import os
from pathlib import Path
import socket
import subprocess
import urllib.request

ROOT = Path('/root/KnowledgeVine')


def command(args):
    result = subprocess.run(args, capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError('Read-only inventory command failed: ' + args[0])
    return result.stdout


def inspect():
    if socket.gethostname() != 'erebor.dedicated.co.za' or Path.cwd().resolve() != ROOT:
        raise RuntimeError('Expected Erebor KnowledgeVine working directory.')
    ids = command(['docker', 'ps', '-aq']).split()
    containers = json.loads(command(['docker', 'inspect', *ids])) if ids else []
    out = {'containers': [], 'protected_files': {}, 'knowledgevine_files': {}, 'services': {}, 'sites': []}
    for c in containers:
        labels = c['Config'].get('Labels') or {}
        out['containers'].append({
            'name': c['Name'].lstrip('/'), 'id': c['Id'], 'state': c['State']['Status'],
            'started': c['State']['StartedAt'], 'restarts': c['RestartCount'], 'image': c['Image'],
            'ports': c['NetworkSettings']['Ports'],
            'networks': {k: v['IPAddress'] for k, v in c['NetworkSettings']['Networks'].items()},
            'mounts': c['Mounts'], 'project': labels.get('com.docker.compose.project'),
            'service': labels.get('com.docker.compose.service'),
            'runtime_config_hash': hashlib.sha256(json.dumps(c['Config'], sort_keys=True).encode()).hexdigest(),
        })
    files = [
        '/etc/nginx/nginx.conf', '/etc/nginx/sites-enabled/gasontherun.conf',
        '/etc/postgresql/16/main/postgresql.conf', '/etc/postgresql/16/main/pg_hba.conf',
        '/etc/postgresql/16/main/pg_ident.conf', '/root/gotr/docker-compose.yml',
        '/root/gotr/releases/20261010-104532/operator/application.env',
        '/root/gotr/releases/20261010-104532/operator/feature-flags.json',
    ]
    for f in files:
        out['protected_files'][f] = hashlib.sha256(Path(f).read_bytes()).hexdigest()
    for f in [ROOT / 'docker-compose.yml', ROOT / 'api/Dockerfile', ROOT / 'webapp/nginx.conf',
              ROOT / 'api/publish.zip', ROOT / 'webapp/knowledevinefront.zip',
              Path('/etc/nginx/sites-available/knowledgevinemontessori.co.za')]:
        out['knowledgevine_files'][str(f)] = hashlib.sha256(f.read_bytes()).hexdigest()
    for unit in ['nginx.service', 'docker.service', 'postgresql@16-main.service']:
        out['services'][unit] = dict(line.split('=', 1) for line in command([
            'systemctl', 'show', unit, '--property=MainPID,ActiveEnterTimestamp,ActiveState,SubState'
        ]).strip().splitlines())
    for url in ['https://gasontherun.online/', 'https://www.knowledgevinemontessori.co.za/home']:
        with urllib.request.urlopen(url, timeout=15) as response:
            out['sites'].append({'url': url, 'status': response.status})
    out['existing_kv_db'] = any(c['name'] == 'knowledge-db' for c in out['containers'])
    disk = os.statvfs(ROOT)
    out['disk_free_gib'] = round(disk.f_bavail * disk.f_frsize / 2**30, 2)
    print(json.dumps(out))


if __name__ == '__main__':
    inspect()
