#!/usr/bin/env python3
"""Manual KnowledgeVine-only backups and isolated restore checks; never host PostgreSQL."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tarfile
import time

import database as db


def run(args, **kwargs):
    result = subprocess.run(args, capture_output=True, **kwargs)
    if result.returncode:
        # Never echo connection/configuration-bearing exception text.
        raise RuntimeError('KnowledgeVine maintenance command failed: ' + args[0])
    return result


def backup():
    db.check_host(); db.check_container()
    folder = db.PRIVATE / 'backups' / datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S')
    folder.mkdir(parents=True, exist_ok=False); folder.chmod(0o700)
    dump = folder / 'knowledgevine.dump'
    fd = os.open(dump, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, 'wb') as stream:
        result = subprocess.run(['docker', 'exec', 'knowledge-db', 'pg_dump', '-Fc',
                                 '-U', db.OWNER, '-d', db.DATABASE], stdout=stream, stderr=subprocess.PIPE)
    if result.returncode:
        raise RuntimeError('KnowledgeVine database backup failed.')
    secret_archive = folder / 'keys-and-config.tar.gz'
    fd = os.open(secret_archive, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, 'wb') as stream, tarfile.open(fileobj=stream, mode='w:gz') as archive:
        for name in ['database.json', 'api.env', 'postgres.env', 'portal/keys']:
            archive.add(db.PRIVATE / name, arcname=name)
        environment = db.ROOT / '.env'
        if not environment.exists():
            environment = db.PRIVATE / 'release.env'
        release = next(line.split('=', 1)[1] for line in environment.read_text().splitlines()
                       if line.startswith('KNOWLEDGEVINE_RELEASE='))
        if not re.fullmatch(r'\d{8}-\d{6}', release):
            raise RuntimeError('Unexpected KnowledgeVine release identifier.')
        archive.add(db.ROOT / 'releases' / release / 'docker-compose.yml', arcname='deployment/docker-compose.yml')
        archive.add(environment, arcname='deployment/release.env')
        archive.add('/etc/nginx/sites-available/knowledgevinemontessori.co.za', arcname='deployment/school-nginx.conf')
    manifest = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in [dump, secret_archive]}
    db.private_file(folder / 'sha256.json', json.dumps(manifest, indent=2) + '\n')
    print(json.dumps({'backup_directory': str(folder), 'sha256': manifest}))


def restore_check(folder):
    db.check_host(); db.check_container()
    folder = Path(folder).resolve()
    if not folder.is_relative_to((db.PRIVATE / 'backups').resolve()):
        raise RuntimeError('Restore verification requires a KnowledgeVine backup directory.')
    manifest = json.loads((folder / 'sha256.json').read_text())
    for name in ['knowledgevine.dump', 'keys-and-config.tar.gz']:
        if hashlib.sha256((folder / name).read_bytes()).hexdigest() != manifest[name]:
            raise RuntimeError('Backup fingerprint mismatch.')
    name = 'knowledgevine-restorecheck-' + datetime.now(timezone.utc).strftime('%Y%m%d%H%M%S')
    # No published ports or shared network. Its temporary anonymous volume is removed afterwards.
    run(['docker', 'run', '-d', '--name', name, '--label', 'knowledgevine.restorecheck=true',
         '--network', 'none', '--env-file', str(db.PRIVATE / 'postgres.env'), 'postgres:18.6'])
    try:
        for _ in range(45):
            result = subprocess.run(['docker', 'exec', name, 'pg_isready', '-h', '127.0.0.1', '-U', 'postgres'], capture_output=True)
            if result.returncode == 0:
                break
            time.sleep(1)
        else:
            raise RuntimeError('Isolated restore database did not become ready.')
        setup = f'CREATE ROLE {db.OWNER} LOGIN; CREATE ROLE {db.APP} LOGIN; CREATE DATABASE {db.DATABASE} OWNER {db.OWNER};'
        run(['docker', 'exec', '-i', name, 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-U', 'postgres', '-d', 'postgres'], input=setup.encode())
        with (folder / 'knowledgevine.dump').open('rb') as stream:
            run(['docker', 'exec', '-i', name, 'pg_restore', '--exit-on-error', '--no-owner',
                 '-U', db.OWNER, '-d', db.DATABASE], stdin=stream)
        query = """
SELECT 'migrations=' || count(*) FROM "__EFMigrationsHistory";
SELECT 'users=' || count(*) FROM users;
SELECT 'students=' || count(*) FROM students;
SELECT 'notes=' || count(*) FROM daily_updates;
SELECT 'ready_admins=' || count(*) FROM users u JOIN user_roles ur ON u.id=ur.user_id
JOIN roles r ON r.id=ur.role_id WHERE r.name='Admin' AND portal_user_is_ready(u.id);
"""
        args = ['psql', '-X', '-At', '-v', 'ON_ERROR_STOP=1', '-U', db.OWNER, '-d', db.DATABASE]
        live = run(['docker', 'exec', '-i', 'knowledge-db', *args], input=query.encode()).stdout
        restored = run(['docker', 'exec', '-i', name, *args], input=query.encode()).stdout
        if live != restored:
            raise RuntimeError('Restored account/schema counts do not match the live KnowledgeVine database.')
        # Verify that the archived keys/config can also be recovered, without extracting secrets.
        with tarfile.open(folder / 'keys-and-config.tar.gz') as archive:
            for file in archive:
                if not file.isfile() or file.name.startswith('deployment/'):
                    continue
                source = (db.PRIVATE / file.name).resolve()
                if not source.is_relative_to(db.PRIVATE.resolve()):
                    raise RuntimeError('Unsafe backup member.')
                if hashlib.sha256(archive.extractfile(file).read()).digest() != hashlib.sha256(source.read_bytes()).digest():
                    raise RuntimeError('Recovered key/configuration fingerprint mismatch.')
        result = {'restore_verified': True, 'counts': restored.decode().splitlines(),
                  'keys_and_configuration_verified': True, 'shared_host_postgres_used': False}
        db.private_file(folder / 'restore-verification.json', json.dumps(result, indent=2) + '\n')
        print(json.dumps(result))
    finally:
        info = json.loads(run(['docker', 'inspect', name]).stdout)[0]
        if info['Config']['Labels'].get('knowledgevine.restorecheck') == 'true':
            run(['docker', 'rm', '-fv', name])


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['backup', 'restore-check'])
    parser.add_argument('folder', nargs='?')
    args = parser.parse_args()
    if args.action == 'backup':
        backup()
    elif args.folder:
        restore_check(args.folder)
    else:
        parser.error('restore-check needs the protected backup directory')
