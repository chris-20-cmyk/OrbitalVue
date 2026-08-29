# Accessibility validation

StreamVue uses WCAG 2.2 Level AA as an engineering baseline where it applies, together with each platform's native accessibility behavior. This is a target and test plan, not a public conformance claim. `store/accessibility-readiness.json` stays fail-closed until the owner approves the scope and a named tester records real assistive-technology evidence for the exact release candidate.

Run the source and evidence contract from the repository root:

```text
pnpm accessibility:check
```

The automated check protects improvements already present in source: visible Windows keyboard focus, useful UI Automation names, Android live-status and selection semantics, Apple playback labels and reduced-motion behavior, semantic television HTML, contained modal focus, remote navigation, and reduced-motion CSS. Automated source checks cannot establish accessibility conformance.

## Required release walkthroughs

| Platform | Manual evidence before `ready: true` |
| --- | --- |
| Windows | complete every action by keyboard; inspect with Narrator; verify 200% display scaling and Windows contrast themes; exercise play, pause, seek, volume, ratio, fullscreen, errors, and recovery |
| Android / Google TV | complete TalkBack and Switch Access paths; verify 200% font scale; use only a TV remote on Android TV; exercise playback and error recovery |
| iPhone / iPad / Apple TV | complete VoiceOver, Voice Control, and Switch Control paths; test the largest Dynamic Type size plus Reduce Motion/Transparency; complete an Apple TV remote-only walkthrough; exercise playback and errors |
| Samsung TV | use only the remote, then repeat with Voice Guide; verify screen zoom/contrast behavior; test playback controls, errors, and recovery on a real supported TV |
| LG webOS TV | use only the remote, then repeat with Audio Guidance where supported; verify screen zoom/contrast behavior; test playback controls, errors, and recovery on a real supported TV |

Record the tested app version, date, tester, and durable evidence references. Evidence may point to private test records; do not commit a tester's personal data, screen-reader speech logs containing playlist URLs, provider credentials, tokens, or private media names.

## Media boundary

StreamVue supplies a player and interface, not the user's media. The app must expose its own controls, states, and errors accessibly. Captions, alternate audio, descriptive audio, loudness, flashing content, and source metadata depend on the authorized media source and must be described as provider-supplied capabilities rather than guaranteed StreamVue features.

## Primary guidance

- [Microsoft: Accessibility overview for Windows apps](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-overview)
- [Android: Compose semantics](https://developer.android.com/develop/ui/compose/accessibility/semantics)
- [Apple: SwiftUI accessibility fundamentals](https://developer.apple.com/documentation/swiftui/accessibility-fundamentals)
- [W3C: Modal dialog pattern](https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/)
- [W3C: WAI-ARIA overview](https://www.w3.org/WAI/standards-guidelines/aria/)

Vendor accessibility behavior varies by television model, operating-system version, locale, and enabled assistive feature. Re-run the relevant physical-device matrix for each public candidate and document exceptions before approving a conformance claim.
