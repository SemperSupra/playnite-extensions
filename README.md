# Playnite Extensions

Public source and release destination for the SemperSupra family of Playnite extensions.

Development authority remains private. Only reviewed, public-safe source, qualification infrastructure, and release artifacts are promoted here.

## Public extensions

### Media Library Enrichment

Media Library Enrichment v0.1.0 source is promoted to `main` and release-qualified for Playnite 10.62 / API 6.16.0.

It provides conservative enrichment for supported non-game media represented in Playnite, including ownership-aware categories, shelves, covers, and non-Play Read/Listen actions, with user-override preservation and rollback.

See:

- `plugins/media-library-enrichment/README.md`
- `plugins/media-library-enrichment/RELEASING.md`
- `plugins/media-library-enrichment/InstallerManifest.yaml`

Binary publication is performed only from an exact qualified public revision.

## Other family extensions

Additional independently releasable family members remain under development or controlled projection, including:

- Playnite Auto Report
- Game Augmentation

Their presence in the private family inventory does not imply that their implementation or a public release has been promoted here.

## Release model

Public releases use Playnite's native SDK, Toolbox packaging, extension installer, and installer-manifest model. The repository does not maintain a parallel package manager or updater.
