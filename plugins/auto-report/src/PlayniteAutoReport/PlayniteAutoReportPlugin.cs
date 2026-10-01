using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using PlayniteAutoReport.Reporting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAutoReport
{
    public sealed class PlayniteAutoReportPlugin : GenericPlugin
    {
        private const string uriSource = "playniteautoreport";
        private static readonly ILogger logger = LogManager.GetLogger();

        public override Guid Id { get; } =
            Guid.Parse("b27b4f0c-642b-4fbd-9559-6e832026f010");

        private readonly object settingsLock = new object();
        private readonly SemaphoreSlim exportGate = new SemaphoreSlim(1, 1);

        private readonly string pluginDataPath;
        private readonly string settingsPath;
        private readonly ReportExporter exporter;

        private ReportSettings settings;

        public PlayniteAutoReportPlugin(IPlayniteAPI api)
            : base(api)
        {
            Properties = new GenericPluginProperties
            {
                HasSettings = false
            };

            pluginDataPath = GetPluginUserDataPath();
            Directory.CreateDirectory(pluginDataPath);

            settingsPath = Path.Combine(pluginDataPath, "report-settings.json");
            settings = ReportSettings.LoadOrCreate(settingsPath, logger);
            exporter = new ReportExporter(pluginDataPath, logger);

            PlayniteApi.UriHandler.RegisterSource(
                uriSource,
                uriArgs => QueueAutomaticExport("uri"));
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(
            GetMainMenuItemsArgs args)
        {
            return new[]
            {
                new MainMenuItem
                {
                    Description = "Export now",
                    MenuSection = "@Playnite Auto Report",
                    Action = menuArgs => ExportNow()
                },
                new MainMenuItem
                {
                    Description = "Open report directory",
                    MenuSection = "@Playnite Auto Report",
                    Action = menuArgs => OpenReportDirectory()
                },
                new MainMenuItem
                {
                    Description = "Open configuration file",
                    MenuSection = "@Playnite Auto Report",
                    Action = menuArgs => OpenConfigurationFile()
                },
                new MainMenuItem
                {
                    Description = "Reload configuration",
                    MenuSection = "@Playnite Auto Report",
                    Action = menuArgs => ReloadConfiguration()
                }
            };
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            if (GetSettingsSnapshot().ExportOnApplicationStart)
            {
                QueueAutomaticExport("application-start");
            }
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            if (GetSettingsSnapshot().ExportOnLibraryUpdate)
            {
                QueueAutomaticExport("library-update");
            }
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            if (GetSettingsSnapshot().ExportOnGameStopped)
            {
                QueueAutomaticExport("game-stopped");
            }
        }

        public override void Dispose()
        {
            PlayniteApi.UriHandler.RemoveSource(uriSource);
            base.Dispose();
        }

        private void ExportNow()
        {
            try
            {
                var currentSettings = GetSettingsSnapshot();
                var snapshot = CaptureSnapshot(currentSettings, "manual");

                exportGate.Wait();
                try
                {
                    var result = exporter.Export(snapshot, currentSettings);
                    PlayniteApi.Dialogs.ShowMessage(
                        string.Format(
                            "Exported {0} games to:\n\n{1}",
                            result.GameCount,
                            result.OutputDirectory),
                        "Playnite Auto Report");
                }
                finally
                {
                    exportGate.Release();
                }
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Manual Playnite report export failed.");
                PlayniteApi.Dialogs.ShowErrorMessage(
                    exception.Message,
                    "Playnite Auto Report");
            }
        }

        private void QueueAutomaticExport(string trigger)
        {
            try
            {
                var currentSettings = GetSettingsSnapshot();
                var snapshot = CaptureSnapshot(currentSettings, trigger);

                Task.Run(async () =>
                {
                    await exportGate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        var result = exporter.Export(snapshot, currentSettings);
                        logger.Info(
                            string.Format(
                                "Exported {0} games after {1} to {2}.",
                                result.GameCount,
                                trigger,
                                result.OutputDirectory));
                    }
                    catch (Exception exception)
                    {
                        logger.Error(
                            exception,
                            "Automatic Playnite report export failed after " + trigger + ".");
                    }
                    finally
                    {
                        exportGate.Release();
                    }
                });
            }
            catch (Exception exception)
            {
                logger.Error(
                    exception,
                    "Unable to capture Playnite library snapshot after " + trigger + ".");
            }
        }

        private LibrarySnapshot CaptureSnapshot(
            ReportSettings currentSettings,
            string trigger)
        {
            return SnapshotFactory.Create(
                PlayniteApi.Database.Games,
                currentSettings,
                trigger);
        }

        private ReportSettings GetSettingsSnapshot()
        {
            lock (settingsLock)
            {
                return settings.Clone();
            }
        }

        private void ReloadConfiguration()
        {
            try
            {
                var loaded = ReportSettings.Load(settingsPath);
                lock (settingsLock)
                {
                    settings = loaded;
                }

                PlayniteApi.Dialogs.ShowMessage(
                    "Configuration reloaded.",
                    "Playnite Auto Report");
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Unable to reload Playnite Auto Report configuration.");
                PlayniteApi.Dialogs.ShowErrorMessage(
                    exception.Message,
                    "Playnite Auto Report");
            }
        }

        private void OpenConfigurationFile()
        {
            try
            {
                if (!File.Exists(settingsPath))
                {
                    GetSettingsSnapshot().Save(settingsPath);
                }

                OpenWithShell(settingsPath);
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Unable to open Playnite Auto Report configuration.");
                PlayniteApi.Dialogs.ShowErrorMessage(
                    exception.Message,
                    "Playnite Auto Report");
            }
        }

        private void OpenReportDirectory()
        {
            try
            {
                var outputDirectory =
                    GetSettingsSnapshot().ResolveOutputDirectory(pluginDataPath);
                Directory.CreateDirectory(outputDirectory);
                OpenWithShell(outputDirectory);
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Unable to open Playnite report directory.");
                PlayniteApi.Dialogs.ShowErrorMessage(
                    exception.Message,
                    "Playnite Auto Report");
            }
        }

        private static void OpenWithShell(string path)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
    }
}
