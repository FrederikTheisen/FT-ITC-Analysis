# Authenticated website forwarding — ITC-214

This configuration was deployed with user approval on 2026-10-21. Nothing in this
directory deploys itself. Future production changes still require deployment approval.

## Contract

The website Worker sends `Proxy-Authorization: Bearer <secret>` and
`X-FTITC-Visitor-IP` on its four existing API routes. The same
`FTITC_WEBSITE_PROXY_SECRET` binding must hold 64 lowercase hexadecimal characters
(32 random bytes) in the Worker and Caddy environments. It is separate from
application bearer codes, Cloudflare API tokens and Turnstile credentials.

Caddy accepts the assertion only on registration, activation, registration status
and viewer-token routes. IPv4 uses strict dotted decimal; the Worker expands IPv6
to eight lowercase four-digit groups, including mapped IPv4. Caddy independently
validates that format. Duplicate fields and comma lists are rejected. Assertions
with the wrong secret, invalid address or wrong route return 403. Requests without
an assertion retain their connection-peer address. Authenticated requests get one
vetted `X-Forwarded-For` value; the application keeps `ForwardLimit = 1`.

The secret and visitor assertion are stripped before Kestrel. Ordinary application
Authorization, cookies, CSRF tokens, method, body, query and Set-Cookie responses
remain intact. Worker requests without a configured secret or valid edge address
return a generic non-cacheable 503. Activation's no-store/no-referrer behavior is
preserved. Redirects from the origin are not followed with the proxy credential.

`Proxy-Authorization` uses Caddy's built-in credential redaction. Do not enable
`log_credentials`, request-header dumps or `--environ`; these can expose secrets.
The service override removes the currently deployed `--environ` startup flag.

Cloudflare **Pseudo IPv4 must be Off or Add Header, not Overwrite Headers** so
`CF-Connecting-IP` carries the native visitor address. Check the live zone setting
before deployment. The Worker does not trust a caller-supplied CF-Connecting-IPv6
header as an alternative source. Local IPv6 tests do not verify live IPv6 ingress.

## Local validation

Use Node.js 22 or newer and Caddy 2.11.7 (matching MIST). With both repositories
checked out locally:

```sh
# In the website repository:
node --test tests/worker.test.mjs

# In the application repository:
CADDY_BIN=/absolute/path/to/caddy \
WEBSITE_ROOT=/absolute/path/to/ft-itc-website \
node --test scripts/caddy/tests/forwarding.test.mjs
```

The second command starts isolated loopback Caddy instances and an echo backend,
then drives the actual Worker through Caddy using mock Cloudflare ingress headers.
It covers wrong/missing secrets, repeated physical header lines, malformed addresses,
route boundaries, forged public headers, direct clients, IPv4/native IPv6/mapped
IPv4 and credential stripping/redaction. All credentials and addresses are test
fixtures. It makes no production requests.

## Production record — 2026-10-21

Caddy was updated first, then the Worker, at 16:29 CEST. Cloudflare Pseudo IPv4 was
confirmed Off. Worker version `43012a2d-482e-431a-bc83-24477551af6b` serves 100% of
traffic; its source exactly matches the reviewed `_worker.js`. The code-only API
update retained the existing static assets and runtime/asset-routing configuration.
The local source remains uncommitted; include this change in the next repository
build so that it retains the fix.

Two distinct live IPv4 clients were verified against independent Cloudflare traces.
Nine tagged status requests reached Kestrel; ten HTTP checks passed. Direct clients
retain their connection address (including the private VPN address on that path),
spoofed addresses cannot select an identity, credentials are stripped, and direct
forged assertions return 403. Services and activation headers passed health checks;
public internal health stays 404. **Live IPv6 forwarding remains unverified.**

Caddy's protected rollback directory is `/root/itc214-20261021T142324Z`.
The prior Worker version is `9b90daf1-589c-49c6-8821-370f670e997f`; restore it first
if rollback is needed. Planning/ISSUES.md, ITC-214, records sanitized evidence.

