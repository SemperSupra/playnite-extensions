namespace MediaLibraryEnrichment
{
    public sealed class ActionEnrichmentSpec
    {
        public string Kind { get; set; }
        public string SemanticKey { get; set; }
        public string ActionName { get; set; }
    }

    public static class ActionEnrichmentPolicy
    {
        private static readonly ActionEnrichmentSpec Book = new ActionEnrichmentSpec
        {
            Kind = "book",
            SemanticKey = "media-open-book",
            ActionName = "Read"
        };

        private static readonly ActionEnrichmentSpec Comic = new ActionEnrichmentSpec
        {
            Kind = "comic",
            SemanticKey = "media-open-comic",
            ActionName = "Read"
        };

        private static readonly ActionEnrichmentSpec Audio = new ActionEnrichmentSpec
        {
            Kind = "audio",
            SemanticKey = "media-open-audio",
            ActionName = "Listen"
        };

        public static ActionEnrichmentSpec ForKind(string kind)
        {
            switch ((kind ?? string.Empty).ToLowerInvariant())
            {
                case "book":
                    return Book;
                case "comic":
                    return Comic;
                case "audio":
                    return Audio;
                default:
                    return null;
            }
        }
    }
}
