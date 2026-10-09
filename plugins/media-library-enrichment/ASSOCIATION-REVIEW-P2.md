# P2 read-only media association suggestions (staged only)

This is an **in-memory, review-required proposal engine**, not an action engine. Source: public issue #16. Branch derives from P1 PR #15, which derives from integrated PR #14. None of those layers may inherit another's CI acceptance.

## Problem
The historical private Playnite observation shows 3,171 admitted Humble records without classified local filenames. File inventory can expose local media candidates in an explicitly selected directory but cannot establish which Playnite record owns them. P2 is a narrow, deterministic way to offer some review suggestions while refusing ambiguous cases.

## Primitive
`MediaAssociationReviewPlanner.Build(admittedObservations, inventory)`

- Accepts only a previously validated/explicitly admitted Playnite observation snapshot and read-only local inventory.
- Suggests one review record only if the **trimmed game title equals the trimmed filename stem**, compared with ordinal case-insensitive equality. **No fuzzy, transliteration, punctuation, provider/source name or implicit download matching**.
- Accepts multiple variants such as `Title.epub` and `Title.pdf` as a *single* review bundle only when media kind is consistent.
- Rejects ambiguity: repeated game titles, repeated Playnite GUIDs, duplicate/case-colliding filenames and mixed-kind variants; rejects title-to-file kind disagreement.
- Requires valid Playnite GUID and nonblank preexisting admission producer/evidence fields. This does not manufacture independent admission; the caller remains responsible for authenticating the snapshot against current Playnite identity.
- Rejects file paths instead of bare filenames, unsupported extensions/kinds, control characters, and over-cap inputs. Files are never opened or modified.
- Every output says `REVIEW_REQUIRED`. No category, cover, action, favorite, source, association or ownership ledger mutation. No automatic Apply on review suggestions.
- Output carries Playnite ID and basename **only in memory on the user's system** to support later local review. Never upload suggestions/identifiers to public hosted CI, telemetry or task logs.

## Red-team and blue-team gates
1. **Name collisions / wrong media:** duplicated normalized titles or filenames and kind disagreements must never emit a suggestion; no attempt at approximate tie-breaking.
2. **Stale snapshot:** this primitive cannot authorize a file write or Playnite mutation. Later acceptance must re-read live Playnite identity, explicit source admission and current file location before any owner-affirmed action.
3. **Path spoofing:** selected-root inventory is the only legitimate path source; do not accept a path in place of a basename. A basename is not content validation or a verified file handle.
4. **Privacy:** synthetic tests only in public CI; never emit real title/ID/file names to public artifacts. UI consent and visibility are separate development nodes.
5. **Resource exhaustion:** at most 10,000 games and 10,000 supported filenames; no recursive or unbounded enumeration.
6. **No false acceptance:** exact-stem name equality only supplies a *review candidate*, not positive proof of provenance, license or actual content.

## Acceptance gates
- Core .NET xUnit deterministic tests for no-match, unique match, multi-format bundle, duplicate titles/GUIDs, kind conflict, path injection, unsupported files and oversized inputs.
- Public standard-hosted Windows 10.62 native build/package/fixture RDTE at the **exact source SHA**, scheduled only after P1 PR #15 has terminal qualified state; no private GHA minutes or sovereign/self-hosted fallback.
- Separate explicit local GUI review design and red/blue acceptance before any review UI or persisted association mapping is implemented.
- No merge/release or user-library success claim from this staged primitive.
