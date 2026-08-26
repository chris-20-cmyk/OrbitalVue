# StreamVue portable catalog contract

Contract `1.0` is the stable boundary between StreamVue platform clients. It preserves the playlist behavior that already exists on Windows without requiring the Windows UI or playback engine on Android, TV, or Apple devices.

The contract covers source identity, channel groups, guide matching, request headers, catch-up metadata, and stable channel IDs. Source display locations must be safe labels and must never expose credentials or playlist tokens.

Files in `fixtures/` are synthetic and use the reserved `.invalid` domain. They are parser conformance inputs, not real channels or distributable content.

Run the dependency-free validation from the repository root:

```text
node contracts/validate-contract.mjs
```

The Samsung/LG implementation lives in `packages/catalog-js` and runs the same fixture through its TypeScript parser:

```text
pnpm catalog:test
```

Breaking changes require a new major contract version. New optional fields can be added in a minor revision after all shipping clients ignore unknown fields safely.
