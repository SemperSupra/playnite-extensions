# P4a — Staged review proposal identity token

Public issue #20. This is a **pure, local, side-effect-free change-detection utility**, not an authorization/approval mechanism, media association, or mutation implementation. It is staged after P3 and **must not be qualified or promoted while P3 native Windows RDTE is active**.

## Output

`MediaAssociationProposalFingerprint.Compute(suggestion, localRootBinding)` deterministically hashes a versioned length-prefixed encoding of the Playnite GUID, exact review-only rule, media kind, exact sorted filename variants, and an externally supplied 32-byte opaque root-binding token. The output is a 64-character hex SHA-256 digest. No filename/path/title/ID appears in the digest itself. It can be used to detect when a locally observed proposal differs from a previously reviewed snapshot; changing the selected root, record ID, filename, kind, rule or disposition invalidates it.

The root-binding token must be produced by a **separate trusted owner-local component** using appropriate location identity and a private root-scoped key. This primitive does not create or certify that binding. A digest is **not** a signature, proof of user presence, proof of local file contents, or permission to Apply.

## Fail-closed rules
- Only `REVIEW_REQUIRED` from the exact-stem P2 rule, with valid Playnite GUID and book/comic/audio kind.
- 1–64 safe bare supported filenames; no paths, UNC, drive prefix, duplicates, control characters or mismatched extension/media kind.
- Exactly 64 hex characters for opaque root binding. No automatic default root, global namespace, remote queries or local discovery.
- The checker returns false for invalid/stale fingerprints or invalid live candidates. It performs no I/O and changes no library fields.
- Explicitly forbid any call from existing Apply/Observe/Rollback: future reviewed actuation needs a different capability grant with owner identity and fresh file/Playnite state validation.

## Adversarial tests
- Variant ordering does not change digest; altered file, root or game identity does.
- Invalid root tokens, unknown rules, misleading `APPROVED_FOR_APPLY` disposition, mixed types, duplicate basenames, traversal, drive paths, control chars and over-cap files all fail closed.
- No private media metadata in public CI, unit fixtures only.
- SHA-256 only supports snapshot consistency; this is not a secret-bound HMAC or an authorization signature.

## Acceptance and release constraints
Exact source SHA xUnit and native Windows Playnite RDTE, independent of P3 passes. Only after P3 corrected Unicode display test reaches terminal, and after the next heavyweight lane is clear, may P4a be placed in a draft PR and qualified with public hosted Windows. No private GHA minutes, local/sovereign fallback, paid compute, auto merge, release or real library acceptance.

Future work: true owner authorization, durable local ledger, provenance, stat/digest, TOCTOU checks, rollback and manual GUI click-through. None is implemented here.
