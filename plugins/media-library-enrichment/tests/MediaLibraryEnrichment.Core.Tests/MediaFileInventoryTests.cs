using System;
using System.IO;
using Xunit;

namespace MediaLibraryEnrichment.Core.Tests
{
    public sealed class MediaFileInventoryTests : IDisposable
    {
        private readonly string root;

        public MediaFileInventoryTests()
        {
            root = Path.Combine(Path.GetTempPath(), "mle-synthetic-inventory-" +
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        [Fact]
        public void ReturnsOnlySupportedImmediateFilesWithStableOrder()
        {
            File.WriteAllText(Path.Combine(root, "b.cbz"), "");
            File.WriteAllText(Path.Combine(root, "a.epub"), "");
            File.WriteAllText(Path.Combine(root, "c.flac"), "");
            File.WriteAllText(Path.Combine(root, "d.pdfx"), "");
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            File.WriteAllText(Path.Combine(root, "nested", "not-discovered.pdf"), "");

            var result = MediaFileInventory.InspectImmediateFiles(root);
            Assert.Equal(5, result.EntriesExamined);
            Assert.Equal(0, result.ReparsePointsSkipped);
            Assert.Collection(result.SupportedFiles,
                item => { Assert.Equal("a.epub", item.FileName); Assert.Equal("book", item.Kind); },
                item => { Assert.Equal("b.cbz", item.FileName); Assert.Equal("comic", item.Kind); },
                item => { Assert.Equal("c.flac", item.FileName); Assert.Equal("audio", item.Kind); });
            Assert.True(File.Exists(Path.Combine(root, "d.pdfx")));
            Assert.True(File.Exists(Path.Combine(root, "nested", "not-discovered.pdf")));
        }

        [Fact]
        public void EmptyDirectoryHasEmptyReadOnlyInventory()
        {
            var result = MediaFileInventory.InspectImmediateFiles(root);
            Assert.Equal(0, result.EntriesExamined);
            Assert.Empty(result.SupportedFiles);
        }

        [Fact]
        public void ExcessiveEntryCountFailsWithoutPartialResult()
        {
            File.WriteAllText(Path.Combine(root, "one.epub"), "");
            File.WriteAllText(Path.Combine(root, "two.cbz"), "");
            File.WriteAllText(Path.Combine(root, "three.txt"), "");
            Assert.Throws<InvalidOperationException>(
                () => MediaFileInventory.InspectImmediateFiles(root, maxEntries: 2));
        }

        [Fact]
        public void RejectsRelativeAndVolumeRootScans()
        {
            Assert.Throws<ArgumentException>(() =>
                MediaFileInventory.InspectImmediateFiles("relative/directory"));
            var volumeRoot = Path.GetPathRoot(root);
            Assert.Throws<ArgumentException>(() =>
                MediaFileInventory.InspectImmediateFiles(volumeRoot));
        }

        [Fact]
        public void RejectsMissingRootAndInvalidLimit()
        {
            Assert.Throws<DirectoryNotFoundException>(() =>
                MediaFileInventory.InspectImmediateFiles(Path.Combine(root, "absent")));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                MediaFileInventory.InspectImmediateFiles(root, maxEntries: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                MediaFileInventory.InspectImmediateFiles(root, maxEntries: 10001));
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
