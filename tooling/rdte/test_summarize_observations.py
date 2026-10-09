import json
from pathlib import Path
import unittest
from summarize_observations import summarize

class MleCoverageTests(unittest.TestCase):
    def test_only_nonidentifying_aggregates(self):
        doc = {"Schema":"sempersupra-media-library-enrichment-observation/v1",
               "CandidateCount":2,"Candidates":[
                 {"Kind":"unresolved","Name":"PRIVATE NEVER ECHO","PlayniteId":"PRIVATE","CoverMissing":True},
                 {"Kind":"book","ClassificationReason":"SUPPORTED_MANUAL_EXTENSION",
                  "EvidenceFieldsPresent":["manual","links"],"LocalEvidenceNames":["PRIVATE.pdf"]}]}
        output = summarize(doc)
        self.assertEqual(output["candidate_count"],2)
        self.assertEqual(output["no_recorded_local_variants"],1)
        self.assertNotIn("PRIVATE",str(output))
    def test_mismatch_fail_closed(self):
        with self.assertRaises(ValueError):
            summarize({"CandidateCount":1,"Candidates":[]})
    def test_unknown_field_not_echoed(self):
        output = summarize({"CandidateCount":1,"Candidates":[
            {"Kind":"unresolved","ClassificationReason":"PRIVATE","EvidenceFieldsPresent":["PRIVATE"]}]})
        self.assertEqual(output["evidence_fields_present"],{"other":1})
        self.assertEqual(output["classification_reasons"],{"not-recorded-or-unknown":1})

    def test_synthetic_receipt_causal_coverage(self):
        path = Path(__file__).resolve().parent / "fixtures" / "mle-synthetic-observation.json"
        doc = json.loads(path.read_text(encoding="utf-8"))
        out = summarize(doc)
        self.assertEqual(out["candidate_count"], 6)
        self.assertEqual(out["kinds"], {"audio":1, "book":1, "comic":1, "unresolved":3})
        self.assertEqual(out["classification_reasons"], {
            "NO_LOCAL_EVIDENCE":2,
            "NO_SUPPORTED_MEDIA_EXTENSION":1,
            "SUPPORTED_MANUAL_EXTENSION":1,
            "SUPPORTED_NOTES_EXTENSION":2
        })
        self.assertEqual(out["missing_covers"], 4)
        self.assertEqual(out["no_recorded_local_variants"], 3)
        self.assertEqual(out["evidence_fields_present"], {
            "description":1, "game-actions":1, "links":1,
            "manual":1, "notes":3, "roms":1
        })
        for prohibited in ("Name", "PlayniteId", "ProviderGameId", "Notes", "Manual"):
            self.assertNotIn(prohibited, json.dumps(out))
    def test_malformed_presence_type_fails_closed(self):
        with self.assertRaises(ValueError):
            summarize({"CandidateCount":1, "Candidates":[{
                "Kind":"unresolved", "EvidenceFieldsPresent":{"manual": "x"}
            }]})

    def test_jules_display_labels_normalized_to_stable_codes(self):
        examples = [
            ("supported Manual extension", "SUPPORTED_MANUAL_EXTENSION"),
            ("supported Notes extension", "SUPPORTED_NOTES_EXTENSION"),
            ("no local evidence", "NO_LOCAL_EVIDENCE"),
            ("no supported extension", "NO_SUPPORTED_MEDIA_EXTENSION")
        ]
        doc = {"CandidateCount": len(examples), "Candidates": [
            {"Kind": "unresolved", "ClassificationReason":label}
            for label, _ in examples
        ]}
        out = summarize(doc)
        self.assertEqual(
            out["classification_reasons"],
            {stable: 1 for _, stable in examples})
        self.assertNotIn("supported Manual extension", json.dumps(out))

    def test_opaque_reason_and_kind_do_not_leak(self):
        doc = {"CandidateCount": 1, "Candidates": [{
            "Kind": {"private": "should-not-echo"},
            "ClassificationReason": ["private-should-not-echo"]
        }]}
        out = summarize(doc)
        self.assertEqual(out["kinds"], {"other": 1})
        self.assertEqual(
            out["classification_reasons"], {"not-recorded-or-unknown": 1})
        self.assertNotIn("should-not-echo", json.dumps(out))

    def test_malformed_nested_field_rejected(self):
        with self.assertRaisesRegex(ValueError, "invalid evidence fields"):
            summarize({"CandidateCount": 1, "Candidates": [{
                "EvidenceFieldsPresent": [["not-hashable"]]
            }]})

if __name__=="__main__":
    unittest.main()
