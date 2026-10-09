# P4b — local file evidence preflight (read-only)

**Status:** Source staged for independent public Windows synthetic/native qualification. Parent is P4a PR #21. Tracking issue #22.

## Purpose

Before any future owner-approved binding, we must know whether a review-only suggestion still refers to exactly the immediate file or files the user inspected. `MediaLocalFileEvidencePreflight.Inspect` combines a P4a proposal fingerprint with the existing P1 top-level local inventory and checks that each supported basename resolves exactly to a nonempty, non-reparse file. It returns an **ephemeral, owner-local** snapshot of exact canonical root, safe basename, file length and `LastWriteTimeUtc` ticks; `MatchesFreshEvidence` reruns the bounded inspection and returns false if any of those observations or the proposal fingerprint changed.

## Trust and authority boundaries

- This is NOT a durable proof of file content, signature, credential, proof of review, anti-TOCTOU handle or authorization to mutate any game. Metadata can be deliberately reused, and a file can be replaced between observation and a later action.
- A caller supplies the P4a opaque root-binding token, but P4b does not authenticate or create that token. The canonical root path is retained only in local memory and **must not be put in public CI artifacts, logs, DLE or telemetry**.
- Future Apply/association is a distinct architecture and owner authorization gate requiring an explicit per-file UI decision, approved exact Playnite identity, fresh secure file validation, ownership ledger, action bounded to approved fields and rollback.
- A future actor must not pass this snapshot directly into the existing Playnite Apply mode and call it consent. There is no integration with Apply/Observe/Rollback.

## Bounded, fail-closed behavior

1. Requires an explicit non-rooted-in-network local folder; P1 rejects relative, drive-relative, volume, UNC, reparse roots and ancestor junctions.
2. Top-level only, max 4,096 entries. No recursion, auto-discovery, monitors, watcher, download or external network request.
3. P4a enforces exact REVIEW_REQUIRED proposal/Playnite GUID/rule/kind and at most 64 safe bare filenames, no duplicate variants, paths, controls, or Unicode bidi/zero-width format spoofing.
4. Each entry must exactly match an observed supported filename/case and media kind. If missing, empty, renamed, linked, or ambiguous, the check fails closed.
5. Returns no partial valid result after an error. Comparison fails closed for changed size, last-write time, root, filename list or proposal identity.
6. Only reads directory/file metadata; does not open media content and does not change a Playnite object or disk file.

## Synthetic qualification

Public standard GitHub-hosted Windows: exact-head C# xUnit, native Playnite 10.62 package, seeded runtime/menu regression. Unit tests include valid and empty files, metadata drift, missing/renamed file, distinct root-binding/identity, variant order, bad roots and invalid review disposition. Only generated temporary synthetic filenames may enter public logs.

## Risks requiring later mitigation

- **TOCTOU:** Metadata snapshots do not pin file handles. Later actuator must re-open safely under an authenticated root, detect links and parent swaps, verify content identity when warranted, re-read the Playnite identity and compare to the owner-approved review receipt before any mutation.
- **Spoofing:** Current filename extensions identify *hints*, not media authenticity; a malicious actor can keep size and timestamp unchanged.
- **Consent:** A fingerprint or `MatchesFreshEvidence == true` is **never approval**. The owner-local review UI and decision artifact are separate development tasks.
- **Scale/availability:** >4,096 entries, missing media or inaccessible folders produce a failure or no proposal, not partial acceptance.
- **Privacy:** All real roots/names must stay on the user's local system. Public runners use synthetic inputs only.
- **Distributed authority:** No sidecar escalation or new system integration until exact source and actors are independently accepted.

**Resource constraint:** $0, public standard GitHub-hosted runners only; no private hosted Actions minutes, local/sovereign/self-hosted or paid fallback, automatic PR merge or release.

**Ultimate acceptance:** Real user library enrichment is still unverified. The historical unresolved Humble observations require owner-selected physical media and explicit mapping before any owned changes.
