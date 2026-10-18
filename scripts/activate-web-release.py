#!/usr/bin/env python3
"""Host-side, restartable release activation. Never restores a database backup.

Run under a transient systemd unit, with a root-owned state file prepared after
the ledger round-trip rehearsal. `recover STATE` is also safe to run manually.
"""
import argparse
from contextlib import contextmanager
import fcntl
import hashlib
import json
import os
import pwd
from pathlib import Path
import signal
import sqlite3
import subprocess
import time
import urllib.request


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def atomic_json(path, value):
    path = Path(path)
    temporary = path.with_suffix(path.suffix + ".new")
    with temporary.open("w") as stream:
        json.dump(value, stream, indent=2)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)
    descriptor = os.open(path.parent, os.O_RDONLY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


class Deployment:
    def __init__(self, state_path):
        self.state_path = Path(state_path)
        self.state = json.loads(self.state_path.read_text())
        self.paths = {key: Path(value) for key, value in self.state["paths"].items()}

    def save(self, **values):
        self.state.update(values)
        atomic_json(self.state_path, self.state)

    def phase(self, name, action):
        self.save(phase=name, phase_complete=False)
        action()
        self.save(phase_complete=True)

    def service(self, verb):
        subprocess.run(["systemctl", verb, "ftitc-web.service"], check=True)

    @contextmanager
    def ledger(self):
        # Root manages release directories, but SQLite must create WAL/SHM files
        # with the web service identity, including on recovery after a failed start.
        original_uid, original_gid = os.geteuid(), os.getegid()
        connection = None
        try:
            if original_uid == 0:
                identity = pwd.getpwnam(self.state.get("service_user", "ftitc-web"))
                os.setegid(identity.pw_gid)
                os.seteuid(identity.pw_uid)
            connection = sqlite3.connect(self.paths["ledger"], timeout=30)
            with connection:
                yield connection
        finally:
            if connection is not None:
                connection.close()
            if original_uid == 0:
                os.seteuid(original_uid)
                os.setegid(original_gid)

    def check_compatibility(self, recovering=False):
        if not self.state.get("backups_complete"):
            raise RuntimeError("Consistent deployment backups are incomplete")
        if not recovering and "data_backup" in self.paths:
            backup = self.paths["data_backup"]
            for entry in json.loads((backup / "manifest.json").read_text()):
                target = backup / entry["path"].lstrip("/")
                if "sha256" in entry and digest(target) != entry["sha256"]:
                    raise RuntimeError("A deployment backup failed verification")
                for name, expected in entry.get("files", {}).items():
                    if digest(target / name) != expected:
                        raise RuntimeError("An executable/configuration backup failed verification")
        report = json.loads(self.paths["compatibility_report"].read_text())
        if report.get("round_trip_passed") is not True:
            raise RuntimeError("Ledger round-trip compatibility has not passed")
        release = self.paths["active"] if recovering else self.paths["candidate"]
        expected = report["recovery_sha256" if recovering else "candidate_sha256"]
        if digest(release / "AnalysisITC.Web.dll") != expected:
            raise RuntimeError("Executable differs from the compatibility rehearsal")
        for name, expected_hash in report.get("recovery_files" if recovering else "candidate_files", {}).items():
            if Path(name).name != name or digest(release / name) != expected_hash:
                raise RuntimeError("Dependency differs from the compatibility rehearsal: " + name)
        if not recovering:
            old = self.paths["active"] / "AnalysisITC.Web.dll"
            if digest(old) != report["previous_sha256"]:
                raise RuntimeError("Active executable changed since the rehearsal")
            for name, expected_hash in report.get("previous_files", {}).items():
                if Path(name).name != name or digest(self.paths["active"] / name) != expected_hash:
                    raise RuntimeError("Active dependency changed since the rehearsal: " + name)

    def pause(self):
        # Both policies must fence new work even if the active directory is absent.
        atomic_json(self.paths["availability"], {
            "Status": "paused", "Message": "Service maintenance is in progress.",
            "UpdatedAtUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())})
        with self.ledger() as connection:
            cursor = connection.execute("UPDATE maintenance_gate SET active=1,reason=?,changed_utc=? WHERE id=1",
                                        ("Release activation", time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())))
            if cursor.rowcount != 1:
                raise RuntimeError("Accounting maintenance gate is missing")

    def require_drained(self):
        with self.ledger() as connection:
            count = connection.execute("SELECT count(*) FROM executions WHERE lifecycle='admitted' AND completed_utc IS NULL").fetchone()[0]
        if count:
            raise RuntimeError(f"{count} executions remain admitted; generation stays paused")

    def health(self):
        failure = "service did not respond"
        for _ in range(30):
            try:
                with urllib.request.urlopen("http://127.0.0.1:5000/api/internal/health", timeout=4) as response:
                    report = json.load(response)
                if report.get("overall") == "healthy":
                    return
                failure = "; ".join(check["id"] + ": " + check.get("reason", "unhealthy")
                                    for group in report.get("groups", []) for check in group.get("checks", [])
                                    if check.get("state") in ("fail", "warning"))
            except (OSError, ValueError) as error:
                failure = type(error).__name__
            time.sleep(1)
        raise RuntimeError("Service health failed: " + failure)

    def restore_availability(self):
        previous = self.state["previous"]
        # Only a successfully checked executable may reopen either gate.
        with self.ledger() as connection:
            connection.execute("UPDATE maintenance_gate SET active=?,reason=?,changed_utc=? WHERE id=1",
                               (previous["maintenance"]["active"], previous["maintenance"]["reason"], previous["maintenance"]["changed_utc"]))
        path = self.paths["availability"]
        if previous["availability"] is None:
            path.unlink(missing_ok=True)
        else:
            atomic_json(path, previous["availability"])

    def migrate(self):
        subprocess.run(["runuser", "-u", self.state.get("service_user", "ftitc-web"), "--", "dotnet", "AnalysisITC.Web.dll", "usage-log", "status"],
                       cwd=self.paths["active"], check=True)

    def move(self, source, destination):
        if destination.exists():
            raise RuntimeError("Release destination already exists: " + str(destination))
        source.rename(destination)

    def restore_directory(self, active_key, backup_key, failed_key):
        active, backup, failed = (self.paths[key] for key in (active_key, backup_key, failed_key))
        if backup.exists():
            if active.exists():
                if failed.exists():
                    raise RuntimeError("Both active and failed release directories exist")
                self.move(active, failed)
            self.move(backup, active)
        if not active.exists() and not (active_key == "active" and self.paths.get("recovery", Path("/nonexistent")).exists()):
            raise RuntimeError("No recoverable release directory: " + str(active))

    def install_recovery(self):
        recovery = self.paths.get("recovery")
        if recovery and recovery.exists():
            if self.paths["active"].exists():
                self.move(self.paths["active"], self.paths["incompatible"])
            self.move(recovery, self.paths["active"])

    def activate(self):
        if self.state.get("status") == "complete":
            return
        if self.state.get("status") in ("activating", "recovering", "recovery_failed"):
            self.recover()
            raise RuntimeError("Interrupted activation was recovered; prepare a new rollout")
        self.check_compatibility()
        self.save(status="activating")
        try:
            self.phase("pause", self.pause)
            self.phase("drain", self.require_drained)
            self.phase("stop", lambda: self.service("stop"))
            self.phase("backup_release", lambda: self.move(self.paths["active"], self.paths["backup"]))
            self.phase("install_release", lambda: self.move(self.paths["candidate"], self.paths["active"]))
            self.phase("migrate", self.migrate)
            self.phase("start", lambda: self.service("start"))
            self.phase("health", self.health)
            self.phase("backup_status_email", lambda: self.move(self.paths["status_active"], self.paths["status_backup"]))
            self.phase("install_status_email", lambda: self.move(self.paths["status_candidate"], self.paths["status_active"]))
            self.phase("restore_availability", self.restore_availability)
            self.phase("final_health", self.health)
            self.save(status="complete")
        except BaseException as error:
            self.save(activation_error=f"{type(error).__name__}: {error}")
            self.recover()
            raise

    def recover(self):
        if self.state.get("status") == "recovered":
            return
        self.save(status="recovering")
        try:
            self.phase("recovery_pause", self.pause)
            self.phase("recovery_stop", lambda: self.service("stop"))
            self.phase("recovery_release", lambda: self.restore_directory("active", "backup", "failed"))
            # A prepared compatibility build may replace an incompatible old executable.
            self.phase("recovery_install", self.install_recovery)
            self.phase("recovery_status_email", lambda: self.restore_directory("status_active", "status_backup", "status_failed"))
            self.phase("recovery_compatibility", lambda: self.check_compatibility(recovering=True))
            self.phase("recovery_start", lambda: self.service("start"))
            self.phase("recovery_health", self.health)
            self.phase("recovery_availability", self.restore_availability)
            self.phase("recovery_final_health", self.health)
            self.save(status="recovered")
        except BaseException as error:
            try:
                self.pause()
            except BaseException as pause_error:
                self.save(pause_error=f"{type(pause_error).__name__}: {pause_error}")
            self.save(status="recovery_failed", recovery_error=f"{type(error).__name__}: {error}")
            raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("activate", "recover"))
    parser.add_argument("state", type=Path)
    args = parser.parse_args()
    # A stable lock serializes manual recovery with the detached activation unit.
    with (args.state.parent / "activation.lock").open("a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        signal.signal(signal.SIGTERM, lambda *_: (_ for _ in ()).throw(InterruptedError("Activation terminated")))
        deployment = Deployment(args.state)
        getattr(deployment, args.operation)()


if __name__ == "__main__":
    main()
