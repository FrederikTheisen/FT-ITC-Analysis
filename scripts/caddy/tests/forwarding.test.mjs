import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { createServer, request as httpRequest } from 'node:http';
import { mkdtempSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { after, before, test } from 'node:test';

const caddy = process.env.CADDY_BIN;
assert.ok(caddy, 'Set CADDY_BIN to a local Caddy 2.11.7 executable.');
const secret = 'a'.repeat(64); // Local fixture only.
const visitor = '198.51.100.7';
const snippet = fileURLToPath(new URL('../website-forwarding.caddy', import.meta.url));
const roots = [];
const processes = [];
let backend;
let main;
let upstream;
const realFetch = globalThis.fetch;

async function freePort() {
  const server = createServer();
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const port = server.address().port;
  await new Promise(resolve => server.close(resolve));
  return port;
}
async function start(configuredSecret) {
  const directory = mkdtempSync(join(process.env.ITC214_TEST_TMP || tmpdir(), 'forwarding-'));
  roots.push(directory);
  const port = await freePort();
  const config = join(directory, 'Caddyfile');
  writeFileSync(config, `{
 admin off
 auto_https off
}
import "${snippet}"
http://127.0.0.1:${port} {
 log
 import ftitc_website_forwarding ${upstream}
}
`);
  const env = { ...process.env, XDG_DATA_HOME: join(directory, 'data'), XDG_CONFIG_HOME: join(directory, 'config') };
  delete env.FTITC_WEBSITE_PROXY_SECRET;
  if (configuredSecret !== undefined) env.FTITC_WEBSITE_PROXY_SECRET = configuredSecret;
  const child = spawn(caddy, ['run', '--config', config, '--adapter', 'caddyfile'], { env, stdio: ['ignore', 'pipe', 'pipe'] });
  const host = { child, url: `http://127.0.0.1:${port}`, logs: '', directory };
  processes.push(host);
  child.stdout.on('data', data => { host.logs += data; });
  child.stderr.on('data', data => { host.logs += data; });
  child.on('error', error => { host.error = error; });
  const deadline = Date.now() + 10000; // Startup/deadlock guard, not a timing expectation.
  while (true) {
    if (child.exitCode !== null || host.error) throw new Error(`Caddy failed to start: ${host.error || host.logs}`);
    try { await realFetch(host.url); break; } catch {
      if (Date.now() >= deadline) throw new Error(`Caddy did not become ready: ${host.logs}`);
      await new Promise(resolve => setTimeout(resolve, 10));
    }
  }
  return host;
}
async function send(path = '/api/registration/status', headers = {}, host = main, method = 'GET') {
  return realFetch(host.url + path, { method, headers, ...(method === 'POST' ? { body: '{"fixture":true}' } : {}) });
}
async function sendRepeated(headers) {
  return new Promise((resolve, reject) => {
    // A flat header array preserves separate physical header lines on the wire.
    const request = httpRequest(main.url + '/api/registration/status', {
      headers: ['Host', new URL(main.url).host, ...headers.flat()], agent: false
    }, response => { response.resume(); response.on('end', () => resolve(response.statusCode)); });
    request.on('error', reject);
    request.end();
  });
}
function assertion(ip = visitor, key = secret) {
  return { 'Proxy-Authorization': `Bearer ${key}`, 'X-FTITC-Visitor-IP': ip };
}
function checkStripped(headers) {
  for (const name of ['proxy-authorization', 'x-ftitc-visitor-ip', 'cf-connecting-ip', 'cf-connecting-ipv6', 'x-real-ip', 'forwarded'])
    assert.equal(headers[name], undefined, name);
}

before(async () => {
  backend = createServer(async (request, response) => {
    let body = ''; for await (const part of request) body += part;
    response.setHeader('Content-Type', 'application/json');
    response.setHeader('Set-Cookie', 'csrf=roundtrip; HttpOnly');
    response.end(JSON.stringify({ headers: request.headers, url: request.url, method: request.method, body }));
  });
  backend.listen(0, '127.0.0.1'); await once(backend, 'listening');
  upstream = `127.0.0.1:${backend.address().port}`;
  main = await start(secret);
});
after(async () => {
  for (const host of processes) {
    if (host.child.exitCode === null) { host.child.kill('SIGTERM'); await once(host.child, 'exit'); }
    assert.equal(host.logs.includes(secret), false, 'Proxy credential must not appear in normal Caddy logs.');
  }
  await new Promise(resolve => backend.close(resolve));
  for (const directory of roots) rmSync(directory, { recursive: true, force: true });
});

for (const route of ['/api/registration', '/api/registration/activate', '/api/registration/status', '/api/viewer/token']) {
  test(`authenticates only the visitor assertion on ${route}`, async () => {
    const response = await send(`${route}?preserved=1`, {
      ...assertion(), Cookie: 'csrf=roundtrip', 'X-CSRF-TOKEN': 'fixture', Authorization: 'Bearer application-code',
      'X-Forwarded-For': '203.0.113.99', 'X-Forwarded-Proto': 'https', 'CF-Connecting-IP': '203.0.113.99'
    }, main, route === '/api/registration' || route.endsWith('/activate') ? 'POST' : 'GET');
    assert.equal(response.status, 200);
    const result = await response.json();
    assert.equal(result.headers['x-forwarded-for'], visitor);
    assert.equal(result.headers['x-forwarded-proto'], 'http');
    assert.equal(result.headers.cookie, 'csrf=roundtrip');
    assert.equal(result.headers['x-csrf-token'], 'fixture');
    assert.equal(result.headers.authorization, 'Bearer application-code');
    assert.equal(result.url, `${route}?preserved=1`);
    assert.equal(result.body, result.method === 'POST' ? '{"fixture":true}' : '');
    assert.equal(response.headers.get('set-cookie'), 'csrf=roundtrip; HttpOnly');
    checkStripped(result.headers);
  });
}
test('separates visitors sharing one connection peer', async () => {
  const values = [];
  for (const ip of ['198.51.100.7', '203.0.113.8', '2001:0db8:0001:0000:0000:0000:0000:0007', '0000:0000:0000:0000:0000:ffff:c000:0207']) {
    const result = await (await send(undefined, assertion(ip))).json();
    assert.equal(result.headers['x-forwarded-for'], ip);
    values.push(result.headers['x-forwarded-for']);
  }
  assert.equal(new Set(values).size, 4);
});
test('direct clients keep their real peer and scheme despite forged public headers', async () => {
  const result = await (await send('/api/interpretation/status', {
    'X-Forwarded-For': visitor, 'X-Forwarded-Proto': 'https', 'CF-Connecting-IP': visitor,
    'CF-Connecting-IPv6': '2001:db8::1', 'X-Real-IP': visitor, Forwarded: `for=${visitor}`
  })).json();
  assert.equal(result.headers['x-forwarded-for'], '127.0.0.1');
  assert.equal(result.headers['x-forwarded-proto'], 'http');
  checkStripped(result.headers);
});
for (const ip of ['', 'example.org', '198.51.100.256', '0198.51.100.7', '198.51.100.7:80', '1.2.3.4,5.6.7.8', '2001:db8::7', 'fe80::1%lo0', '[::1]', '0000:0000:0000:0000:0000:0000:0000:zzzz']) {
  test(`rejects authenticated malformed/noncanonical visitor ${JSON.stringify(ip)}`, async () => {
    assert.equal((await send(undefined, assertion(ip))).status, 403);
  });
}
for (const headers of [
  { 'X-FTITC-Visitor-IP': visitor },
  { 'Proxy-Authorization': `Bearer ${secret}` },
  assertion(visitor, 'b'.repeat(64)),
  { 'Proxy-Authorization': `Bearer ${secret},Bearer ${secret}`, 'X-FTITC-Visitor-IP': visitor }
]) {
  test(`rejects an incomplete or invalid assertion ${JSON.stringify(Object.keys(headers))}`, async () => {
    assert.equal((await send(undefined, headers)).status, 403);
  });
}
for (const headers of [
  [['Proxy-Authorization', `Bearer ${secret}`], ['Proxy-Authorization', 'wrong'], ['X-FTITC-Visitor-IP', visitor]],
  [['Proxy-Authorization', 'wrong'], ['Proxy-Authorization', `Bearer ${secret}`], ['X-FTITC-Visitor-IP', visitor]],
  [['Proxy-Authorization', `Bearer ${secret}`], ['X-FTITC-Visitor-IP', visitor], ['X-FTITC-Visitor-IP', 'bad']],
  [['Proxy-Authorization', `Bearer ${secret}`], ['X-FTITC-Visitor-IP', 'bad'], ['X-FTITC-Visitor-IP', visitor]],
  [['Proxy-Authorization', `Bearer ${secret}`], ['X-FTITC-Visitor-IP', visitor], ['X-FTITC-Visitor-IP', '203.0.113.8']]
]) {
  test(`rejects ambiguous repeated values ${JSON.stringify(headers.map(([name]) => name))}`, async () => {
    assert.equal(await sendRepeated(headers), 403);
  });
}
for (const path of ['/api/internal/health', '/api/interpretation/generate', '/api/registration/extra', '/api/registration/', '/API/REGISTRATION/EXTRA']) {
  test(`does not grant authenticated forwarding to ${path}`, async () => {
    assert.equal((await send(path, assertion())).status, 403);
  });
}
for (const configured of [undefined, '', 'short']) {
  test(`missing/invalid Caddy secret cannot authenticate (${configured ?? 'unset'})`, async () => {
    const host = await start(configured);
    assert.equal((await send(undefined, assertion(visitor, configured || ''), host)).status, 403);
    assert.equal((await send(undefined, assertion(), host)).status, 403);
    const direct = await (await send(undefined, { 'CF-Connecting-IP': visitor }, host)).json();
    assert.equal(direct.headers['x-forwarded-for'], '127.0.0.1');
  });
}

test('real Worker and Caddy carry native visitor identity end to end without exposing credentials', async () => {
  assert.ok(process.env.WEBSITE_ROOT, 'Set WEBSITE_ROOT to the website checkout.');
  const source = readFileSync(resolve(process.env.WEBSITE_ROOT, '_worker.js'), 'utf8');
  const worker = (await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`)).default;
  const previous = globalThis.fetch;
  globalThis.fetch = async (target, options) => {
    const parsed = new URL(target);
    assert.equal(parsed.origin, 'https://app.ft-itc.org');
    return realFetch(main.url + parsed.pathname + parsed.search, { ...options, ...(options.body ? { duplex: 'half' } : {}) });
  };
  try {
    for (const [address, expected] of [['198.51.100.7', '198.51.100.7'], ['203.0.113.8', '203.0.113.8'], ['2001:db8::7', '2001:0db8:0000:0000:0000:0000:0000:0007']]) {
      const response = await worker.fetch(new Request('https://ft-itc.org/api/registration/status', {
        headers: { 'CF-Connecting-IP': address, 'X-FTITC-Visitor-IP': '203.0.113.99', 'Proxy-Authorization': 'forged' }
      }), { FTITC_WEBSITE_PROXY_SECRET: secret });
      assert.equal(response.status, 200);
      const result = await response.json();
      assert.equal(result.headers['x-forwarded-for'], expected);
      checkStripped(result.headers);
      assert.equal(JSON.stringify(result).includes(secret), false);
    }
  } finally { globalThis.fetch = previous; }
});
