# Release readiness report

The release manifests are intentionally strict, but their individual booleans are not a convenient project view. Generate one human-readable and one machine-readable dossier from the repository root:

```text
pnpm release:report
```

The command writes ignored build output to `artifacts/release-readiness/`:

- `release-readiness.md` groups remaining evidence by platform and gate.
- `release-readiness.json` provides stable fields for later dashboards or release tooling.

The report covers premium commerce, Android/Samsung verifier deployment, privacy, Store copy/assets, accessibility, and platform distribution. Verifier source code is checked separately from production hosting, secret management, provider access, rate limiting, privacy review, and real purchase/refund evidence. LG's free, premium-locked release is shown as a valid no-commerce lane; it is not incorrectly blocked on a nonexistent LG premium product. Shared owner decisions appear in each affected platform so a platform cannot look ready in isolation.

The **Cross-platform Store release contract** workflow uploads the dossier as a temporary CI artifact after all structural verifiers pass. It is not a release asset and nothing in the report generator signs, publishes, or uploads an application candidate.

Treat the report as a truthful checklist. A missing seller identity, URL, product, legal approval, image, certificate review, or real-device test remains blocked until the owner has actual evidence. Never change a readiness value solely to produce a green report.
