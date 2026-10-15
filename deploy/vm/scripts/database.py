#!/usr/bin/env python3
"""Provision only the KnowledgeVine PostgreSQL container on ubuntu24testenv."""
import argparse
import json
from pathlib import Path
import secrets
import socket
import subprocess

ROOT = Path('/home/anrich/KnowledgeVine')
PRIVATE = ROOT / '.local/vm'
CONFIG = PRIVATE / 'database.json'
DATABASE = 'knowledgevine_local'
OWNER = 'knowledgevine_migrator'
APP = 'knowledgevine_app'


def check_vm():
    if socket.gethostname() != 'ubuntu24testenv' or not (ROOT / 'docker-compose.yml').exists():
        raise RuntimeError('This helper runs only in KnowledgeVine on the testing VM.')


def private_file(path, content):
    path.touch(mode=0o600, exist_ok=True)
    path.chmod(0o600)
    path.write_text(content)


def configure():
    PRIVATE.mkdir(parents=True, exist_ok=True)
    (ROOT / '.local').chmod(0o700)
    PRIVATE.chmod(0o700)
    if CONFIG.exists():
        config = load()
    else:
        config = {'database': DATABASE, 'app_user': APP, 'migration_user': OWNER,
                  'app_password': secrets.token_hex(32), 'migration_password': secrets.token_hex(32),
                  'postgres_password': secrets.token_hex(32)}
        private_file(CONFIG, json.dumps(config, indent=2) + '\n')
    private_file(PRIVATE / 'postgres.env', 'POSTGRES_DB=postgres\nPOSTGRES_USER=postgres\nPOSTGRES_PASSWORD=' + config['postgres_password'] + '\n')
    connection = ('Host=db;Port=5432;Database=' + DATABASE + ';Username=' + APP
                  + ';Password=' + config['app_password'] + ';Application Name=KnowledgeVinePortal;GSS Encryption Mode=Disable')
    private_file(PRIVATE / 'api.env', 'ConnectionStrings__KnowledgeVine=' + connection + '\n')
    (PRIVATE / 'portal').mkdir(exist_ok=True)
    print('Protected VM credentials prepared; existing credentials preserved.')


def load():
    config = json.loads(CONFIG.read_text())
    if (config['database'], config['app_user'], config['migration_user']) != (DATABASE, APP, OWNER):
        raise RuntimeError('Unexpected KnowledgeVine VM credentials.')
    for key in ['app_password', 'migration_password', 'postgres_password']:
        if len(config[key]) != 64 or any(c not in '0123456789abcdef' for c in config[key]):
            raise RuntimeError('Unexpected generated credential format.')
    return config


def check_container():
    result = subprocess.run(['docker', 'inspect', '--format',
                             '{{index .Config.Labels "com.docker.compose.project"}}/{{index .Config.Labels "com.docker.compose.service"}}',
                             'knowledge-db'], capture_output=True, text=True, check=True)
    if result.stdout.strip() != 'knowledgevine/db':
        raise RuntimeError('Expected the KnowledgeVine-only database container.')


def sql(user, database, statement):
    if database not in {'postgres', DATABASE} or user not in {'postgres', OWNER, APP}:
        raise RuntimeError('KnowledgeVine database target required.')
    result = subprocess.run(['docker', 'compose', 'exec', '-T', 'db', 'psql', '-X', '-q',
                             '-v', 'ON_ERROR_STOP=1', '-U', user, '-d', database, '-At'],
                            input=statement, text=True, capture_output=True, cwd=ROOT)
    if result.returncode:
        message = result.stderr
        for key in ['app_password', 'migration_password', 'postgres_password']:
            message = message.replace(load()[key], '[redacted]')
        raise RuntimeError(message.strip())
    return result.stdout.strip()


def provision():
    check_container()
    config = load()
    owner = sql('postgres', 'postgres', "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname='knowledgevine_local';")
    if owner and owner != OWNER:
        raise RuntimeError('Existing KnowledgeVine database has an unexpected owner; stopped.')
    for role, key in [(OWNER, 'migration_password'), (APP, 'app_password')]:
        exists = sql('postgres', 'postgres', f"SELECT rolname FROM pg_roles WHERE rolname='{role}';")
        if not exists:
            sql('postgres', 'postgres', f"CREATE ROLE {role} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS PASSWORD '{config[key]}';")
    if not owner:
        sql('postgres', 'postgres', f"CREATE DATABASE {DATABASE} OWNER {OWNER} TEMPLATE template0 ENCODING 'UTF8';")
    sql('postgres', 'postgres', f"""
REVOKE ALL ON DATABASE {DATABASE} FROM PUBLIC;
GRANT CONNECT ON DATABASE {DATABASE} TO {APP};
ALTER ROLE {APP} IN DATABASE {DATABASE} SET search_path=public;
ALTER ROLE {APP} IN DATABASE {DATABASE} SET timezone='UTC';
ALTER ROLE {OWNER} IN DATABASE {DATABASE} SET timezone='UTC';
""")
    sql(OWNER, DATABASE, f'REVOKE ALL ON SCHEMA public FROM PUBLIC; GRANT USAGE ON SCHEMA public TO {APP};')
    sql(OWNER, DATABASE, (PRIVATE / 'portal-schema.sql').read_text())
    sql(OWNER, DATABASE, f"""
REVOKE ALL ON ALL TABLES IN SCHEMA public FROM PUBLIC;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM PUBLIC;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA public FROM PUBLIC;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {APP};
REVOKE ALL ON TABLE public."__EFMigrationsHistory" FROM {APP};
GRANT SELECT ON TABLE public."__EFMigrationsHistory" TO {APP};
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {APP};
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA public TO {APP};
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {APP};
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO {APP};
ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT EXECUTE ON FUNCTIONS TO {APP};
""")
    print('KnowledgeVine VM schema and restricted runtime permissions are ready.')


def status():
    check_container()
    print(sql(APP, DATABASE, """
SELECT 'database=' || current_database() || '; runtime=' || current_user;
SELECT 'postgres=' || current_setting('server_version');
SELECT 'tables=' || count(*) FROM information_schema.tables WHERE table_schema='public';
SELECT 'functions=' || count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public';
SELECT 'triggers=' || count(*) FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND NOT t.tgisinternal;
SELECT 'roles=' || string_agg(name, ', ' ORDER BY name) FROM roles;
SELECT 'users=' || count(*) FROM users;
SELECT 'students=' || count(*) FROM students;
SELECT 'app_superuser=' || rolsuper || '; app_createdb=' || rolcreatedb || '; app_createrole=' || rolcreaterole
FROM pg_roles WHERE rolname=current_user;
SELECT 'admin=' || u.email || '; ready=' || portal_user_is_ready(u.id) FROM users u
JOIN user_roles ur ON ur.user_id=u.id JOIN roles r ON r.id=ur.role_id WHERE r.name='Admin';
"""))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['configure', 'provision', 'status'])
    action = parser.parse_args().action
    check_vm()
    {'configure': configure, 'provision': provision, 'status': status}[action]()
