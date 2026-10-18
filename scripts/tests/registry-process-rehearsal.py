#!/usr/bin/env python3
"""Linux-only registry mutation rehearsal using temporary paths and real identities."""
import argparse
import fcntl
import json
import os
from pathlib import Path
import pwd
import subprocess
import tempfile
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("probe")
    parser.add_argument("release")
    args = parser.parse_args()
    identity = pwd.getpwnam("ftitc-web")
    with tempfile.TemporaryDirectory(prefix="ftitc-registry-rehearsal-") as temporary:
        directory = Path(temporary)
        os.chown(directory, identity.pw_uid, identity.pw_gid)
        os.chmod(directory, 0o2770)
        (directory / ".ftitc-rehearsal").touch()

        def command(mode, registry_id=None, service=False):
            return (["runuser", "-u", "ftitc-web", "--"] if service else []) + ["dotnet", args.probe, mode, args.release, str(directory)] + ([registry_id] if registry_id else [])

        def run(mode, registry_id=None, service=False, success=True):
            result = subprocess.run(command(mode, registry_id, service), capture_output=True, text=True, timeout=15)
            if (result.returncode == 0) != success:
                raise AssertionError(f"{mode} returned {result.returncode}: {result.stderr}")
            return result.stdout.strip()

        first = run("registry-create")
        second = run("registry-create", service=True)
        lock_path = directory / "public.json.lock"
        original_inode = lock_path.stat().st_ino
        assert lock_path.stat().st_mode & 0o777 == 0o660
        assert lock_path.stat().st_gid == identity.pw_gid
        with lock_path.open("r+") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            touch = subprocess.Popen(command("registry-touch", first, True), stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            revoke = subprocess.Popen(command("registry-revoke", second), stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            time.sleep(0.3)
            assert touch.poll() is None and revoke.poll() is None
            fcntl.flock(lock, fcntl.LOCK_UN)
            for process in (touch, revoke):
                _, error = process.communicate(timeout=10)
                assert process.returncode == 0, error
        records = json.loads((directory / "public.json").read_text())
        assert next(record for record in records if record["id"] == first)["lastUseAtUtc"]
        assert next(record for record in records if record["id"] == second)["revokedAtUtc"]
        processes = [subprocess.Popen(command("registry-create", service=index % 2 == 0), stdout=subprocess.PIPE, stderr=subprocess.PIPE) for index in range(8)]
        for process in processes:
            _, error = process.communicate(timeout=15)
            assert process.returncode == 0, error
        assert len(json.loads((directory / "public.json").read_text())) == 10

        holder = subprocess.Popen(["python3", "-c", "import fcntl,sys,time; f=open(sys.argv[1],'r+'); fcntl.flock(f,fcntl.LOCK_EX); print('ready',flush=True); time.sleep(60)", str(lock_path)], stdout=subprocess.PIPE)
        try:
            assert holder.stdout.readline().strip() == b"ready"
            pending = subprocess.Popen(command("registry-create", service=True), stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            time.sleep(0.3)
            assert pending.poll() is None
            holder.kill()
            holder.wait(timeout=5)
            _, error = pending.communicate(timeout=10)
            assert pending.returncode == 0, error
        finally:
            if holder.poll() is None:
                holder.kill()
                holder.wait(timeout=5)

        with lock_path.open("r+") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            original = (directory / "public.json").read_bytes()
            started = time.monotonic()
            assert run("registry-touch", first, service=True) == "TryTouch=True"
            assert 4.8 <= time.monotonic() - started < 10
            run("registry-create", success=False)
            run("registry-revoke", first, service=True, success=False)
            assert (directory / "public.json").read_bytes() == original
        assert run("registry-revoke", first, service=True) == "Revoke=True"
        assert run("registry-touch", first) == "TryTouch=False"
        assert lock_path.stat().st_ino == original_inode
        print("PASS: Linux service/admin permissions, concurrent create/touch/revoke, termination, timeout semantics, and persistent lock inode")


if __name__ == "__main__":
    main()
