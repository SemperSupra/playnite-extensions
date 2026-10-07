using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;

namespace MediaLibraryEnrichment
{
    public sealed class MediaAdmissionEvidenceSnapshot
    {
        public string Schema { get; set; } =
            "sempersupra-media-library-enrichment-admission-evidence/v1";

        public List<MediaAdmissionEvidence> Items { get; set; } =
            new List<MediaAdmissionEvidence>();

        public static MediaAdmissionEvidenceSnapshot LoadOrEmpty(string path)
        {
            if (!File.Exists(path))
            {
                return new MediaAdmissionEvidenceSnapshot();
            }

            MediaAdmissionEvidenceSnapshot loaded;
            Exception error;
            if (Serialization.TryFromJsonFile(path, out loaded, out error) &&
                loaded != null)
            {
                if (loaded.Items == null)
                {
                    loaded.Items = new List<MediaAdmissionEvidence>();
                }

                return loaded;
            }

            throw new InvalidDataException(
                "Media Library Enrichment admission evidence is not valid JSON.",
                error);
        }
    }
}
