# Experimental local inventory — opt-in foundation only

This module is a **read-only library primitive**, not a media import feature.
Current authority: Playnite MLE functional recovery (private issue #59).
Implementation: `MediaFileInventory.InspectImmediateFiles(explicitRoot, maxEntries)`.

## What it does
- Requires an existing, explicitly chosen absolute directory.
- Refuses drive/volume roots, UNC/network roots, relative roots, and reparse-point directory roots.
- Enumerates **only immediate directory entries**, never follows nested directories.
- Skips symlink/reparse-point entries and never opens file contents.
- Counts every encountered entry (including unrelated files/directories) against a hard upper bound of 10,000.
- Fails closed without returning partial results if traversal exceeds the selected bound or an I/O error occurs.
- Returns recognized basename + media kind only, using the existing classifier, in stable order.

## What it does not do
- **No user-visible scan option** is wired yet.
- No automatic scan, filesystem watcher, daemon, broad search, hash/content inspection, or network operation.
- No matching a media filename to a Playnite title or Humble provider entry.
- No setting Category, CoverImage, Action, filter presets, or ownership ledgers.
- No claims that every matching extension contains valid media or belongs to an admitted game.
- No printing or uploading of paths or filenames to public CI artifacts.

## Red team / blue team
- Paths can be unexpected/hostile: reject non-absolute roots, volume/UNC and reparse roots, no recursive walk.
- Many entries may consume resources: hard cap includes directories and unsupported types; fail closed on overflow.
- Symlink files may point outside the chosen directory: skip reparse entries.
- Metadata/existence may change during enumeration (TOCTOU): this is **not** an authorization or stable mutation plan. Revalidate any later exact file actions against a fresh approved preview.
- File extensions can lie: treat discovered kind as *hint*, never proof that file content is safe/valid.
- Names and paths can contain private data: only synthetic file names in tests, no artifact export of real paths.

## Qualification gate
The new pure-core xUnit tests run using temporary synthetic folders. Public Windows-native MLE RDTE must pass at the exact PR revision. The owner-local capability remains NOT_READY until a separate UI root-selection and preview contract is implemented and qualified.
