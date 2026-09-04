using System;
using System.Collections.Generic;

namespace RTS.Sim.Engine.Diagnostics
{
    /// <summary>
    /// Channelled logging for the sim and everything around it.
    /// </summary>
    /// <remarks>
    /// <para><strong>It can always be turned off.</strong> <see cref="Enabled"/> is checked
    /// before anything else, so switching it off costs one boolean read per call site and
    /// nothing more. A logging system that cannot be disabled becomes a reason not to log.</para>
    ///
    /// <para><strong>Nothing is formatted until it is wanted.</strong> The overloads here take
    /// a ready string, so <c>Log.Debug(ch, $"x = {x}")</c> builds that string whether or not
    /// the channel is on. In a Tick-phase system, guard it:</para>
    /// <code>
    /// if (Log.On(LogChannel.Commands, LogLevel.Debug)) Log.Debug(LogChannel.Commands, $"{n} applied");
    /// </code>
    ///
    /// <para><strong>It is outside the determinism contract, and must stay that way.</strong>
    /// §7.1 forbids static mutable state in the sim, and this is exactly that — deliberately,
    /// because a logger threaded through every call signature would not be used. The rule that
    /// makes it safe: <em>logging may not influence sim state.</em> Every write method returns
    /// void so there is nothing to read back, and <see cref="On"/> is the one hazard — a system
    /// could do state-changing work inside the guard. Do not. `LogDeterminismTests` replays the
    /// same seed and command log with logging off, on at Error and on at Trace, and asserts the
    /// digests match, which is what would catch it.</para>
    ///
    /// <para>Sinks are registered by the composition root, which is Unity-side and knows where
    /// a file belongs. With no sinks registered, every call is a no-op after the first check —
    /// so a headless test run costs nothing without configuring anything.</para>
    /// </remarks>
    public static class Log
    {
        private static readonly object Gate = new object();

        private static readonly LogLevel[] Levels = NewLevelTable(LogLevel.Info);

        private static ILogSink[] _sinks = Array.Empty<ILogSink>();
        private static LogLevel _defaultLevel = LogLevel.Info;

        /// <summary>Every channel, in declaration order. For config dumps and reader tools.</summary>
        public static readonly LogChannel[] AllChannels =
            (LogChannel[])Enum.GetValues(typeof(LogChannel));

        /// <summary>
        /// The kill switch. False means every call returns after one boolean read: no
        /// formatting, no dispatch, no allocation.
        /// </summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// The in-game day stamped onto records. Set by whoever runs the phases; it is a
        /// convenience so call sites need not thread the day through every signature.
        /// </summary>
        public static int Day { get; set; }

        /// <summary>
        /// The threshold applied to every channel that has not been set individually. Assigning
        /// it re-levels every channel, so apply it before per-channel settings.
        /// </summary>
        public static LogLevel DefaultLevel
        {
            get => _defaultLevel;
            set
            {
                lock (Gate)
                {
                    _defaultLevel = value;
                    for (int i = 0; i < Levels.Length; i++) Levels[i] = value;
                }
            }
        }

        /// <summary>
        /// Whether anything would come of logging at this level. Guard expensive message
        /// building with it.
        /// </summary>
        public static bool On(LogChannel channel, LogLevel level) =>
            Enabled && _sinks.Length > 0 && level >= LevelOf(channel);

        public static LogLevel LevelOf(LogChannel channel)
        {
            int index = (int)channel;
            return index >= 0 && index < Levels.Length ? Levels[index] : _defaultLevel;
        }

        public static void SetLevel(LogChannel channel, LogLevel level)
        {
            int index = (int)channel;
            if (index < 0 || index >= Levels.Length) return;

            lock (Gate) Levels[index] = level;
        }

        public static void SetAllLevels(LogLevel level) => DefaultLevel = level;

        public static void AddSink(ILogSink sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));

            lock (Gate)
            {
                var updated = new ILogSink[_sinks.Length + 1];
                Array.Copy(_sinks, updated, _sinks.Length);
                updated[_sinks.Length] = sink;

                // Swapped whole rather than mutated, so a concurrent write iterates a complete
                // array and never a half-updated one.
                _sinks = updated;
            }
        }

        public static bool RemoveSink(ILogSink sink)
        {
            lock (Gate)
            {
                int index = Array.IndexOf(_sinks, sink);
                if (index < 0) return false;

                var updated = new ILogSink[_sinks.Length - 1];
                Array.Copy(_sinks, updated, index);
                Array.Copy(_sinks, index + 1, updated, index, _sinks.Length - index - 1);
                _sinks = updated;
                return true;
            }
        }

        public static void ClearSinks()
        {
            lock (Gate) _sinks = Array.Empty<ILogSink>();
        }

        public static int SinkCount => _sinks.Length;

        /// <summary>Every channel and its current threshold, in declaration order.</summary>
        public static IReadOnlyList<KeyValuePair<LogChannel, LogLevel>> Snapshot()
        {
            var result = new List<KeyValuePair<LogChannel, LogLevel>>(AllChannels.Length);
            for (int i = 0; i < AllChannels.Length; i++)
                result.Add(new KeyValuePair<LogChannel, LogLevel>(AllChannels[i], LevelOf(AllChannels[i])));

            return result;
        }

        public static void Trace(LogChannel channel, string message) => Write(channel, LogLevel.Trace, message);

        public static void Debug(LogChannel channel, string message) => Write(channel, LogLevel.Debug, message);

        public static void Info(LogChannel channel, string message) => Write(channel, LogLevel.Info, message);

        public static void Warn(LogChannel channel, string message) => Write(channel, LogLevel.Warn, message);

        public static void Error(LogChannel channel, string message) => Write(channel, LogLevel.Error, message);

        public static void Flush()
        {
            ILogSink[] sinks = _sinks;
            for (int i = 0; i < sinks.Length; i++) Safely(sinks[i], s => s.Flush());
        }

        private static LogLevel[] NewLevelTable(LogLevel level)
        {
            var values = (LogChannel[])Enum.GetValues(typeof(LogChannel));

            int highest = 0;
            for (int i = 0; i < values.Length; i++)
                if ((int)values[i] > highest) highest = (int)values[i];

            var table = new LogLevel[highest + 1];
            for (int i = 0; i < table.Length; i++) table[i] = level;
            return table;
        }

        private static void Write(LogChannel channel, LogLevel level, string message)
        {
            // Cheapest check first: the kill switch, then whether anyone is listening at all.
            if (!Enabled) return;

            ILogSink[] sinks = _sinks;
            if (sinks.Length == 0) return;

            if (level < LevelOf(channel)) return;

            var record = new LogRecord(level, channel, Day, message);
            for (int i = 0; i < sinks.Length; i++)
            {
                ILogSink sink = sinks[i];
                Safely(sink, s => s.Write(record));
            }

            // An error is the line you most want to survive a crash, so it is not left in a
            // buffer waiting for a flush that may never come.
            if (level == LogLevel.Error) Flush();
        }

        /// <summary>
        /// Runs a sink operation, and drops the sink if it throws. A logging failure must not
        /// take down the sim, and a sink that throws once will throw on every line after it.
        /// </summary>
        private static void Safely(ILogSink sink, Action<ILogSink> action)
        {
            try
            {
                action(sink);
            }
            catch (Exception)
            {
                RemoveSink(sink);
            }
        }
    }
}
