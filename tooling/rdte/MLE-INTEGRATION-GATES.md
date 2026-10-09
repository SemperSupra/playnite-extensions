# MLE integrated diagnostic qualification

Source parent: Jules MLE classification PR #10. Added anonymous evidence checks from PR #11 and read-only directory-inventory source and tests from PR #12. This is a candidate for combined qualification, not an accepted release.

Keep raw Playnite backups, identifying observations, and private DLE out of public workflows. Do not modify live game metadata, invoke local hosts, or use private GitHub Actions. Runtime acceptance requires successful exact-head Playnite Windows RDTE and the Python fixture suite. Local directory inspection remains API-only, without a UI or mutating association.

The inventory must reject directory symlinks, including ancestor reparse points; must be explicitly rooted, top-level only, and capped. Classifier inference from the provider/source label is prohibited. No automated merge or release.
