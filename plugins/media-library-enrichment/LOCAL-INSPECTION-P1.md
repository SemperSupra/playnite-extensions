# Opt-in local inventory Preview (experimental P1)

**Status:** Staged for independent native Playnite validation; not merged or released.

## User flow
Under `@Media Library Enrichment`, choose `Inspect selected directory (read-only)`. A native Windows folder selector appears. Selecting Cancel, closing the dialog, or declining to choose a folder performs no scan. Selecting a directory calls the `MediaFileInventory` core primitive and shows a local aggregate-only result. It does not associate files to games and cannot add categories, actions or covers.

## Bounded behavior
- No automatic scan at launch or during Apply/Observe/Rollback.
- No default directory selected, no recursive traversal, no reads of file contents.
- Only explicitly selected absolute local folder, no drive/UNC roots or symlink/junction ancestors.
- Hard cap 4,096 entries by default; on exceeding cap, fail closed without returning partial candidates.
- No paths or filenames written to receipts or GitHub CI artifacts; local UI shows aggregate counts only.
- The user may see only number examined, recognized-kind totals and skipped reparse points.
- No media matching, mutation, metadata/network enrichment, or staged application.
- Success in synthetic Windows tests is not proof that any personal Humble media files were discovered.

## Red/blue-team review
- **Unsolicited access:** no call from startup, scheduled task or general Apply; explicit menu click and folder selection only.
- **Symlink containment:** reject the entire selected directory ancestry; skip reparse file entries, and do not descend.
- **Time-of-check/use:** directory inventory is a read-only hint. No downstream mutation may trust this result without fresh explicit identity/path verification.
- **Privacy:** control-character or private filenames never appear in the public artifact; only fixed kind labels and counts are presented in-process. Error dialog emits exception type only.
- **Scope creep:** no matching or user-facing Apply permissions in this tranche.

## Qualification
1. Compile/package/install against the disposable Playnite 10.62 Windows runtime on public standard GitHub runner.
2. Test core inventory synthetic coverage, upper bounds and directory-junction/ancestor rejection on exact revision.
3. Confirm new menu item exists. A full interactive picker click-through may require a specialized GUI actor; do not mark it PASS from compilation alone.
4. Confirm mode remains Observe and no database or ledger mutation follows read-only inspection.
5. No merge/release without reviewed evidence and exact source provenance.

**Resource contract:** $0, no private GitHub-hosted Actions, no self-hosted or sovereign fallback. This feature branch is based on the combined diagnostics branch; reconcile that exact base before publication.
