using PlayniteAutoReport.Reporting;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace PlayniteAutoReport.Tests
{
    public sealed class CsvWriterTests
    {
        [Fact]
        public void WriteProducesUtf8BomAndEscapesFields()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "PlayniteAutoReport.Tests",
                Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "report.csv");

            try
            {
                CsvWriter.Write(
                    path,
                    new[]
                    {
                        new GameReportRecord
                        {
                            Name = "Quoted, \"Game\"",
                            Source = "Steam",
                            Platforms = "PC",
                            ActivityClass = "Never played"
                        }
                    });

                var bytes = File.ReadAllBytes(path);
                Assert.True(
                    bytes.Length >= 3 &&
                    bytes[0] == 0xEF &&
                    bytes[1] == 0xBB &&
                    bytes[2] == 0xBF);

                var text = Encoding.UTF8.GetString(bytes);
                Assert.Contains("\"Quoted, \"\"Game\"\"\"", text);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }
    }
}
