using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using System;
using System.IO;
using System.Linq;

namespace MediaLibraryEnrichment
{
    public sealed class MediaLibraryEnrichmentPlugin : GenericPlugin
    {
        public static readonly Guid PluginGuid =
            Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1377");

        public override Guid Id { get; } = PluginGuid;

        public MediaLibraryEnrichmentPlugin(IPlayniteAPI api)
            : base(api)
        {
            Properties = new GenericPluginProperties
            {
                HasSettings = false
            };
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            var candidates = PlayniteApi.Database.Games
                .Where(game =>
                    MediaCandidateClassifier.ShouldObserve(
                        game.Source == null ? string.Empty : game.Source.Name,
                        !string.IsNullOrWhiteSpace(game.CoverImage)))
                .Select(game => new MediaObservation
                {
                    PlayniteId = game.Id.ToString(),
                    ProviderGameId = game.GameId ?? string.Empty,
                    Name = game.Name ?? string.Empty,
                    Source = game.Source == null ? string.Empty : game.Source.Name ?? string.Empty,
                    Kind = MediaCandidateClassifier.ClassifyKind(game.Manual, game.Notes),
                    LocalEvidenceName =
                        MediaCandidateClassifier.NormalizeLocalEvidenceName(game.Manual),
                    CoverMissing = string.IsNullOrWhiteSpace(game.CoverImage)
                })
                .OrderBy(item => item.PlayniteId, StringComparer.Ordinal)
                .ToArray();

            var receipt = new MediaObservationReceipt
            {
                Schema = "sempersupra-media-library-enrichment-observation/v1",
                FixtureContract = "media-baseline-v1",
                CandidateCount = candidates.Length,
                Candidates = candidates
            };

            var dataPath = GetPluginUserDataPath();
            Directory.CreateDirectory(dataPath);
            File.WriteAllText(
                Path.Combine(dataPath, "observation-receipt.json"),
                Serialization.ToJson(receipt, true));
        }
    }
}
