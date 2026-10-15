#!/usr/bin/env python3
"""Export reviewable EF migration SQL with a database guard before any schema writes."""
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "KnowledgeVineAPI/KnowledgeVineAPI"
OUTPUT = PROJECT / "Data/Migrations/portal-schema.sql"
GUARD = PROJECT / "Data/Sql/portal_target_v1.sql"

with tempfile.TemporaryDirectory(prefix="knowledgevine-schema-") as folder:
    generated = Path(folder) / "schema.sql"
    subprocess.run([
        "dotnet", "ef", "migrations", "script", "--idempotent",
        "--project", str(PROJECT), "--output", str(generated)], cwd=ROOT, check=True)
    OUTPUT.write_text(
        "-- Generated with scripts/export_portal_schema.py.\n"
        "-- Use psql -v ON_ERROR_STOP=1; only KnowledgeVine databases are permitted.\n"
        + GUARD.read_text() + "\n" + generated.read_text(encoding="utf-8-sig"), encoding="utf-8")
print(f"Schema SQL saved: {OUTPUT.relative_to(ROOT)}")
