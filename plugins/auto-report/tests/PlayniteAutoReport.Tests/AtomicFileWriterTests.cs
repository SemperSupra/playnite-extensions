using PlayniteAutoReport.Infrastructure;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace PlayniteAutoReport.Tests
{
    public sealed class AtomicFileWriterTests
    {
        [Fact]
        public void WriteTextCreatesNewDestination()
        {
            var directory = CreateTemporaryDirectory();

            try
            {
                var path = Path.Combine(directory, "report.txt");
                var encoding = new UTF8Encoding(false);

                AtomicFileWriter.WriteText(path, "first", encoding);

                Assert.Equal("first", File.ReadAllText(path, encoding));
                Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [Fact]
        public void WriteTextReplacesExistingDestination()
        {
            var directory = CreateTemporaryDirectory();

            try
            {
                var path = Path.Combine(directory, "report.txt");
                var encoding = new UTF8Encoding(false);
                File.WriteAllText(path, "original", encoding);

                AtomicFileWriter.WriteText(path, "replacement", encoding);

                Assert.Equal("replacement", File.ReadAllText(path, encoding));
                Assert.Single(Directory.EnumerateFiles(directory));
                Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [Fact]
        public void WriteFailurePreservesDestinationAndRemovesTemporaryFile()
        {
            var directory = CreateTemporaryDirectory();

            try
            {
                var path = Path.Combine(directory, "report.txt");
                File.WriteAllText(path, "original", new UTF8Encoding(false));

                var error = Assert.Throws<InvalidOperationException>(() =>
                    AtomicFileWriter.Write(path, stream =>
                    {
                        stream.WriteByte(42);
                        throw new InvalidOperationException("simulated failure");
                    }));

                Assert.Equal("simulated failure", error.Message);
                Assert.Equal("original", File.ReadAllText(path));
                Assert.Single(Directory.EnumerateFiles(directory));
                Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [Fact]
        public void WriteRejectsInvalidArguments()
        {
            Assert.Throws<ArgumentException>(() =>
                AtomicFileWriter.Write(" ", stream => stream.WriteByte(1)));
            Assert.Throws<ArgumentNullException>(() =>
                AtomicFileWriter.Write(Path.Combine("directory", "file"), null));
            Assert.Throws<InvalidOperationException>(() =>
                AtomicFileWriter.Write("file.txt", stream => stream.WriteByte(1)));
        }

        private static string CreateTemporaryDirectory()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "PlayniteAutoReportTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteTemporaryDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }
}
