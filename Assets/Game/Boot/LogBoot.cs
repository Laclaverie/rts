using System;
using System.IO;
using RTS.Content.Loading;
using RTS.Content.Validation;
using RTS.Game.Diagnostics;
using RTS.Sim.Engine.Diagnostics;
using UnityEngine;

namespace RTS.Game.Boot
{
    /// <summary>
    /// Installs the log sinks and applies <c>logging.csv</c>, before anything else runs.
    /// </summary>
    /// <remarks>
    /// The composition root for logging: the one place that knows both where a file belongs and
    /// what <c>Sim</c> expects. Runs before the first scene loads, so a failure during content
    /// loading is already being recorded.
    /// <para>
    /// Nothing here may throw. Logging is instrumentation; a game that refuses to start because
    /// it could not open a log file has traded a minor problem for a total one. Every failure
    /// degrades instead: no config file means default levels, no writable directory means
    /// console only.
    /// </para>
    /// </remarks>
    public static class LogBoot
    {
        /// <summary>Folder under <c>Application.persistentDataPath</c>.</summary>
        public const string LogFolderName = "Logs";

        /// <summary>How many previous runs' logs to keep.</summary>
        public const int KeepFiles = 10;

        private static FileLogSink _file;
        private static bool _installed;

        private static readonly System.Diagnostics.Stopwatch _session =
            new System.Diagnostics.Stopwatch();

        public static string LogDirectory => Path.Combine(Application.persistentDataPath, LogFolderName);

        /// <summary>The file this run is writing to, or null if none could be opened.</summary>
        public static string CurrentLogPath => _file?.Path;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Install()
        {
            if (_installed) return;

            _installed = true;

            // One clock for the whole session, so the console and the log file agree about
            // what time it is. A reader comparing the two should not have to work out which
            // zero is which — and the console's leading column used to be a hardcoded zero on
            // every line, a fixed-width nothing in the one place somebody is skimming for a
            // problem.
            _session.Restart();

            Log.AddSink(new UnityConsoleLogSink(elapsedSeconds: Elapsed));

            TryOpenFile();
            TryApplySettings();

            Log.Info(LogChannel.Boot,
                $"logging started; unity {Application.unityVersion}; file {CurrentLogPath ?? "<none>"}");

            Application.quitting += Shutdown;
        }

        /// <summary>Flushes and closes the log file. Called on quit.</summary>
        public static void Shutdown()
        {
            if (!_installed) return;

            Log.Info(LogChannel.Boot, "logging stopped");
            Log.Flush();

            if (_file != null)
            {
                Log.RemoveSink(_file);
                _file.Dispose();
                _file = null;
            }

            Application.quitting -= Shutdown;
            _installed = false;
        }

        /// <summary>Seconds since logging started, shared by every sink.</summary>
        public static double Elapsed() => _session.Elapsed.TotalSeconds;

        private static void TryOpenFile()
        {
            try
            {
                _file = FileLogSink.Open(LogDirectory, KeepFiles, Elapsed);
                Log.AddSink(_file);
            }
            catch (Exception e)
            {
                // Console only from here. Reported through Unity directly, since the sink that
                // would have carried it is the thing that failed.
                UnityEngine.Debug.LogWarning(
                    $"[Boot] no log file: {e.GetType().Name}: {e.Message}. Console logging only.");
            }
        }

        private static void TryApplySettings()
        {
            try
            {
                CsvTable table = ConfigFiles.ReadCsv(ConfigFiles.LoggingFile);
                var report = new ValidationReport();
                LogSettings settings = LogSettings.Load(table, report);

                if (!report.IsValid)
                {
                    // Loud, but not fatal: unreadable logging config must not stop a launch, and
                    // the default levels are serviceable. Content problems are a different
                    // matter — those do stop it (§5.3).
                    foreach (string problem in report.Problems)
                        Log.Warn(LogChannel.Boot, problem);
                }

                settings.Apply();
            }
            catch (Exception e)
            {
                Log.Warn(LogChannel.Boot,
                    $"{ConfigFiles.LoggingFile} not applied ({e.GetType().Name}: {e.Message}); using defaults.");
            }
        }
    }
}
