using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using System;
using System.IO;
using System.Linq;

namespace MediaLibraryEnrichment
{
    public sealed partial class MediaLibraryEnrichmentPlugin
    {
        private const string UriSourceName = "sempersupra-mle";

        private void RegisterUriSource()
        {
            PlayniteApi.UriHandler.RegisterSource(
                UriSourceName,
                HandleUriCommand);
        }

        public override void OnApplicationStopped(
            OnApplicationStoppedEventArgs args)
        {
            PlayniteApi.UriHandler.RemoveSource(UriSourceName);
        }

        private void HandleUriCommand(PlayniteUriEventArgs args)
        {
            var arguments = args == null || args.Arguments == null
                ? new string[0]
                : args.Arguments;
            var command = arguments.FirstOrDefault() ?? string.Empty;
            command = command.Trim().ToLowerInvariant();

            try
            {
                if (arguments.Length != 1)
                {
                    throw new InvalidOperationException(
                        "MLE URI requires exactly one command argument.");
                }

                string summary = null;
                PlayniteApi.MainView.UIDispatcher.Invoke(
                    new Action(
                        () =>
                        {
                            switch (command)
                            {
                                case "preview":
                                    summary = BuildPreviewSummary();
                                    break;
                                case "apply":
                                    summary = ExecuteModeCore("apply", false);
                                    break;
                                case "observe":
                                    summary = ExecuteModeCore("observe", false);
                                    break;
                                case "rollback":
                                    summary = ExecuteModeCore("rollback", true);
                                    break;
                                default:
                                    throw new InvalidOperationException(
                                        "Unsupported MLE URI command.");
                            }
                        }));

                WriteUriCommandReceipt(
                    command,
                    "PASS",
                    summary ?? string.Empty,
                    string.Empty);
            }
            catch (Exception exception)
            {
                WriteUriCommandReceipt(
                    command,
                    "FAIL",
                    string.Empty,
                    exception.Message);
            }
        }

        private void WriteUriCommandReceipt(
            string command,
            string result,
            string summary,
            string error)
        {
            var dataPath = GetPluginUserDataPath();
            Directory.CreateDirectory(dataPath);

            File.WriteAllText(
                Path.Combine(dataPath, "uri-command-receipt.json"),
                Serialization.ToJson(
                    new
                    {
                        schema =
                            "sempersupra-media-library-enrichment-uri-command/v1",
                        source = UriSourceName,
                        command = command ?? string.Empty,
                        result = result ?? string.Empty,
                        summary = summary ?? string.Empty,
                        error = error ?? string.Empty,
                        playnite_version =
                            PlayniteApi.ApplicationInfo.ApplicationVersion
                                .ToString(),
                        sdk_version = SdkVersions.SDKVersion.ToString(),
                        finished_utc = DateTime.UtcNow.ToString("o")
                    },
                    true));
        }
    }
}
