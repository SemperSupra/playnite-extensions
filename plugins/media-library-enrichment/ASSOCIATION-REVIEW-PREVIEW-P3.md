# P3 local review-only association Preview — staging contract

Authority: public issue #18. Stacked on P2 PR #17 and P1 PR #15; dependent on P2 exact-head Windows native acceptance.

## User-facing workflow

The extra command **Preview local media association suggestions (read-only)** is in the existing Media Library Enrichment menu. It asks the user to select one folder each time, using Playnite's native SelectFolder dialog; Cancel returns without scanning.

Only immediate supported media filenames are inventoried from that folder (bounded at 4,096 entries by default) with no recursion, content reads or startup scans. P2's deterministic planner cross-checks the current admitted Playnite observation snapshot and proposes only unique exact stem/title matches. A local-only text dialog shows candidate counts, suppressions and at most 12 review-only title/file examples (max 72 displayed characters per field, control characters neutralized). Over-limit suggestions are counted, not expanded. The selected folder's absolute path and Playnite GUIDs are never displayed.

## Explicit non-goals

No mutation of Playnite objects, library database, source, cover, category, action, filter preset, evidence or ownership ledgers. No file writes, copies, hashes, recursive indexing, watcher, downloads, network calls, auto-approval, automated matching or Apply integration. **Displayed suggestions are not positive identity evidence.** Any future association grant requires explicit user selection, an exact live game identity and newly verified file evidence under a distinct authority contract.

No real names, IDs or file paths in public CI logs/artifacts, durable task records or outbound telemetry. The public test harness uses synthetic filenames only. The method that includes names is callable by the local UI; do not serialize its returned string into public evidence.

## Adversarial qualification

- No folder selection -> no filesystem enumeration.
- Whole-volume/relative/UNC/reparse-point rooted or ancestor-junction path -> fail closed.
- Duplicated game titles or IDs, invalid admission, mixed media kinds and untrusted filename paths -> no proposals.
- Native 10.62 fixture seeder asserts the six menu commands, builds a positive synthetic match to a known admitted game, counts one suggestion, displays no Playnite GUID and causes no managed library mutation.
- Hosted Windows xUnit covers planner ambiguity, no-admission, invalid file identity and resource caps; native build/package/seeder and menu receipt all pass at the exact head.
- Native modal picker interaction/keyboard accessibility remains separate GUI acceptance, not inherited from reflection-based fixture tests.
- Since this is stacked development, record the exact P2 and P1 base revisions before any integration/merge.

Hard resource bounds: $0, public standard hosted cloud only, never private GitHub-hosted Actions, owner's local/sovereign runner or paid compute. No merge or release without independent review.
