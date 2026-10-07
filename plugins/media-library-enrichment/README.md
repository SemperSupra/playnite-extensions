# Media Library Enrichment

Provider-aware enrichment for non-game media and game-adjacent extras represented in Playnite.

Humble is the first adapter and acceptance source. The product remains provider-neutral: later adapters are earned only when evidence and fixtures justify them.

## Qualified v0.1 behavior

The current candidate is no longer observation-only. It has qualified native Playnite 10.62 behavior for:

- conservative admission of Humble and explicitly evidenced media records;
- book, comic, and audio classification from supported local variants;
- owned media categories and native Filter Presets;
- non-Play `Read` / `Listen` actions backed by Playnite `GameActionType.File`;
- cover enrichment from explicit evidence;
- R4I reconciliation and exact ownership ledgers;
- user/tool override preservation across category, action, FilterPreset, and CoverImage;
- rollback that removes only still-owned state;
- normal uninstall that preserves enriched library state, plugin data, and local media files;
- active coexistence with Metadata Utilities 1.9.0;
- reproducible native build/package/install/restart/uninstall qualification;
- collector-scale qualification through 10,005 total Playnite records with exactly four intended candidates and no control leakage.

## Media activation

MLE does not bundle readers or media players and does not maintain its own file-extension-to-application registry.

Local media actions use Playnite's native non-Play `GameActionType.File` mechanism. Exact Playnite 10.62 runtime qualification proved that its native `ProcessStarter` delegates local files to the Windows registered/default handler.

The same unchanged Playnite actuator was qualified for PDF, EPUB, CBZ, audio, video, and image targets. URI dispatch through Playnite's native URL path was also qualified.

Modality determines semantics and metadata; target type determines dispatch. Specialized adapters are added only when a concrete target falsifies the native mechanism.

## Current scope

The v0.1 product policy currently materializes semantic enrichment for:

- books / ebooks;
- comics;
- audio / soundtracks.

The generic actuator is already capable of additional file modalities, but video/image/web admission and semantic policy are future product-policy work unless separately promoted into v0.1.

No event-driven reconciliation is currently registered; reconciliation occurs at application startup. A future event-driven implementation must earn its own coalescing/event-storm qualification.

Product authority: issue #21.
Execution/RDTE authority: issue #23.
Native target-dispatch qualification: issue #48 (completed).


## Installation and updates

Release artifact:

`4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1377_0_1_0.pext`

For an independent GitHub release, download the `.pext` asset and install it through Playnite's native extension installer. The package is rebuilt and fully requalified from the exact release revision; the release publishes its matching `.sha256` checksum alongside the package.

The public `InstallerManifest.yaml` is maintained for Playnite Add-on Browser/update compatibility. Once official new-plugin intake is available and the add-on is accepted into the Playnite Addon Database, the direct install URI will be:

`playnite://playnite/installaddon/4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1377`

Release procedure and official-distribution handoff are documented in `RELEASING.md`.
