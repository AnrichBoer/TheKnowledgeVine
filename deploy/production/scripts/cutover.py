#!/usr/bin/env python3
"""Apply the reviewed KnowledgeVine release, with scoped rollback and GOTR preservation."""
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request

ROOT = Path('/root/KnowledgeVine')
SCHOOL = Path('/etc/nginx/sites-available/knowledgevinemontessori.co.za')


def command(args):
    result = subprocess.run(args, capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError('Release command failed: ' + args[0] + ' ' + args[1])
    return result.stdout


def atomic(path, data, mode):
    temporary = path.with_name(path.name + '.knowledgevine-tmp')
    fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, mode)
    with os.fdopen(fd, 'wb') as stream:
        stream.write(data)
    os.replace(temporary, path)


def probe(path, status=200):
    url = path if path.startswith('https://') else 'https://www.knowledgevinemontessori.co.za' + path
    try:
        response = urllib.request.urlopen(url, timeout=5)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        data = response.read()
        if response.status != status:
            raise RuntimeError('Unexpected release HTTP status for ' + path)
        return response.headers, data


def wait_for(path, status=200):
    for _ in range(30):
        try:
            return probe(path, status)
        except (OSError, RuntimeError):
            time.sleep(1)
    raise RuntimeError('Release did not become ready: ' + path)


def preserve(baseline):
    for name, expected in baseline['protected_files'].items():
        if hashlib.sha256(Path(name).read_bytes()).hexdigest() != expected:
            raise RuntimeError('Protected configuration changed: ' + name)
    for old in baseline['containers']:
        if not old['name'].startswith('gotr-'):
            continue
        c = json.loads(command(['docker', 'inspect', old['name']]))[0]
        checks = [c['Id'] == old['id'], c['Image'] == old['image'],
                  c['State']['Status'] == old['state'], c['State']['StartedAt'] == old['started'],
                  c['RestartCount'] == old['restarts'],
                  sorted(c['Mounts'], key=lambda m: m['Destination']) ==
                  sorted(old['mounts'], key=lambda m: m['Destination']),
                  c['NetworkSettings']['Ports'] == old['ports'],
                  {k: v['IPAddress'] for k, v in c['NetworkSettings']['Networks'].items()} == old['networks'],
                  hashlib.sha256(json.dumps(c['Config'], sort_keys=True).encode()).hexdigest() == old['runtime_config_hash']]
        if not all(checks):
            raise RuntimeError('GOTR container baseline changed: ' + old['name'])
    for unit, expected in baseline['services'].items():
        now = dict(line.split('=', 1) for line in command([
            'systemctl', 'show', unit, '--property=MainPID,ActiveEnterTimestamp,ActiveState,SubState'
        ]).strip().splitlines())
        if now != expected:
            raise RuntimeError('Shared host service identity changed: ' + unit)
    probe('https://gasontherun.online/')


def main(release):
    if socket.gethostname() != 'erebor.dedicated.co.za' or Path.cwd().resolve() != ROOT:
        raise RuntimeError('Wrong deployment target.')
    if not re.fullmatch(r'\d{8}-\d{6}', release):
        raise RuntimeError('Invalid release identifier.')
    stage = ROOT / 'releases' / release
    private = ROOT / '.local/production/releases' / release
    baseline = json.loads((private / 'before.json').read_text())
    previous = private / 'previous'
    old_school = SCHOOL.read_bytes()
    if hashlib.sha256(old_school).hexdigest() != baseline['knowledgevine_files'][str(SCHOOL)]:
        raise RuntimeError('School configuration changed since preflight.')
    preserve(baseline)
    text = old_school.decode()
    if text.count('proxy_set_header Host $host;') != 2:
        raise RuntimeError('Unexpected school proxy layout.')
    text = text.replace('http://localhost:8083', 'http://127.0.0.1:8083').replace('http://localhost:8082/', 'http://127.0.0.1:8082/')
    text = text.replace('proxy_set_header Host $host;', 'proxy_set_header Host $host;\n        proxy_set_header X-Forwarded-Proto $scheme;')
    compose = ['docker', 'compose', '-p', 'knowledgevine', '-f', str(ROOT / 'docker-compose.yml')]
    changed_school = False
    changed_compose = False
    started = datetime.now(timezone.utc)
    try:
        atomic(SCHOOL, text.encode(), SCHOOL.stat().st_mode & 0o777)
        changed_school = True
        command(['nginx', '-t'])
        command(['systemctl', 'reload', 'nginx'])
        preserve(baseline)
        probe('/home')
        print('School-only HTTPS forwarding validated and gracefully reloaded; GOTR baseline unchanged.', flush=True)
        changed_compose = True
        atomic(ROOT / '.env', ('KNOWLEDGEVINE_RELEASE=' + release + '\n').encode(), 0o600)
        atomic(ROOT / 'docker-compose.yml', (stage / 'docker-compose.yml').read_bytes(), 0o644)
        command(compose + ['up', '-d', '--no-deps', '--no-build', 'api'])
        headers, _ = wait_for('/api/portal/auth/csrf')
        if 'no-store' not in headers.get('Cache-Control', ''):
            raise RuntimeError('Private API no-store header missing.')
        if not any('__Host-KnowledgeVine.Antiforgery=' in value and 'secure' in value.lower()
                   for value in (headers.get_all('Set-Cookie') or [])):
            raise RuntimeError('Secure antiforgery cookie missing.')
        probe('/api/portal/auth/session', 401)
        command(compose + ['up', '-d', '--no-deps', '--no-build', 'web'])
        headers, data = wait_for('/manifest.webmanifest')
        manifest = json.loads(data)
        if manifest['start_url'] != '/portal/' or manifest['display'] != 'standalone':
            raise RuntimeError('Unexpected PWA manifest.')
        for url, file in [('/manifest.webmanifest', 'manifest.webmanifest'), ('/ngsw.json', 'ngsw.json'),
                          ('/ngsw-worker.js', 'ngsw-worker.js'), ('/index.html', 'index.html')]:
            headers, body = probe(url)
            if hashlib.sha256(body).digest() != hashlib.sha256((stage / 'web/browser' / file).read_bytes()).digest():
                raise RuntimeError('Published asset does not match release: ' + file)
        probe('/portal/login')
        probe('/guides/knowledge-vine-phone-guide.pdf', 404)
        preserve(baseline)
        result = {'release': release, 'deployed': True, 'started_utc': started.isoformat(),
                  'finished_utc': datetime.now(timezone.utc).isoformat(), 'gotr_preserved': True,
                  'postgres_host_service_preserved': True, 'public_https_verified': True}
        atomic(private / 'cutover.json', (json.dumps(result, indent=2) + '\n').encode(), 0o600)
        print(json.dumps(result))
    except Exception:
        # Original host-mounted assets/configuration were never overwritten.
        if changed_compose:
            atomic(ROOT / 'docker-compose.yml', (previous / 'docker-compose.yml').read_bytes(), 0o644)
            if (previous / 'root.env').exists():
                atomic(ROOT / '.env', (previous / 'root.env').read_bytes(), 0o600)
            else:
                (ROOT / '.env').unlink(missing_ok=True)
            command(compose + ['up', '-d', '--no-deps', '--no-build', 'api', 'web'])
        if changed_school:
            atomic(SCHOOL, old_school, SCHOOL.stat().st_mode & 0o777)
            command(['nginx', '-t'])
            command(['systemctl', 'reload', 'nginx'])
        raise


if __name__ == '__main__':
    if len(sys.argv) != 2:
        raise SystemExit('Specify the staged KnowledgeVine release identifier.')
    main(sys.argv[1])
