import importlib.util
import json
from pathlib import Path
import sqlite3
import tempfile
import unittest
from unittest.mock import patch

MODULE_PATH = Path(__file__).resolve().parents[1] / "activate-web-release.py"
SPEC = importlib.util.spec_from_file_location("activation", MODULE_PATH)
activation = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(activation)
PREPARE_SPEC = importlib.util.spec_from_file_location("preparation", MODULE_PATH.with_name("prepare-web-release.py"))
preparation = importlib.util.module_from_spec(PREPARE_SPEC)
PREPARE_SPEC.loader.exec_module(preparation)


class FakeDeployment(activation.Deployment):
    def __init__(self, state, faults=()):
        super().__init__(state)
        self.faults = set(faults)
        self.services = []

    def phase(self, name, action):
        if name in self.faults:
            self.faults.remove(name)
            raise RuntimeError("injected " + name)
        super().phase(name, action)
        if name + ":after" in self.faults:
            self.faults.remove(name + ":after")
            raise RuntimeError("injected after " + name)

    def service(self, verb):
        self.services.append(verb)

    def health(self):
        self.assert_release_exists()

    def assert_release_exists(self):
        if not (self.paths["active"] / "AnalysisITC.Web.dll").exists():
            raise RuntimeError("missing active executable")

    def migrate(self):
        with sqlite3.connect(self.paths["ledger"]) as db:
            db.execute("INSERT OR IGNORE INTO charges VALUES ('during-activation', 9)")


