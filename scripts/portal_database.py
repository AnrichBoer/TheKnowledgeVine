#!/usr/bin/env python3
"""Create/manage only the local KnowledgeVine database. Never enumerate other databases."""
import argparse
import getpass
import json
import os
from pathlib import Path
import secrets
import subprocess

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "KnowledgeVineAPI/KnowledgeVineAPI/KnowledgeVineAPI.csproj"
LOCAL = ROOT / ".local/portal"
CONFIG = LOCAL / "database.json"
DATABASE = "knowledgevine_local"
HOST = "127.0.0.1"
PORT = "5432"
OWNER = "knowledgevine_migrator"
APP = "knowledgevine_app"


def sql_literal(value):
    return "'" + value.replace("'", "''") + "'"


def run_sql(user, password, database, sql):
    if database not in {"postgres", DATABASE}:
        raise RuntimeError("Only postgres maintenance and knowledgevine_local are permitted.")
    env = dict(os.environ, PGPASSWORD=password, PGCONNECT_TIMEOUT="5")
    result = subprocess.run(
        ["psql", "-X", "-w", "-h", HOST, "-p", PORT, "-U", user,
         "-d", database, "-v", "ON_ERROR_STOP=1", "-At"],
        input=sql, text=True, capture_output=True, env=env, check=False)
    if result.returncode:
        message = result.stderr.replace(password, "[redacted]")
        if CONFIG.exists():
            for name in ["app_password", "migration_password"]:
                message = message.replace(json.loads(CONFIG.read_text())[name], "[redacted]")
        raise RuntimeError(message.strip())
    return result.stdout.strip()


def load_config():
    config = json.loads(CONFIG.read_text())
    if (config.get("database"), config.get("host"), str(config.get("port"))) != (DATABASE, HOST, PORT):
        raise RuntimeError("Local configuration must target knowledgevine_local on 127.0.0.1:5432.")
    if config.get("app_user") != APP or config.get("migration_user") != OWNER:
        raise RuntimeError("Unexpected local database roles.")
    return config


def connection_string(user, password):
    # Generated passwords are hex strings, so they contain no connection-string separators.
    return f"Host={HOST};Port={PORT};Database={DATABASE};Username={user};Password={password};Application Name=KnowledgeVinePortal"


def configure_secrets(config):
    payload = {
        "ConnectionStrings:KnowledgeVine": connection_string(APP, config["app_password"]),
        "ConnectionStrings:KnowledgeVineMigrations": connection_string(OWNER, config["migration_password"]),
        "Portal:DataProtectionKeyPath": str(LOCAL / "keys"),
    }
    result = subprocess.run(["dotnet", "user-secrets", "set", "--project", str(PROJECT)],
                            input=json.dumps(payload), text=True, capture_output=True, check=False)
    if result.returncode:
        raise RuntimeError("Could not save KnowledgeVine development user secrets.")
    secret_path = Path.home() / ".microsoft/usersecrets/knowledge-vine-portal-local/secrets.json"
    if secret_path.exists():
        secret_path.parent.chmod(0o700)
        secret_path.chmod(0o600)


def create(admin_user):
    password = getpass.getpass("Local PostgreSQL administrator password: ")
    identity = run_sql(admin_user, password, "postgres", "SELECT current_database();")
    if identity != "postgres":
        raise RuntimeError("Expected the postgres maintenance database.")
    existing_owner = run_sql(admin_user, password, "postgres",
                            "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = 'knowledgevine_local';")
    if existing_owner and existing_owner != OWNER:
        raise RuntimeError("knowledgevine_local already exists with an unexpected owner; nothing was changed.")

    LOCAL.mkdir(parents=True, exist_ok=True)
    LOCAL.chmod(0o700)
    (LOCAL / "keys").mkdir(exist_ok=True)
    (LOCAL / "keys").chmod(0o700)
    had_config = CONFIG.exists()
    config = load_config() if had_config else {
        "database": DATABASE, "host": HOST, "port": int(PORT),
        "app_user": APP, "app_password": secrets.token_hex(32),
        "migration_user": OWNER, "migration_password": secrets.token_hex(32),
    }
    for role in [OWNER, APP]:
        exists = run_sql(admin_user, password, "postgres",
                         f"SELECT rolname FROM pg_roles WHERE rolname = {sql_literal(role)};")
        if exists and not had_config:
            raise RuntimeError("A KnowledgeVine role exists but its local credential file is missing; no role was modified.")

    CONFIG.write_text(json.dumps(config, indent=2) + "\n")
    CONFIG.chmod(0o600)
    for role, key in [(OWNER, "migration_password"), (APP, "app_password")]:
        exists = run_sql(admin_user, password, "postgres",
                         f"SELECT rolname FROM pg_roles WHERE rolname = {sql_literal(role)};")
        if not exists:
            run_sql(admin_user, password, "postgres",
                    f"CREATE ROLE {role} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS PASSWORD {sql_literal(config[key])};")

    if not existing_owner:
        run_sql(admin_user, password, "postgres",
                f"CREATE DATABASE {DATABASE} OWNER {OWNER} TEMPLATE template0 ENCODING 'UTF8';")
    run_sql(admin_user, password, "postgres", f"""
REVOKE ALL ON DATABASE {DATABASE} FROM PUBLIC;
GRANT CONNECT ON DATABASE {DATABASE} TO {APP};
ALTER ROLE {APP} IN DATABASE {DATABASE} SET search_path = public;
ALTER ROLE {APP} IN DATABASE {DATABASE} SET timezone = 'UTC';
ALTER ROLE {OWNER} IN DATABASE {DATABASE} SET timezone = 'UTC';
""")
    run_sql(OWNER, config["migration_password"], DATABASE, f"""
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO {APP};
""")
    configure_secrets(config)
    print(f"Local database ready: {DATABASE}")
    print(f"Runtime role: {APP}; migration owner: {OWNER}")
    print("Development connection strings saved in .NET user secrets; credentials were not printed.")


def grant_runtime():
    config = load_config()
    run_sql(OWNER, config["migration_password"], DATABASE, f"""
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
    print("Runtime permissions applied only inside knowledgevine_local.")


def status():
    config = load_config()
    output = run_sql(APP, config["app_password"], DATABASE, """
SELECT 'database=' || current_database() || '; role=' || current_user;
SELECT 'table=' || table_name FROM information_schema.tables
WHERE table_schema = 'public' ORDER BY table_name;
SELECT 'function=' || p.proname FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
WHERE n.nspname = 'public' ORDER BY p.proname;
SELECT 'roles=' || string_agg(name, ', ' ORDER BY name) FROM public.roles;
SELECT 'users=' || count(*) FROM public.users;
SELECT 'students=' || count(*) FROM public.students;
""")
    print(output)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["create", "grant-runtime", "status"])
    parser.add_argument("--admin-user", default="postgres")
    args = parser.parse_args()
    if args.action == "create":
        create(args.admin_user)
    elif args.action == "grant-runtime":
        grant_runtime()
    else:
        status()


if __name__ == "__main__":
    main()
