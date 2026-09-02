# StreamVue entitlement verifier Worker

This package is the reviewed HTTPS hosting adapter for `@streamvue/entitlement-verifier`. It is intentionally safe to build but impossible to mistake for a production deployment:

- `workers.dev` and preview URLs are disabled.
- No route or custom domain is committed.
- Hostnames and seller product IDs use `.invalid` or `REPLACE_` values.
- Google, Samsung, and rate-limit secrets are declared with Wrangler `secrets.required`; no secret value is stored in source or configuration.
- Staging and production have separate per-purchaser and provider-wide rate-limit namespaces.
- The Worker checks the exact request host, rejects any unapproved browser `Origin`, limits bodies to 32 KiB, caps provider traffic, and separately rate-limits an HMAC of the purchaser identity instead of retaining or exposing the raw purchase token or Samsung customer ID.
- Logs contain only a generic event, route, status, and deployment label. Request bodies, purchase identifiers, secrets, provider responses, and HMAC keys are never logged.

Samsung Smart TVs do not support the CORS `Origin` request header, so the signed television client must remain able to make an origin-less request. `ALLOWED_BROWSER_ORIGINS` is only for an exact HTTPS browser origin that an operator deliberately adds; it never accepts `*` or `null`. CORS is not treated as authentication. Provider verification plus the rate limiter remain the security boundary. See Samsung's [Security Q&A](https://developer.samsung.com/smarttv/develop/faq/security.html) and Cloudflare's [Rate Limiting binding](https://developers.cloudflare.com/workers/runtime-apis/bindings/rate-limit/).

## Local verification

From the repository root:

```text
pnpm entitlements:build
pnpm verifier-worker:types
pnpm verifier-worker:check
```

The check verifies Wrangler-generated bindings, TypeScript, Workers-runtime tests, and a local `wrangler deploy --dry-run`. It does not contact a Cloudflare account or publish the Worker.

To run `wrangler dev`, copy `.dev.vars.example` to `.dev.vars` and replace every local-only value. `.dev.vars*` and `.env*` are ignored by Git except for the example. Never use production credentials in a local file.

## Deliberate staging setup

Do not deploy until the StreamVue owner has created the exact Play Console and Samsung Seller Office/DPI records.

1. Replace the staging `.invalid` hostname, Google product ID, Samsung Checkout application ID, and Samsung product ID in `wrangler.jsonc`. Keep Google test purchases disabled unless the staging environment is isolated for licensed test buyers.
2. Choose both rate-limit `namespace_id` values so they are unique within the Cloudflare account. The 30/minute purchaser binding and 600/minute provider-wide binding are layered abuse controls and are deliberately not used as purchase accounting.
3. Add an exact staging custom-domain route. Keep `workers_dev` and `preview_urls` disabled.
4. From `packages/entitlement-verifier-worker`, set every required secret for staging interactively:

```text
pnpm exec wrangler secret put GOOGLE_SERVICE_ACCOUNT_EMAIL --env staging
pnpm exec wrangler secret put GOOGLE_SERVICE_ACCOUNT_PRIVATE_KEY --env staging
pnpm exec wrangler secret put SAMSUNG_DPI_SECURITY_KEY --env staging
pnpm exec wrangler secret put RATE_LIMIT_KEY_SECRET --env staging
```

`RATE_LIMIT_KEY_SECRET` must be an independent, randomly generated value of at least 32 characters. The Google service account must have only the minimum Play Console purchase-verification access. The Samsung key must be the DPI security key for the exact Checkout application.

5. Re-run `pnpm verifier-worker:check`, then review `pnpm exec wrangler deploy --env staging --dry-run` before any real staging deployment.
6. After deployment, confirm `GET /healthz`, completed and pending Play purchases, Samsung available and unavailable countries, restore, cancellation, refund revocation, rate limiting, and identifier-free logs.

## Production remains locked

Production repeats the staging setup with a separate hostname, route, rate-limit namespace, and four separately configured secrets using `--env production`. Test purchases cannot be enabled in production. A real deployment command is intentionally not part of repository automation yet.

Only after the deployed URL, secrets, provider permissions, privacy/retention review, rate limiter, real purchases, and refund tests have evidence should `store/premium-verifier-readiness.json` change. `edgeRateLimitConfigured` means the production binding was observed working; committed source code alone does not satisfy it.
