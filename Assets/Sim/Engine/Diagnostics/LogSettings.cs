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
    /// same loud failure, same file-and-line problems.
    /// <para>
    /// Because <see cref="LogChannel"/> is an enum, a misspelled channel name is a load failure
    /// naming the valid ones — not a phantom channel configured while the real one silently
    /// keeps its default. That is the whole reason the set is closed.
    /// </para>
    /// <para>
    /// It lives in <c>Sim</c> rather than <c>Content</c> because it produces
    /// <see cref="LogLevel"/> and <see cref="LogChannel"/>, and the dependency arrow runs
    /// Sim → Content, never back.
    /// </para>
    /// </remarks>
    public sealed class LogSettings
    {
        public const string ChannelColumn = "channel";
        public const string LevelColumn = "level";

        /// <summary>The channel name that sets the default for everything not listed.</summary>
        public const string DefaultChannelName = "*";

        private readonly List<KeyValuePair<LogChannel, LogLevel>> _levels =
            new List<KeyValuePair<LogChannel, LogLevel>>();

        public LogLevel DefaultLevel { get; private set; } = LogLevel.Info;

        /// <summary>Channel thresholds in file order.</summary>
        public IReadOnlyList<KeyValuePair<LogChannel, LogLevel>> Levels => _levels;

        public static LogSettings Load(CsvTable table, ValidationReport report)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (report == null) throw new ArgumentNullException(nameof(report));

            var settings = new LogSettings();

            if (!report.RequireColumns(table, ChannelColumn, LevelColumn)) return settings;

            var seen = new Dictionary<LogChannel, int>();
            bool defaultSeen = false;

            foreach (CsvRow row in table.Rows)
            {
                var reader = new RowReader(row, report, table.SourceName);

                string channelText = reader.Text(ChannelColumn, required: true);
                if (reader.HasProblems) continue;

                if (channelText == DefaultChannelName)
                {
                    LogLevel defaultLevel = reader.Enum<LogLevel>(LevelColumn);
                    if (reader.HasProblems) continue;

                    if (defaultSeen)
                    {
                        report.Add(table.SourceName, row.Line, "the default '*' is set more than once.");
                        continue;
                    }

                    defaultSeen = true;
                    settings.DefaultLevel = defaultLevel;
                    continue;
                }

                LogChannel channel = reader.Enum<LogChannel>(ChannelColumn);
                LogLevel level = reader.Enum<LogLevel>(LevelColumn);
                if (reader.HasProblems) continue;

                if (seen.TryGetValue(channel, out int firstLine))
                {
                    report.Add(table.SourceName, row.Line,
                        $"channel '{channel}' is already configured on line {firstLine}.");
                    continue;
                }

                seen.Add(channel, row.Line);
                settings._levels.Add(new KeyValuePair<LogChannel, LogLevel>(channel, level));
            }

            return settings;
        }

        /// <summary>
        /// Applies these thresholds. The default goes first, because setting it re-levels every
        /// channel — including the ones this file names.
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
