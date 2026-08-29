# Store listing assets

Place reviewed, final listing artwork in a platform folder and record each relative path in `store/store-listing.json`:

- `windows/`
- `android/`
- `apple/`
- `samsung/`
- `lg/`

Screenshots must come from the matching current build on the named device class. Use only content the tester owns or is authorized to display. Remove provider names, playlist addresses, account details, notifications, unrelated brands, and other personal information before committing an image.

Do not substitute concept art or AI-generated interface screens for actual product screenshots. Marketing artwork may be designed separately, but it must not imply unavailable content, store approval, rankings, pricing, or unsupported features.

Run `pnpm listing:check` after adding or replacing any asset. The validator checks the committed path, image format, dimensions, alpha rules, and size limits for the selected Store lane; the human `reviewed` flags remain required for composition, trademark, content-rights, accessibility, and real-device accuracy.
