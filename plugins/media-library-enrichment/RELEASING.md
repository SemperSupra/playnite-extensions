# Media Library Enrichment v0.1.0 release

## Identity

- Add-on ID: `4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1377`
- Version: `0.1.0`
- Required Playnite API: `6.16.0`
- Release tag: `media-library-enrichment-v0.1.0`
- Release asset: `4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1377_0_1_0.pext`

## Qualified baseline

The product candidate was qualified on Playnite 10.62 with two independent exact-head native reps.

Qualified package SHA-256:

`897e1e1a97c545c41c0da606776f96f8832b14c0fd0118074d7c246c92e4a938`

Release preparation must not change the package bytes. If the package hash changes, stop and requalify the new package before publication.

## Public release

The public repository is the source, release, and installer-manifest home. The release workflow must:

1. build from the exact selected public revision;
2. run the native MLE qualification suite;
3. package using the Playnite 10.62 Toolbox;
4. require the qualified SHA-256 above;
5. upload the package as a workflow artifact;
6. create tag/release `media-library-enrichment-v0.1.0` only when publication is explicitly requested.

No binary is committed to source and no private GitHub Actions minutes are required.

## Official Playnite distribution

Prepared submission manifest:

`distribution/PlayniteAddonDatabase.yaml`

Intended destination when plugin intake is open:

`JosefNemec/PlayniteAddonDatabase/addons/generic/SemperSupra_MediaLibraryEnrichment.yaml`

The official database is an index. It references our stable public `InstallerManifest.yaml`, which references the GitHub Release `.pext`.

As of 2026-10-07, the existing Playnite Addon Database has paused new plugin submissions while a new submission/verification system is being prepared for Playnite 11 and planned for backport to Playnite 10. Revalidate the official process immediately before submission.
