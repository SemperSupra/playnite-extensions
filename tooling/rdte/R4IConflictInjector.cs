using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;

namespace SemperSupraRdteConflictInjector
{
    public sealed class SemperSupraRdteConflictInjector : GenericPlugin
    {
        public static readonly Guid PluginGuid =
            Guid.Parse("6d06cf1b-d1e4-4caa-b6c3-cc6026953136");

        private static readonly Guid ManualGameId =
            Guid.Parse("73000000-0000-4000-8000-000000000004");

        private static readonly Guid BookCategoryId =
            Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1401");

        private const string BookCategoryName = "SemperSupra.Media:Book";

        public override Guid Id { get; } = PluginGuid;

        public SemperSupraRdteConflictInjector(IPlayniteAPI api)
            : base(api)
        {
            Properties = new GenericPluginProperties { HasSettings = false };
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            var dataPath = GetPluginUserDataPath();
            Directory.CreateDirectory(dataPath);

            var modePath = Path.Combine(dataPath, "mode.txt");
            var receiptPath = Path.Combine(dataPath, "conflict-receipt.json");
            var mode = File.Exists(modePath)
                ? File.ReadAllText(modePath).Trim()
                : "verify";

            string result = "PASS";
            string detail = string.Empty;

            try
            {
                var category = PlayniteApi.Database.Categories.Get(BookCategoryId);
                var game = PlayniteApi.Database.Games.Get(ManualGameId);

                if (category == null ||
                    !string.Equals(category.Name, BookCategoryName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Expected plugin-owned Book category is unavailable or renamed.");
                }

                if (game == null)
                {
                    throw new InvalidOperationException("Manual control game is unavailable.");
                }

                if (string.Equals(mode, "apply", StringComparison.OrdinalIgnoreCase))
                {
                    var categories = game.CategoryIds == null
                        ? new List<Guid>()
                        : new List<Guid>(game.CategoryIds);

                    if (!categories.Contains(BookCategoryId))
                    {
                        categories.Add(BookCategoryId);
                        game.CategoryIds = categories;
                        PlayniteApi.Database.Games.Update(game);
                    }
                }
                else if (!string.Equals(mode, "verify", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Unsupported conflict-injector mode '" + mode + "'.");
                }

                var verifiedGame = PlayniteApi.Database.Games.Get(ManualGameId);
                var membershipPresent =
                    verifiedGame != null &&
                    verifiedGame.CategoryIds != null &&
                    verifiedGame.CategoryIds.Contains(BookCategoryId);

                if (!membershipPresent)
                {
                    throw new InvalidOperationException(
                        "External manual-game category membership is not present.");
                }

                detail = string.Equals(mode, "apply", StringComparison.OrdinalIgnoreCase)
                    ? "External membership applied and verified."
                    : "External membership remains present.";
            }
            catch (Exception exception)
            {
                result = "FAIL";
                detail = exception.Message;
            }

            var currentCategory = PlayniteApi.Database.Categories.Get(BookCategoryId);
            var currentGame = PlayniteApi.Database.Games.Get(ManualGameId);
            var currentMembership =
                currentGame != null &&
                currentGame.CategoryIds != null &&
                currentGame.CategoryIds.Contains(BookCategoryId);

            File.WriteAllText(
                receiptPath,
                Serialization.ToJson(
                    new
                    {
                        schema = "sempersupra-playnite-r4i-conflict-injector/v1",
                        mode = mode,
                        result = result,
                        manual_game_id = ManualGameId.ToString(),
                        category_id = BookCategoryId.ToString(),
                        category_name = currentCategory == null ? string.Empty : currentCategory.Name,
                        membership_present = currentMembership,
                        detail = detail
                    },
                    true));
        }
    }
}
