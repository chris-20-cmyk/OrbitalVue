# OrbitalVue public site fidelity ledger

The generated overview and privacy concepts are the visual specification for the static public-site foundation:

- `docs/design/orbitalvue-support-site-overview-concept.png`
- `docs/design/orbitalvue-support-site-policy-concept.png`

The implementation was inspected in the Codex in-app browser at the default 1280 × 720 viewport, a 1440 × 900 desktop viewport, and a 390 × 844 mobile viewport. Viewport screenshots and both source concepts were inspected at original detail with the local image viewer. Temporary QA screenshots were not retained in the repository.

| Comparison point | Concept intent | Implemented result |
| --- | --- | --- |
| Brand system | Near-black navy canvas, restrained teal signal color, white editorial type | Matched through the shared CSS color tokens, typography, borders, and focus treatment |
| Overview hierarchy | Large three-line promise beside a player composition | Matched with responsive editorial copy and a generic, rights-safe OrbitalVue player illustration |
| Privacy hierarchy | Persistent contents rail, readable article, at-a-glance rail | Matched at desktop; contents becomes a horizontal rail and the page becomes one column on mobile |
| Signal path | Device to chosen provider, with Store involved only for purchases | Matched and expanded with precise local-data and entitlement copy from the privacy inventory |
| Platform band | One coordinated support home across five platform families | Matched without claiming that unfinished Store releases are already available |
| Support transition | Clear help and release-status actions | Matched with an additional interactive troubleshooting rail and safe-reporting notice |
| Accessibility | High contrast, obvious navigation, usable controls | Added skip links, landmarks, current-page state, 44-pixel controls, visible focus, reduced-motion support, and keyboard-safe mobile navigation |
| Responsive behavior | Preserve hierarchy on smaller screens | Verified with no horizontal page overflow at 390 pixels after correcting CSS grid minimum sizing |

## Intentional deviations

- The concept's sample channel and player imagery was replaced by a custom generic interface so the public site does not imply licensed programming or a particular provider.
- Platform text says that foundations are in development instead of claiming Store availability.
- The policy includes the complete reviewed IPTV, Plex, Emby, purchase-verification, diagnostics, and deletion boundaries from `store/privacy-data-inventory.json`.
- The concept's `© 2024` sample was omitted because it is not an approved owner statement and is factually stale in 2026.
- Privacy and support retain visible draft notices until owner identity, a monitored privacy contact, legal review, deployment, and live URLs are verified.
