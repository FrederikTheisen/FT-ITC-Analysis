"""Deterministic two-process races against the unchanged production registry.

A FIFO holds one already-open registry read. Restore the pathname to a regular
file, complete writer B, then deliver the original snapshot to A's open stream.
This models a reader of the old inode across atomic replacement. No production
hooks, private-method calls, or modified assembly are used. Requires Unix.
"""
import errno
import json
import os
from pathlib import Path
import select
import subprocess
import sys
import time

probe = str(Path(sys.argv[1]).resolve())
root = Path(sys.argv[2]).resolve()
fixed = '--fixed' in sys.argv[3:]
if not str(root).startswith('/private/tmp/registration-reliability-'):
    raise SystemExit('Use /private/tmp/registration-reliability-*')
root.mkdir(parents=True, exist_ok=True)


def run(mode, directory, *args):
    p = subprocess.run(['dotnet', probe, mode, str(directory), *args],
                       capture_output=True, text=True, timeout=15)
    if p.returncode:
        raise RuntimeError((p.returncode, p.stdout, p.stderr))
    return p.stdout


def last_json(output):
    return json.loads(output.strip().splitlines()[-1])


def snapshot(directory):
    return {name: json.loads((directory / name).read_text())
            for name in ('manual.json', 'registered.json', 'markers.json') if (directory / name).exists()}


cases = [
    ('create', 'revoke', 'manual', False),
    ('details', 'regenerate', 'manual', False),
    ('quota', 'tier', 'manual', False),
    ('tier', 'details', 'manual', False),
    ('revoke', 'quota', 'registered', False),
    ('regenerate', 'revoke', 'registered', False),
    ('scrub', 'provision', 'registered', False),
    ('provision', 'revoke', 'registered', False),
    ('details', 'revoke', 'manual', True),
]
if fixed:
    cases += [('marker', 'marker', 'markers', False), ('marker', 'marker', 'markers', True)]
for index, (a, b, owner, terminate) in enumerate(cases):
    directory = root / f'{index:02}-{a}-{b}-{owner}'
    directory.mkdir()
    seed = last_json(run('seed', directory))
    account_id = 'marker-a' if owner == 'markers' else seed['manualId' if owner == 'manual' else 'registeredId']
    b_id = 'marker-b' if owner == 'markers' else account_id
    path = directory / (owner + '.json')
    if owner == 'markers': path.write_text('[]')
    original = path.read_bytes()
    before = snapshot(directory)
    path.unlink()
    os.mkfifo(path, 0o600)
    child = subprocess.Popen(['dotnet', probe, 'mutate', str(directory), a, account_id],
                             stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    fd = None
    writer_b = None
    events = [{'event': 'start-A', 'pid': child.pid, 'at': time.monotonic()}]
    try:
        if not select.select([child.stdout], [], [], 10)[0] or child.stdout.readline().strip() != 'START':
            raise RuntimeError('A did not start')
        deadline = time.monotonic() + 10
        while fd is None:
            try:
                fd = os.open(path, os.O_WRONLY | os.O_NONBLOCK)
            except OSError as e:
                if e.errno != errno.ENXIO or time.monotonic() > deadline:
                    raise
                time.sleep(.01)
        if a == 'provision':
            # Provisioning reads registered records once for validation, then again
            # for its final append. Gate the second read, not just validation.
            path.rename(directory / 'validation-read.fifo')
            os.mkfifo(path, 0o600)
            os.write(fd, original)
            os.close(fd)
            fd = None
            deadline = time.monotonic() + 10
            while fd is None:
                try:
                    fd = os.open(path, os.O_WRONLY | os.O_NONBLOCK)
                except OSError as e:
                    if e.errno != errno.ENXIO or time.monotonic() > deadline:
                        raise
                    time.sleep(.01)
            events.append({'event': 'A-completed-validation-opened-append-read', 'at': time.monotonic()})
        events.append({'event': 'A-opened-registry-read', 'at': time.monotonic()})
        # The FIFO remains open in A. B sees normal registry files.
        path.rename(directory / 'held-read.fifo')
        path.write_bytes(original)
        if fixed:
            writer_b = subprocess.Popen(['dotnet', probe, 'mutate', str(directory), b, b_id],
                                        stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            if not select.select([writer_b.stdout], [], [], 5)[0] or writer_b.stdout.readline().strip() != 'START':
                raise RuntimeError('B did not start')
            time.sleep(.2)
            assert writer_b.poll() is None, 'B bypassed the held registry lock'
            assert snapshot(directory) == before, 'A or B changed the registry while A was paused'
            events.append({'event': 'B-started-and-blocked-by-A', 'at': time.monotonic()})
        else:
            output_b = run('mutate', directory, b, b_id)
            after_b = snapshot(directory)
            events.append({'event': 'B-completed', 'at': time.monotonic()})
        if terminate:
            child.kill()
            events.append({'event': 'A-killed-before-write', 'at': time.monotonic()})
        else:
            os.write(fd, original)
            events.append({'event': 'released-original-snapshot-to-A', 'at': time.monotonic()})
        os.close(fd)
        fd = None
        output_a, error_a = child.communicate(timeout=15)
        if not terminate and child.returncode:
            raise RuntimeError((child.returncode, output_a, error_a))
        events.append({'event': 'A-exited', 'at': time.monotonic()})
        if fixed:
            output_b, error_b = writer_b.communicate(timeout=10)
            if writer_b.returncode:
                raise RuntimeError((writer_b.returncode, output_b, error_b))
            after_b = snapshot(directory)
            events.append({'event': 'B-completed-after-A-release', 'at': time.monotonic()})
        final = snapshot(directory)
        original_code = seed['Code'] if owner == 'manual' else 'ftitc_op_registered_fixture'
        auth = last_json(run('auth', directory, original_code))
        regenerated_auth = None
        if a == 'regenerate' or b == 'regenerate':
            regenerated = last_json(output_a if a == 'regenerate' else output_b)
            regenerated_auth = last_json(run('auth', directory, regenerated['Item2']))
        result = dict(test='race', fixed=fixed, a=a, b=b, owner=owner, terminate=terminate,
                      events=events, before=before, after_b=after_b, final=final,
                      output_a=output_a, output_b=output_b, originalCredential=auth,
                      regeneratedCredential=regenerated_auth)
        if not terminate and owner != 'markers':
            control = directory / 'serialized-control'
            control_seed = last_json(run('seed', control))
            control_id = control_seed['manualId' if owner == 'manual' else 'registeredId']
            control_b = run('mutate', control, b, control_id)
            control_a = run('mutate', control, a, control_id)
            result['serializedControl'] = dict(final=snapshot(control),
                                               output_b=control_b, output_a=control_a)
        (directory / 'evidence.json').write_text(json.dumps(result, indent=2))
        print(json.dumps(result), flush=True)
    finally:
        if fd is not None:
            os.close(fd)
        if child.poll() is None:
            child.kill()
            child.wait(timeout=5)
        if writer_b is not None and writer_b.poll() is None:
            writer_b.kill()
            writer_b.wait(timeout=5)
        # Never leave a FIFO at a registry pathname after a failed probe.
        if path.exists() and not path.is_file():
            path.unlink()
            path.write_bytes(original)
        held = directory / 'held-read.fifo'
        if held.exists():
            held.unlink()
        validation = directory / 'validation-read.fifo'
        if validation.exists():
            validation.unlink()
