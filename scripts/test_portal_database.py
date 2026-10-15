#!/usr/bin/env python3
"""Run rollback-only integration checks against the local KnowledgeVine runtime connection."""
import os
import subprocess
from portal_database import APP, OWNER, ROOT, connection_string, load_config

config = load_config()
env = dict(os.environ,
           KNOWLEDGEVINE_PROJECT_ROOT=str(ROOT),
           KNOWLEDGEVINE_TEST_CONNECTION=connection_string(APP, config["app_password"]),
           KNOWLEDGEVINE_BOOTSTRAP_TEST_CONNECTION=connection_string(OWNER, config["migration_password"]))
result = subprocess.run([
    "dotnet", "test", "KnowledgeVineAPI/KnowledgeVineAPI.sln",
    "--artifacts-path", "/tmp/knowledge-vine-portal-build", "--verbosity", "minimal"],
    cwd=ROOT, env=env, check=False)
raise SystemExit(result.returncode)
