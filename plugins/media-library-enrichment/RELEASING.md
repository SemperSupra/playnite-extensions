# Media Library Enrichment v0.1.0 release

## Identity

- Add-on ID: `4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1377`
- Version: `0.1.0`
- Required Playnite API: `6.16.0`
- Release tag: `media-library-enrichment-v0.1.0`
- Release asset: `4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1377_0_1_0.pext`

## Qualified baseline

The product candidate was qualified on Playnite 10.62 with two independent exact-head native reps.

The product-head qualification at `2d88b4cf6cacb624e42a701ea12da7fdb5e03252` produced package SHA-256:

`897e1e1a97c545c41c0da606776f96f8832b14c0fd0118074d7c246c92e4a938`

That hash is historical evidence for that exact Git revision, not a cross-commit release invariant. .NET build provenance can change PE/debug identity when the repository revision changes even when product source blobs are unchanged.

The authoritative release rule is therefore:
1. release-only changes must not alter MLE product source or `extension.yaml`;
2. the exact final public release revision must pass the complete native qualification suite;
3. two independent builds of that exact revision must produce the same `.pext` SHA-256;
4. that exact SHA-256 is published beside the release asset.

If exact-head package hashes disagree, stop and diagnose before publication.

## Public release

The public repository is the source, release, and installer-manifest home. The release workflow must:

1. build from the exact selected public revision;
2. run the native MLE qualification suite;
3. package using the Playnite 10.62 Toolbox;
4. prove two exact-revision package builds are byte-identical;
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
