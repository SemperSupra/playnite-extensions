#!/usr/bin/env python3
"""Emit bounded anonymous MLE counters from a local observation receipt.

Never prints names, GUIDs, provider IDs, notes, local paths or URLs.
"""
import argparse
from collections import Counter
import json
from pathlib import Path

KINDS = {"book", "comic", "audio", "unresolved"}
REASONS = {"SUPPORTED_MANUAL_EXTENSION", "SUPPORTED_NOTES_EXTENSION",
           "NO_LOCAL_EVIDENCE", "NO_SUPPORTED_MEDIA_EXTENSION"}
FIELDS = {"manual", "notes", "description", "install-directory",
          "links", "game-actions", "roms"}

# Exact compatibility aliases for Jules PR #10. Unknown labels are never copied to output.
REASON_ALIASES = {
    "supported Manual extension": "SUPPORTED_MANUAL_EXTENSION",
    "supported Notes extension": "SUPPORTED_NOTES_EXTENSION",
    "no local evidence": "NO_LOCAL_EVIDENCE",
    "no supported extension": "NO_SUPPORTED_MEDIA_EXTENSION",
}

def summarize(doc):
    if doc.get("Schema") not in (None, "sempersupra-media-library-enrichment-observation/v1"):
        raise ValueError("unexpected observation receipt schema")
    candidates = doc.get("Candidates")
    if not isinstance(candidates, list) or not isinstance(doc.get("CandidateCount"), int):
        raise ValueError("invalid candidate list/count")
    if len(candidates) != doc["CandidateCount"] or len(candidates) > 100000:
        raise ValueError("candidate count mismatch or excessive records")
    kinds, reasons, fields = Counter(), Counter(), Counter()
    missing_cover = no_variants = 0
    for item in candidates:
        if not isinstance(item, dict):
            raise ValueError("invalid candidate")
        kind = item.get("Kind")
        kinds[kind if isinstance(kind, str) and kind in KINDS else "other"] += 1
        reason = item.get("ClassificationReason")
        canonical = REASON_ALIASES.get(reason, reason) if isinstance(reason, str) else None
        reasons[canonical if canonical in REASONS else "not-recorded-or-unknown"] += 1
        present = item.get("EvidenceFieldsPresent")
        if present is None:
            present = []
        if not isinstance(present, list) or any(not isinstance(field, str) for field in present):
            raise ValueError("invalid evidence fields")
        for field in set(present):
            fields[field if field in FIELDS else "other"] += 1
        missing_cover += item.get("CoverMissing") is True
        no_variants += not bool(item.get("LocalEvidenceNames")) and not bool(item.get("LocalEvidenceName"))
    return {"schema":"mle-anonymous-coverage/v1", "candidate_count":len(candidates),
            "kinds":dict(sorted(kinds.items())),
            "classification_reasons":dict(sorted(reasons.items())),
            "evidence_fields_present":dict(sorted(fields.items())),
            "missing_covers":missing_cover, "no_recorded_local_variants":no_variants}

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("receipt", type=Path)
    args = p.parse_args()
    if args.receipt.stat().st_size > 128 * 1024 * 1024:
        raise SystemExit("receipt exceeds allowed file size")
    data = json.loads(args.receipt.read_text(encoding="utf-8"))
    print(json.dumps(summarize(data), indent=2, sort_keys=True))

if __name__ == "__main__":
    main()
