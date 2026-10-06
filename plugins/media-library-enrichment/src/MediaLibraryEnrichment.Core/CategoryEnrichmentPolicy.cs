using System;

namespace MediaLibraryEnrichment
{
    public sealed class CategoryEnrichmentSpec
    {
        public string Kind { get; set; }
        public Guid CategoryId { get; set; }
        public string CategoryName { get; set; }
    }

    public enum CategoryMembershipDecision
    {
        Noop,
        AddMembership,
        UserOverride
    }

    public static class CategoryEnrichmentPolicy
    {
        private static readonly CategoryEnrichmentSpec Book = new CategoryEnrichmentSpec
        {
            Kind = "book",
            CategoryId = Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1401"),
            CategoryName = "SemperSupra.Media:Book"
        };

        private static readonly CategoryEnrichmentSpec Comic = new CategoryEnrichmentSpec
        {
            Kind = "comic",
            CategoryId = Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1402"),
            CategoryName = "SemperSupra.Media:Comic"
        };

        private static readonly CategoryEnrichmentSpec Audio = new CategoryEnrichmentSpec
        {
            Kind = "audio",
            CategoryId = Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1403"),
            CategoryName = "SemperSupra.Media:Audio"
        };

        public static CategoryMembershipDecision DecideMembership(
            bool membershipPresent,
            string ledgerStatus)
        {
            if (membershipPresent)
            {
                return CategoryMembershipDecision.Noop;
            }

            if (string.Equals(
                    ledgerStatus,
                    "COMMITTED",
                    StringComparison.Ordinal) ||
                string.Equals(
                    ledgerStatus,
                    "USER_OVERRIDDEN",
                    StringComparison.Ordinal))
            {
                return CategoryMembershipDecision.UserOverride;
            }

            return CategoryMembershipDecision.AddMembership;
        }

        public static CategoryEnrichmentSpec ForKind(string kind)
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
