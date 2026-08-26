# StreamVue television design system

The Samsung/LG shell evolves StreamVue's established dark broadcast interface into a ten-foot, remote-first surface. The generated concept established the three-column composition and the implementation was verified at both vendor-supported graphics sizes.

## Reference artifacts

- Generated concept: `streamvue-tv-shell-concept-v2.png`
- Final production 1920×1080 render: `streamvue-tv-shell-production-1920x1080.png`
- Final 1280×720 render: `streamvue-tv-shell-render-1280x720-v2.png`
- Standalone broadcast preview: `../../platforms/tv-web/public/assets/broadcast-preview.png`

## Locked visual system

| Token | Value | Role |
| --- | --- | --- |
| Background | `#020912` | Full television canvas |
| Deep background | `#01060c` | Top and bottom chrome |
| Surface | `#06111d` | Rails and channel rows |
| Border | `#243548` | Quiet structural separators |
| Text | `#f3f7fb` | Primary ten-foot copy |
| Muted | `#99aac1` | Groups and secondary information |
| Accent | `#31dedc` | Focus, active group, and primary action |
| Live | `#ff405a` | Live status only |

Typography uses Segoe UI/Roboto/system sans, with minimum rendered copy above LG's 20 px FHD recommendation. Corners remain modest at 5–14 px. Focus uses an unmistakable 4 px turquoise inset ring and never depends on hover.

## Component and focus model

- Quiet top bar: brand, page title, privacy-safe refresh state, Search, and Source.
- Group rail: exact source order, active turquoise edge, channel count.
- Channel rail: five visible FHD rows or four visible HD rows, windowed around selection; exact group section labels appear in All Channels.
- Detail surface: generated neutral broadcast artwork, real channel identity, primary Watch action, and private Favorite state.
- Playback: full-screen native surface, auto-hiding chrome, real buffering state, error/warning message, and seven aspect modes.
- Modal focus is trapped to Search or Source controls. Back closes playback/modals first and otherwise yields to LG or exits through Samsung's application API.

## Fidelity ledger

| Comparison | Concept evidence | Render result |
| --- | --- | --- |
| Three-column composition | Group rail, channel rail, dominant preview | Matched at 1920×1080 and proportionally adapted at 1280×720 |
| Color hierarchy | Near-black navy, slate lines, turquoise focus, coral live | Matched with locked CSS tokens |
| Typography | Large geometric brand, channel names, and detail title | Matched with system-safe television fonts and room-scale sizing |
| Focus treatment | Bright rectangular selected-channel ring | Matched with 4 px inset focus and deterministic initial channel focus |
| Container model | Open rails rather than nested card grid | Matched; only preview, controls, and modal require borders |
| Remote hints | OK, vertical browse, left groups, Back exit | Matched in a persistent bottom strip |
| Preview treatment | Dark broadcast-world frame without UI text baked in | Recreated as a standalone generated asset under live HTML controls |

The first render exposed a Search-first focus order and a notice overlapping Favorite. Both were corrected before the final render. The HD pass also reduced the channel window from five rows to four so no selectable row can be clipped.
