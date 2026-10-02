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
