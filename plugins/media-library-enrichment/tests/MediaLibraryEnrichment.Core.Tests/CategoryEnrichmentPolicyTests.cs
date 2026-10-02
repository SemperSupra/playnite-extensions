using System;
using Xunit;

namespace MediaLibraryEnrichment.Core.Tests
{
    public sealed class CategoryEnrichmentPolicyTests
    {
        [Theory]
        [InlineData("book", "4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1401", "SemperSupra.Media:Book")]
        [InlineData("comic", "4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1402", "SemperSupra.Media:Comic")]
        [InlineData("audio", "4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1403", "SemperSupra.Media:Audio")]
        public void MapsRecognizedKindsToStableOwnedCategories(
            string kind,
            string expectedId,
            string expectedName)
        {
            var spec = CategoryEnrichmentPolicy.ForKind(kind);

            Assert.NotNull(spec);
            Assert.Equal(Guid.Parse(expectedId), spec.CategoryId);
            Assert.Equal(expectedName, spec.CategoryName);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("unresolved")]
        [InlineData("video")]
        public void DoesNotInventCategoriesForUnsupportedKinds(string kind)
        {
            Assert.Null(CategoryEnrichmentPolicy.ForKind(kind));
        }
    }
}
