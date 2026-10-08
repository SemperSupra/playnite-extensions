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

if __name__=="__main__":
    unittest.main()
