"""Check the corrected behavior against the retained independent reproductions."""
import hashlib
import json
from pathlib import Path
import sys

root = Path(sys.argv[1])


def read(name):
    return [json.loads(line) for line in (root / name).read_text().splitlines() if line.startswith('{')]


faults = read('faults.jsonl')
mail = [r for r in faults if r['test'] == 'mail']
assert len(mail) == 8
for r in mail:
    assert r['failures'] == ['none'] * 6 and r['before'] == r['after']
    assert not r['provisioned'] and r['noRequests'] and r['sent'] == 2
    assert all(x['state'] == 'sent' for x in r['repaired'])
markers = {r['fault']: r for r in faults if r['test'] == 'markers'}
assert len(markers) == 5 and all(r['scrubbedDenied'] for r in markers.values())
assert markers['valid']['blocked'] and markers['valid']['sent'] == 0
assert not markers['absent']['blocked'] and markers['absent']['sent'] == 1
for fault in ('malformed', 'null', 'unreadable'):
    r = markers[fault]
    assert r['blocked'] and r['contains'] is None and r['sent'] == 0
    assert r['containsError'] == r['addError'] == 'OperatorTombstoneRegistryUnavailableException'
    assert r['afterAdd'] == (r['original'] if fault == 'unreadable' else r['beforeAdd'])
    assert r['delivery'][0]['state'] == 'pending' and r['delivery'][0]['attempts'] == 0
controls = {r['test']: r for r in faults if r['test'] not in ('mail', 'markers')}
assert controls['payload']['first'] and controls['payload']['second'] and controls['payload']['sent'] == 1
assert controls['payload']['rows'][0]['last_failure_code'] == 'delivery_payload_unreadable'
assert controls['delayed']['sent'] == 1 and controls['delayed']['rows'][-1]['state'] == 'pending'
assert controls['expiry']['beforeBoundary'] and not controls['expiry']['atBoundary']
assert controls['cancel']['stopMilliseconds'] < 5000 and controls['cancel']['rows'][0]['attempts'] == 0

races = read('races.jsonl')
assert len(races) == 11
for r in races:
    assert r['fixed'] and r['final'] == r['after_b']
    events = [e['event'] for e in r['events']]
    assert events.index('B-started-and-blocked-by-A') < events.index('A-exited') < events.index('B-completed-after-A-release')
    if r['owner'] == 'markers':
        assert {x['Id'] for x in r['final']['markers.json']} == ({'marker-b'} if r['terminate'] else {'marker-a', 'marker-b'})
        continue
    records = r['final'][r['owner'] + '.json']
    first = records[0]
    if r['b'] == 'revoke':
        assert first['revokedAtUtc'] is not None and not r['originalCredential']['authorized']
    elif r['b'] == 'regenerate':
        code = json.loads(r['output_b'].splitlines()[-1])['Item2']
        assert first['codeHash'] == hashlib.sha256(code.encode()).hexdigest()
        assert r['regeneratedCredential']['authorized'] and not r['originalCredential']['authorized']
    elif r['b'] == 'tier': assert first['accessTier'] == 'standard' and first['monthlyQuotaUsdOverride'] == 7
    elif r['b'] == 'details': assert first['name'] == 'Changed' and first['accessTier'] == 'standard'
    elif r['b'] == 'quota': assert first['monthlyQuotaUsdOverride'] == 7 and first['revokedAtUtc'] is not None
    elif r['b'] == 'provision': assert len(records) == 2 and first['scrubbedAtUtc'] is not None
    if r['a'] == 'create': assert len(records) == 2
    if r['a'] == 'provision': assert len(records) == 2
    if r['a'] == 'regenerate': assert not r['regeneratedCredential']['authorized']

outage = read('worker-outage.jsonl')
stalled = next(r for r in outage if r['test'] == 'worker-outage')
recovered = next(r for r in outage if r['test'] == 'worker-recovered')
assert stalled['before'] == stalled['after'] and stalled['sent'] == 0
assert recovered['sent'] == 2 and all(r['state'] == 'sent' for r in recovered['rows'])

worker = read('worker.jsonl')
dispatch = [r for r in worker if r['test'] == 'dispatch']
summary = next(r for r in worker if r['test'] == 'worker-summary')
assert summary['arrivals'] == 100 and summary['messages'] == len(dispatch) == 200
assert sum(r['kind'] == 'activation' for r in dispatch) == 100
assert sum(r['kind'] == 'access-code' for r in dispatch) == 100
assert max(r['queueWaitSeconds'] for r in dispatch) <= 600
assert summary['stopMilliseconds'] < 5000 and summary['drainCompleted']
assert all(r['state'] == 'sent' for r in summary['rows'])
print(json.dumps(dict(acceptanceChecks='passed', mailFaultCases=8, markerCases=5,
    serializedOperatorInterleavings=8, operatorTerminationControls=1,
    concurrentMarkerControls=2, workerMessages=len(dispatch),
    maxQueueWaitSeconds=max(r['queueWaitSeconds'] for r in dispatch),
    drainSeconds=summary['drainSeconds'], workerElapsedSeconds=summary['elapsedSeconds'],
    pendingAtStop=0, stopMilliseconds=summary['stopMilliseconds']), indent=2))
