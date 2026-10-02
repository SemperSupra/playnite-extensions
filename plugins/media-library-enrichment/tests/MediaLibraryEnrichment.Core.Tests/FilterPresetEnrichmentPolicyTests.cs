using System;
using System.Linq;
using Xunit;

namespace MediaLibraryEnrichment.Core.Tests
{
    public sealed class FilterPresetEnrichmentPolicyTests
    {
        [Theory]
        [InlineData(
            "book",
            "4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1501",
            "SemperSupra Media: Books",
            "4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1401")]
        [InlineData(
            "comic",
            "4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1502",
            "SemperSupra Media: Comics",
            "4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1402")]
        [InlineData(
            "audio",
            "4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1503",
            "SemperSupra Media: Audio",
            "4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1403")]
        public void MapsKnownKindsToStableNativeFilterPresets(
            string kind,
            string presetId,
            string presetName,
            string categoryId)
        {
            var spec = FilterPresetEnrichmentPolicy.ForKind(kind);

            Assert.NotNull(spec);
            Assert.Equal(Guid.Parse(presetId), spec.PresetId);
            Assert.Equal(presetName, spec.PresetName);
            Assert.Equal(Guid.Parse(categoryId), spec.CategoryId);
        }

        [Fact]
        public void LeavesUnknownKindsUnmanaged()
        {
            Assert.Null(FilterPresetEnrichmentPolicy.ForKind("video"));
        }

        [Fact]
        public void StablePresetIdsAreUnique()
        {
            var specs = FilterPresetEnrichmentPolicy.All();

            Assert.Equal(3, specs.Length);
            Assert.Equal(3, specs.Select(item => item.PresetId).Distinct().Count());
        }
    }
}
