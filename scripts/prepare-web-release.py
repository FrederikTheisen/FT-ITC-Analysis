#!/usr/bin/env python3
"""Prepare a protected rollout state after an independently verified rehearsal.

This command snapshots availability before any pause. It does not activate a
release. Run `backup STATE` after pausing and draining generation; that command
stops the service while taking consistent backups and always attempts to restart
it with generation still paused. Rehearse on copies of these backups, then supply
the resulting compatibility report before starting the activation unit.
"""
import argparse
from contextlib import closing
import importlib.util
import json
import os
from pathlib import Path
import shutil
import sqlite3
import subprocess

spec = importlib.util.spec_from_file_location("activation", Path(__file__).with_name("activate-web-release.py"))
activation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(activation)


def prepare(specification, state_path):
    state_path = Path(state_path)
    if state_path.exists():
        raise RuntimeError("A saved deployment state must never be overwritten")
    configuration = json.loads(Path(specification).read_text())
    paths = {key: Path(value) for key, value in configuration["paths"].items()}
    required = ("active", "candidate", "backup", "failed", "status_active", "status_candidate", "status_backup", "status_failed", "availability", "ledger", "compatibility_report", "data_backup")
    if any(key not in paths or not paths[key].is_absolute() for key in required):
        raise RuntimeError("All required deployment paths must be absolute")
    if len(set(paths.values())) != len(paths):
        raise RuntimeError("Deployment paths must be distinct")
    if any(path != path.resolve() for path in paths.values()):
        raise RuntimeError("Deployment paths must be canonical and cannot be symlink aliases")
    for filename in configuration["backup_files"]:
        source = Path(filename)
        if not source.is_absolute() or source != source.resolve():
            raise RuntimeError("Backup source paths must be canonical absolute paths")
        if paths["data_backup"] == source or source in paths["data_backup"].parents:
            raise RuntimeError("The backup directory cannot be inside a backup source")
    if any(paths[key].exists() for key in ("backup", "failed", "status_backup", "status_failed", "data_backup")):
        raise RuntimeError("Backup and failed-release paths must be new")
    availability = json.loads(paths["availability"].read_text()) if paths["availability"].exists() else None
    if availability is not None and availability.get("Status") not in ("active", "paused", "retired"):
        raise RuntimeError("The existing availability policy is invalid")
    with sqlite3.connect(f"file:{paths['ledger']}?mode=ro", uri=True) as db:
        db.row_factory = sqlite3.Row
        maintenance = dict(db.execute("SELECT active,reason,changed_utc FROM maintenance_gate WHERE id=1").fetchone())
    configuration.update(status="prepared", backups_complete=False,
                         previous=dict(availability=availability, maintenance=maintenance))
    state_path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    activation.atomic_json(state_path, configuration)
    os.chmod(state_path, 0o600)


def backup(state_path):
    deployment = activation.Deployment(state_path)
    if deployment.state.get("backups_complete"):
        raise RuntimeError("Existing rollback evidence must not be replaced")
    deployment.phase("preflight_pause", deployment.pause)
    deployment.phase("preflight_drain", deployment.require_drained)
    destination = deployment.paths["data_backup"]
    destination.mkdir(mode=0o700)
    deployment.save(phase="preflight_stop", phase_complete=False)
    try:
        deployment.service("stop")
        deployment.save(phase_complete=True)
        manifest = []
        for filename in deployment.state["backup_files"]:
            source = Path(filename)
            target = destination / str(source).lstrip("/")
            target.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
            entry = {"path": str(source), "exists": source.exists()}
            if source.is_dir():
                shutil.copytree(source, target)
                entry["files"] = {str(item.relative_to(target)): activation.digest(item)
                                  for item in target.rglob("*") if item.is_file()}
            elif source.exists():
                if source.suffix == ".db":
                    with closing(sqlite3.connect(f"file:{source}?mode=ro", uri=True)) as original:
                        with closing(sqlite3.connect(target)) as copy:
                            original.backup(copy)
                    os.chmod(target, 0o600)
                else:
                    shutil.copy2(source, target)
                entry["sha256"] = activation.digest(target)
            manifest.append(entry)
        activation.atomic_json(destination / "manifest.json", manifest)
        # The previous policy is in state, since the backed-up live policy is paused.
        deployment.save(backups_complete=True, phase="preflight_backups", phase_complete=True)
    finally:
        deployment.service("start")


def companion(state_path):
    deployment = activation.Deployment(state_path)
    target = deployment.paths["status_candidate"]
    if target.exists():
        raise RuntimeError("Companion staging directory already exists")
    shutil.copytree(deployment.paths["status_active"], target)
    extensions = (".dll", ".pdb", ".deps.json", ".runtimeconfig.json", ".so", ".config")
    for source in deployment.paths["candidate"].iterdir():
        if source.is_file() and source.name.endswith(extensions):
            shutil.copy2(source, target / source.name)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("prepare", "backup", "companion"))
    parser.add_argument("state")
    parser.add_argument("--specification")
    args = parser.parse_args()
    if args.operation == "prepare":
        if not args.specification:
            parser.error("prepare requires --specification")
        prepare(args.specification, args.state)
    elif args.operation == "backup":
        backup(args.state)
    else:
        companion(args.state)


if __name__ == "__main__":
    main()
