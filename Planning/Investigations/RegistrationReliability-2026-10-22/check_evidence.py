"""Check reproduction evidence, not acceptance of the defective behavior.

Usage: python3 check_evidence.py EVIDENCE_DIRECTORY
An exit of zero confirms the documented failures and passing controls occurred.
It does not mean the production reliability requirements pass.
"""
import json
from pathlib import Path
import sys

root = Path(sys.argv[1])


def read(name):
    return [json.loads(line) for line in (root / name).read_text().splitlines()
            if line.startswith('{')]


faults = read('faults.jsonl')
mail = [r for r in faults if r['test'] == 'mail']
assert len(mail) == 8
for r in mail:
    assert len(r['failures']) == 6 and 'none' not in r['failures']
    assert r['noRequests'] and r['sent'] == 2
    assert all(x['attempts'] == 0 and x['last_failure_code'] is None for x in r['after'])
    assert [x['next_attempt_utc'] for x in r['before']] == [x['next_attempt_utc'] for x in r['after']]
    assert r['provisioned'] == (r['kind'] == 'access-code')
    assert all(x['state'] == 'sent' for x in r['repaired'])
markers = {r['fault']: r for r in faults if r['test'] == 'markers'}
assert len(markers) == 5
assert markers['valid']['blocked'] and markers['valid']['sent'] == 0
assert not markers['absent']['blocked'] and markers['absent']['sent'] == 1
for fault in ('malformed', 'null', 'unreadable'):
    r = markers[fault]
    assert not r['blocked'] and not r['contains'] and r['sent'] == 1
    assert r['addError'] is None and len(json.loads(r['afterAdd'])) == 1
assert len(json.loads(markers['unreadable']['original'])) == 3
assert all(r['scrubbedDenied'] for r in markers.values())
controls = {r['test']: r for r in faults if r['test'] not in ('mail', 'markers')}
assert controls['payload']['first'] is False and controls['payload']['second'] is True
assert controls['payload']['rows'][0]['last_failure_code'] == 'delivery_payload_unreadable'
assert controls['payload']['sent'] == 1
assert controls['delayed']['sent'] == 1
assert controls['delayed']['rows'][-1]['state'] == 'pending'
assert controls['expiry']['beforeBoundary'] and not controls['expiry']['atBoundary']
assert controls['cancel']['stopMilliseconds'] < 5000
assert controls['cancel']['rows'][0]['attempts'] == 0

races = read('races.jsonl')
assert len(races) == 9
preserved_field = {'revoke': 'revokedAtUtc', 'regenerate': 'codeHash', 'tier': 'accessTier',
                   'details': 'name', 'quota': 'monthlyQuotaUsdOverride'}
for r in races:
    if r['terminate']:
        assert r['final'] == r['after_b'] and not r['originalCredential']['authorized']
        continue
    name = r['owner'] + '.json'
    before = r['after_b'][name]
    after = r['final'][name]
    serial = r['serializedControl']['final'][name]
    if r['b'] == 'provision':
        assert len(before) == 2 and len(after) == 1 and len(serial) == 2
    else:
        field = preserved_field[r['b']]
        assert before[0][field] != after[0][field], (r['a'], r['b'], field)
        assert serial[0][field] == before[0][field] or field in ('revokedAtUtc', 'codeHash')
        if field == 'revokedAtUtc': assert serial[0][field] is not None
        if field == 'codeHash':
            code = json.loads(r['serializedControl']['output_b'].splitlines()[-1])['Item2']
            import hashlib
            assert serial[0][field] == hashlib.sha256(code.encode()).hexdigest()
assert next(r for r in races if r['a'] == 'create')['originalCredential']['authorized']
assert not next(r for r in races if r['b'] == 'regenerate')['regeneratedCredential']['authorized']
assert next(r for r in races if r['a'] == 'regenerate')['regeneratedCredential']['authorized']

outage = read('worker-outage.jsonl')
stalled = next(r for r in outage if r['test'] == 'worker-outage')
recovered = next(r for r in outage if r['test'] == 'worker-recovered')
assert stalled['before'] == stalled['after'] and stalled['sent'] == 0
assert recovered['sent'] == 2 and all(r['state'] == 'sent' for r in recovered['rows'])

worker = read('worker.jsonl')
dispatch = [r for r in worker if r['test'] == 'dispatch']
summary = next(r for r in worker if r['test'] == 'worker-summary')
assert summary['arrivals'] == 100 and summary['messages'] == len(dispatch)
gaps = [b['elapsedSeconds'] - a['elapsedSeconds'] for a, b in zip(dispatch, dispatch[1:])]
assert all(g >= 60 for g in gaps)
assert max(r['queueWaitSeconds'] for r in dispatch) > 600
assert set(r['kind'] for r in dispatch) == {'activation', 'access-code'}
assert summary['stopMilliseconds'] < 5000 and not summary['drainCompleted']
print(json.dumps(dict(reproductionChecks='passed; confirms defects, not fixes',
    mailFaultCases=len(mail), markerCases=len(markers), losingInterleavings=8,
    serializedControls=8, terminatedWriterControls=1,
    workerMessages=len(dispatch), maxCompletedQueueWaitSeconds=max(r['queueWaitSeconds'] for r in dispatch),
    dispatchGapMinSeconds=min(gaps), dispatchGapMaxSeconds=max(gaps),
    workerElapsedSeconds=summary['elapsedSeconds'],
    pendingAtStop=sum(r['state'] == 'pending' for r in summary['rows'])), indent=2))
