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
    /// if (Log.On(Combat, LogLevel.Debug)) Log.Debug(Combat, $"resolved {count} attacks");
    /// </code>
    ///
    /// <para><strong>It is outside the determinism contract, and must stay that way.</strong>
    /// §7.1 forbids static mutable state in the sim, and this is exactly that — deliberately,
    /// because a logger threaded through every call signature would not be used. The rule that
    /// makes it safe: <em>logging may not influence sim state.</em> Every write method returns
    /// void so there is nothing to read back, and <see cref="On"/> is the one hazard — a system
    /// could do state-changing work inside the guard. Do not. `LogDoesNotAffectDeterminism`
    /// replays the same seed and command log with logging on and off and asserts the digests
    /// match, which is what would catch it.</para>
    ///
    /// <para>Sinks are registered by the composition root, which is Unity-side and knows where
    /// a file belongs. With no sinks registered, every call is a no-op after the first check —
    /// so a headless test run costs nothing without configuring anything.</para>
    /// </remarks>
    public static class Log
    {
        private static readonly object Gate = new object();
        private static readonly LogChannelTable Channels = new LogChannelTable();

        private static ILogSink[] _sinks = Array.Empty<ILogSink>();

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

        /// <summary>The threshold for channels nobody has configured.</summary>
        public static LogLevel DefaultLevel
        {
            get => Channels.DefaultLevel;
            set => Channels.DefaultLevel = value;
        }

        /// <summary>
        /// Declares or looks up a channel. Hold the result in a <c>static readonly</c> field:
        /// this takes a lock, and the returned handle makes every later check an array read.
        /// </summary>
        public static LogChannel Channel(string name) => Channels.Resolve(name);

        /// <summary>
        /// Whether anything would come of logging at this level. Guard expensive message
        /// building with it.
        /// </summary>
        public static bool On(in LogChannel channel, LogLevel level) =>
            Enabled && _sinks.Length > 0 && level >= Channels.LevelOf(channel);

        public static void SetLevel(in LogChannel channel, LogLevel level) =>
            Channels.SetLevel(channel, level);

        public static void SetLevel(string channel, LogLevel level) =>
            Channels.SetLevel(channel, level);

        /// <summary>Every declared channel and its threshold, for a config dump or a reader.</summary>
        public static IReadOnlyList<KeyValuePair<string, LogLevel>> Channels_Snapshot() =>
            Channels.Snapshot();

        public static void SetAllLevels(LogLevel level) => Channels.ResetLevels(level);

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

        public static void Trace(in LogChannel channel, string message) => Write(channel, LogLevel.Trace, message);

        public static void Debug(in LogChannel channel, string message) => Write(channel, LogLevel.Debug, message);

        public static void Info(in LogChannel channel, string message) => Write(channel, LogLevel.Info, message);

        public static void Warn(in LogChannel channel, string message) => Write(channel, LogLevel.Warn, message);

        public static void Error(in LogChannel channel, string message) => Write(channel, LogLevel.Error, message);

        public static void Flush()
        {
            ILogSink[] sinks = _sinks;
            for (int i = 0; i < sinks.Length; i++) Safely(sinks[i], s => s.Flush());
        }

        private static void Write(in LogChannel channel, LogLevel level, string message)
        {
            // Cheapest check first: the kill switch, then whether anyone is listening at all.
            if (!Enabled) return;

            ILogSink[] sinks = _sinks;
            if (sinks.Length == 0) return;

            if (level < Channels.LevelOf(channel)) return;

            var record = new LogRecord(level, channel.Name, Day, message);
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