## Deployment order — after approval only

1. Review the two repositories' exact diffs. The website change includes `_worker.js`,
   its tests, README and ignore rules. The application adds this directory and a
   pointer in the deployment notes; no application executable needs rebuilding.
   Retain the approved previous Worker version, `/etc/caddy/Caddyfile` and Caddy
   service overrides. Check live configuration for drift before applying the patch.
2. Confirm the Cloudflare Pseudo IPv4 setting above. Generate one new random
   32-byte secret as 64 lowercase hex characters in protected operator storage.
   Do not print it, place it in command arguments/history, commit it, or reuse the
   public test fixture. Create no real secret during code review.
3. Provision a root-owned mode-0600 `/etc/caddy/ftitc-website-proxy.env` containing
   only `FTITC_WEBSITE_PROXY_SECRET=<generated value>`. Stage the same value as a
   production Worker **secret binding**, never as a Wrangler vars entry or browser
   asset. Cloudflare secret/binding changes and `wrangler secret put` can create or
   deploy a Worker version; they are deployment actions and require approval.
4. Install `website-forwarding.caddy` as
   `/etc/caddy/ftitc-website-forwarding.caddy` (root-owned, mode 0644), apply
   `Caddyfile.patch` to `/etc/caddy/Caddyfile`, and install
   `website-forwarding.service.conf` as
   `/etc/systemd/system/caddy.service.d/website-forwarding.conf` (root-owned, 0644).
   The patch preserves the existing compression and security headers. Do not add
   global trusted proxies or change the application's forwarding limit.
5. Validate the candidate Caddyfile with Caddy 2.11.7. Its runtime environment
   placeholders keep the secret out of adapted JSON; syntax validation alone does
   not prove the secret binding exists. Separately check the protected env file's
   value format without printing it. Run `systemd-analyze verify caddy.service`
   after installing the override. Review for any remaining `--environ` or
   `log_credentials` settings before exposing the secret to Caddy.
6. During the approved restart window, run `systemctl daemon-reload` and restart
   Caddy so the new EnvironmentFile is loaded and the old `--environ` process is
   replaced. A reload alone cannot load a new service environment. The previous
   Worker still works as an ordinary peer until the new Worker is deployed; its
   existing shared-bucket behavior persists during that transition.
7. Deploy the reviewed Worker through the existing Cloudflare Workers Builds
   process with the secret binding present. The 2026-10-21 rollout instead used a code-only
   Cloudflare API update to preserve assets and avoid a repository push. Never deploy the new Worker before Caddy can
   validate its assertions.
8. Verify tagged status GETs through website and direct API from two distinct
   networks. Observe only selected address fields on the Caddy-to-Kestrel link:
   website X-Forwarded-For must now equal each native visitor address; direct API
   requests must retain their connection-peer address. Confirm spoofed headers do
   not change direct identity and assertions never reach Kestrel or normal logs.
   Repeat with a controlled IPv6 client when available. Avoid production
   registration submissions, activation-token use and rate-limit exhaustion.
   Finally check the existing status endpoints and public internal-health 404.

## Rollback and rotation

Roll back the Worker to its previous version **first**, then restore the previous
Caddyfile and service override and restart Caddy if required. Reversing that order
would leave a Worker carrying assertions to an origin that does not strip them.
The old forwarding defect returns on rollback; do not replace authentication with
unconditional trust in CF-Connecting-IP. Never restore the old `--environ` command
while the proxy-secret environment file remains configured.

For rotation, use a coordinated Caddy-environment/Worker-secret update and Caddy
restart. A mismatch fails closed with 403, so plan a brief registration maintenance
window; this candidate intentionally has no dual-secret rotation mechanism.
Remove superseded credentials only after both sides are verified. No registration
registry, ledger, application credentials or scientific data change is required.