class ActivationTests(unittest.TestCase):
    def fixture(self, previous_status="active"):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name).resolve()
        names = ["active", "candidate", "backup", "failed", "status_active", "status_candidate", "status_backup", "status_failed"]
        paths = {key: root / key for key in names}
        for key in ("active", "candidate", "status_active", "status_candidate"):
            paths[key].mkdir()
            (paths[key] / "AnalysisITC.Web.dll").write_text("new" if "candidate" in key else "old")
        paths.update(availability=root / "availability.json", ledger=root / "ledger.db", compatibility_report=root / "compatibility.json")
        previous = {"Status": previous_status, "Message": "original", "UpdatedAtUtc": "2026-10-17T10:00:00Z"}
        activation.atomic_json(paths["availability"], previous)
        with sqlite3.connect(paths["ledger"]) as db:
            db.executescript("CREATE TABLE maintenance_gate(id INTEGER,active INTEGER,reason TEXT,changed_utc TEXT); INSERT INTO maintenance_gate VALUES(1,0,NULL,'old'); CREATE TABLE executions(lifecycle TEXT,completed_utc TEXT); CREATE TABLE charges(id TEXT PRIMARY KEY,cost INTEGER); INSERT INTO charges VALUES('original',3);")
        old = activation.digest(paths["active"] / "AnalysisITC.Web.dll")
        new = activation.digest(paths["candidate"] / "AnalysisITC.Web.dll")
        activation.atomic_json(paths["compatibility_report"], dict(round_trip_passed=True, previous_sha256=old, recovery_sha256=old, candidate_sha256=new))
        state = root / "state.json"
        activation.atomic_json(state, dict(status="prepared", backups_complete=True, paths={key:str(value) for key,value in paths.items()}, previous=dict(availability=previous, maintenance=dict(active=0,reason=None,changed_utc="old"))))
        return state, paths, previous

    def assert_restored(self, state, paths, previous):
        self.assertEqual(json.loads(state.read_text())["status"], "recovered")
        self.assertEqual((paths["active"] / "AnalysisITC.Web.dll").read_text(), "old")
        self.assertEqual((paths["status_active"] / "AnalysisITC.Web.dll").read_text(), "old")
        self.assertEqual(json.loads(paths["availability"].read_text()), previous)
        with sqlite3.connect(paths["ledger"]) as db:
            self.assertEqual(db.execute("SELECT active FROM maintenance_gate").fetchone()[0], 0)
            self.assertEqual(db.execute("SELECT cost FROM charges WHERE id='original'").fetchone()[0], 3)

    def test_success_restores_original_availability(self):
        for status in ("active", "paused", "retired"):
            with self.subTest(status=status):
                state, paths, previous = self.fixture(status)
                FakeDeployment(state).activate()
                self.assertEqual(json.loads(state.read_text())["status"], "complete")
                self.assertEqual(json.loads(paths["availability"].read_text()), previous)
                self.assertEqual((paths["active"] / "AnalysisITC.Web.dll").read_text(), "new")

    def test_every_activation_phase_recovers_before_and_after(self):
        phases = ("pause", "drain", "stop", "backup_release", "install_release", "migrate", "start", "health", "backup_status_email", "install_status_email", "restore_availability", "final_health")
        for phase in phases:
            for suffix in ("", ":after"):
                with self.subTest(phase=phase+suffix):
                    state, paths, previous = self.fixture()
                    with self.assertRaises(RuntimeError):
                        FakeDeployment(state, [phase+suffix]).activate()
                    self.assert_restored(state, paths, previous)
                    # Repeating recovery is harmless and never restores old accounting.
                    FakeDeployment(state).recover()
                    self.assert_restored(state, paths, previous)
                    if phase in phases[6:] or phase == "migrate" and suffix:
                        with sqlite3.connect(paths["ledger"]) as db:
                            self.assertEqual(db.execute("SELECT cost FROM charges WHERE id='during-activation'").fetchone()[0], 9)

    def test_every_recovery_failure_retains_pause_and_can_retry(self):
        phases = ("recovery_pause", "recovery_stop", "recovery_release", "recovery_install", "recovery_status_email", "recovery_compatibility", "recovery_start", "recovery_health", "recovery_availability", "recovery_final_health")
        for phase in phases:
            for suffix in ("", ":after"):
                with self.subTest(phase=phase+suffix):
                    state, paths, previous = self.fixture()
                    with self.assertRaises(RuntimeError):
                        FakeDeployment(state, ["final_health",phase+suffix]).activate()
                    self.assertEqual(json.loads(state.read_text())["status"], "recovery_failed")
                    self.assertEqual(json.loads(paths["availability"].read_text())["Status"], "paused")
                    with sqlite3.connect(paths["ledger"]) as db:
                        self.assertEqual(db.execute("SELECT active FROM maintenance_gate").fetchone()[0], 1)
                        self.assertEqual(db.execute("SELECT cost FROM charges WHERE id='during-activation'").fetchone()[0], 9)
                    FakeDeployment(state).recover()
                    self.assert_restored(state, paths, previous)

    def test_missing_compatibility_proof_never_stops_service(self):
        state, paths, _ = self.fixture()
        activation.atomic_json(paths["compatibility_report"], dict(round_trip_passed=False))
        deployment = FakeDeployment(state)
        with self.assertRaises(RuntimeError):
            deployment.activate()
        self.assertEqual(deployment.services, [])

    def test_interrupted_move_recovers_from_directory_state(self):
        state, paths, previous = self.fixture()
        deployment = FakeDeployment(state)
        deployment.save(status="activating",phase="backup_release",phase_complete=False)
        paths["active"].rename(paths["backup"])
        with self.assertRaises(RuntimeError):
            FakeDeployment(state).activate()
        self.assert_restored(state, paths, previous)

    def test_preparation_preserves_previous_policy_and_consistent_ledger(self):
        state, paths, previous = self.fixture()
        config = json.loads(state.read_text())
        config["paths"]["data_backup"] = str(state.parent / "data-backup")
        config["backup_files"] = [str(paths["ledger"]), str(paths["availability"])]
        specification = state.parent / "specification.json"
        activation.atomic_json(specification, config)
        prepared_state = state.parent / "prepared.json"
        preparation.prepare(specification, prepared_state)
        self.assertEqual(json.loads(prepared_state.read_text())["previous"]["availability"], previous)
        services = []
        with patch.object(preparation.activation.Deployment, "service", lambda self, verb: services.append(verb)):
            preparation.backup(prepared_state)
        self.assertEqual(services, ["stop", "start"])
        self.assertTrue(json.loads(prepared_state.read_text())["backups_complete"])
        backup_db = Path(config["paths"]["data_backup"]) / str(paths["ledger"]).lstrip("/")
        with sqlite3.connect(backup_db) as db:
            self.assertEqual(db.execute("SELECT cost FROM charges").fetchone()[0], 3)
        self.assertEqual(json.loads(paths["availability"].read_text())["Status"], "paused")
        with self.assertRaises(RuntimeError):
            preparation.prepare(specification, prepared_state)

    def test_unfinished_compatibility_recovery_move_can_resume(self):
        state, paths, previous = self.fixture()
        recovery = state.parent / "recovery"
        recovery.mkdir()
        (recovery / "AnalysisITC.Web.dll").write_text("old")
        incompatible = state.parent / "incompatible"
        deployment = FakeDeployment(state)
        deployment.state["paths"].update(recovery=str(recovery),incompatible=str(incompatible))
        deployment.save(status="recovering")
        paths["active"].rename(incompatible)
        FakeDeployment(state).recover()
        self.assert_restored(state, paths, previous)


if __name__ == "__main__":
    unittest.main()
