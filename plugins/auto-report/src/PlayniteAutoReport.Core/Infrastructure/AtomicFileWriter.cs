using System;
using System.IO;
using System.Text;

namespace PlayniteAutoReport.Infrastructure
{
    internal static class AtomicFileWriter
    {
        public static void WriteText(string path, string content, Encoding encoding)
        {
            Write(path, stream =>
            {
                using (var writer = new StreamWriter(stream, encoding, 4096, true))
                {
                    writer.Write(content);
                }
            });
        }

        public static void Write(string path, Action<Stream> writeAction)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A destination path is required.", nameof(path));
            }

            if (writeAction == null)
            {
                throw new ArgumentNullException(nameof(writeAction));
            }

            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("The destination path has no directory.");
            }

            Directory.CreateDirectory(directory);

            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    writeAction(stream);
                    stream.Flush();
                }

                Replace(temporaryPath, path);
                temporaryPath = null;
            }
            finally
            {
                if (!string.IsNullOrEmpty(temporaryPath) && File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static void Replace(string temporaryPath, string destinationPath)
        {
            if (!File.Exists(destinationPath))
            {
                File.Move(temporaryPath, destinationPath);
                return;
            }

            try
            {
                File.Replace(temporaryPath, destinationPath, null);
            }
            catch (PlatformNotSupportedException)
            {
                File.Delete(destinationPath);
                File.Move(temporaryPath, destinationPath);
            }
            catch (IOException)
            {
                File.Delete(destinationPath);
                File.Move(temporaryPath, destinationPath);
            }
        }
    }
}
