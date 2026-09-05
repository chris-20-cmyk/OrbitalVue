# Guide, readability, and media-center UX repair

This branch addresses the September 2026 usability regressions reported against OrbitalVue 5.8 alpha:

- XMLTV guide refreshes that fall into the `needs attention` state when HTTP decompression and `.gz` payload handling disagree.
- UI typography and credential-entry controls that are too small to read comfortably on the target display.
- Media-center browsing that flattens Plex content too aggressively instead of preserving source/library hierarchy.
- Release-safety gaps around XAML/branding verifiers and legacy DVR recording-folder migration.

The work is intentionally split into small commits and kept off `main` until Windows CI and review are complete.
