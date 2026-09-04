using System;
using System.Collections.Generic;
using RTS.Content.Loading;
using RTS.Content.Validation;

namespace RTS.Sim.Engine.Diagnostics
{
    /// <summary>
    /// Channel thresholds, read from <c>logging.csv</c> so they can be changed without a
    /// rebuild.
    /// </summary>
    /// <remarks>
    /// Uses the §5.3 validation harness rather than a second config mechanism: same reader,
    /// same loud failure, same file-and-line problems. A typo in a level name is reported, not
    /// silently treated as "off" — a channel that quietly stops logging is the logging
    /// equivalent of a system missing from pipeline.csv.
    /// <para>
    /// It lives in <c>Sim</c> rather than <c>Content</c> because it produces
    /// <see cref="LogLevel"/>, and the dependency arrow runs Sim → Content, never back.
    /// </para>
    /// </remarks>
    public sealed class LogSettings
    {
        public const string ChannelColumn = "channel";
        public const string LevelColumn = "level";

        /// <summary>The channel name that sets the default for everything not listed.</summary>
        public const string DefaultChannelName = "*";

        private readonly List<KeyValuePair<string, LogLevel>> _levels =
            new List<KeyValuePair<string, LogLevel>>();

        public LogLevel DefaultLevel { get; private set; } = LogLevel.Info;

        /// <summary>Channel thresholds in file order.</summary>
        public IReadOnlyList<KeyValuePair<string, LogLevel>> Levels => _levels;

        public static LogSettings Load(CsvTable table, ValidationReport report)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (report == null) throw new ArgumentNullException(nameof(report));

            var settings = new LogSettings();

            if (!report.RequireColumns(table, ChannelColumn, LevelColumn)) return settings;

            var seen = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (CsvRow row in table.Rows)
            {
                var reader = new RowReader(row, report, table.SourceName);

                string channel = reader.Text(ChannelColumn, required: true);
                LogLevel level = reader.Enum<LogLevel>(LevelColumn);

                if (reader.HasProblems) continue;

                if (seen.TryGetValue(channel, out int firstLine))
                {
                    report.Add(table.SourceName, row.Line,
                        $"channel '{channel}' is already configured on line {firstLine}.");
                    continue;
                }

                seen.Add(channel, row.Line);

                if (channel == DefaultChannelName) settings.DefaultLevel = level;
                else settings._levels.Add(new KeyValuePair<string, LogLevel>(channel, level));
            }

            return settings;
        }

        /// <summary>
        /// Applies these thresholds to <see cref="Log"/>. The default is set first, so a
        /// channel declared later by a class that has not run yet inherits it.
        /// </summary>
        public void Apply()
        {
            Log.DefaultLevel = DefaultLevel;

            for (int i = 0; i < _levels.Count; i++)
                Log.SetLevel(_levels[i].Key, _levels[i].Value);
        }

        public override string ToString() =>
            $"LogSettings(default {DefaultLevel}, {_levels.Count} channel(s))";
    }
}
