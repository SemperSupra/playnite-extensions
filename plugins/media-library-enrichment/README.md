# Media Library Enrichment

Provider-aware enrichment for non-game media and game-adjacent extras represented in Playnite.

Humble is the first adapter and acceptance source. The product is provider-neutral: later adapters are earned only when evidence and fixtures justify them.

## Current executable slice

The current vertical slice is intentionally observation-only:

- load as a real Playnite 10 GenericPlugin;
- inspect the Playnite library through the supported SDK;
- identify Humble-sourced records with missing cover artwork;
- classify obvious local document/audio variants conservatively;
- emit a deterministic machine-readable observation receipt under this extension's `ExtensionsData` directory;
- make no Playnite/library/media-file mutations.

This slice exists to qualify the second real family plugin through the native build/package/install/restart/uninstall RDTE path before artwork mutation, UI, rollback, provider networking, or shared framework extraction is introduced.

Product authority: issue #21.


## Installation and updates

Release artifact:

`4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1377_0_1_0.pext`

For an independent GitHub release, download the `.pext` asset and install it through Playnite's native extension installer. The release package is built with Playnite Toolbox and is expected to have SHA-256:

`897e1e1a97c545c41c0da606776f96f8832b14c0fd0118074d7c246c92e4a938`

The public `InstallerManifest.yaml` is maintained for Playnite Add-on Browser/update compatibility. Once official new-plugin intake is available and the add-on is accepted into the Playnite Addon Database, the direct install URI will be:

`playnite://playnite/installaddon/4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1377`

Release procedure and official-distribution handoff are documented in `RELEASING.md`.
