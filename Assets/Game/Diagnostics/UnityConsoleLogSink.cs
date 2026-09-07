using System;
using RTS.Sim.Engine.Diagnostics;
using UnityEngine;

namespace RTS.Game.Diagnostics
{
    /// <summary>
    /// Writes to the Unity console, mapping levels onto Unity's three.
    /// </summary>
    /// <remarks>
    /// Same line format as the file sink, so a line copied out of the console can be grepped
    /// against the log file and vice versa.
    /// <para>
    /// The elapsed stamp is omitted here: the console has its own timestamps and its own
    /// ordering, and the column would be noise on screen. The file is the record; this is for
    /// noticing something while the editor happens to be open.
    /// </para>
    /// </remarks>
    public sealed class UnityConsoleLogSink : ILogSink
    {
        private readonly LogLevel _minimum;

        /// <param name="minimum">
        /// Everything below this is dropped rather than written. The default is
        /// <see cref="LogLevel.Warn"/>: only what is wrong is worth a console entry.
        /// </param>
        /// <param name="elapsedSeconds">
        /// How long the session has been running. Optional, and zero when absent — a test
        /// asserting the format wants a fixed clock, not a real one.
        /// </param>
        public UnityConsoleLogSink(LogLevel minimum = LogLevel.Warn,
            Func<double> elapsedSeconds = null)
        {
            _minimum = minimum;
            _elapsedSeconds = elapsedSeconds ?? (() => 0d);
        }

        /// <summary>
        /// The level below which nothing reaches the console.
        /// </summary>
        /// <remarks>
        /// The two sinks answer different questions and so deserve different thresholds. The
        /// file is the record — every system that ran, every command queued and applied, in
        /// order, so a question about what the engine did has an answer. The console is for
        /// noticing that something is wrong while the editor happens to be open, and a console
        /// carrying the whole day boundary is a console nobody reads, which means a real warning
        /// scrolls past unseen.
        /// </remarks>
        public LogLevel Minimum => _minimum;

        /// <summary>How long the session has been running, for the leading column.</summary>
        /// <remarks>
        /// Supplied rather than measured here, so the console and the log file agree about what
        /// time it is. A reader comparing the two should not have to wonder which zero is which.
        /// </remarks>
        private readonly Func<double> _elapsedSeconds;

        public void Write(in LogRecord record)
        {
            if (record.Level < _minimum) return;

            // The real clock, not a hardcoded zero. Every console line used to begin
            // "[00000.0]" — a fixed-width column of nothing, in the one place a reader is
            // skimming for something wrong. It now says how far into the session the line was
            // written, which is the question you actually have when a warning appears.
            string line = TextWriterLogSink.Format(record, _elapsedSeconds());

            // Unity's console colours and filters by these three, and its Error entries are
            // what break a CI build or catch an eye in the editor. Mapping anything below Warn
            // onto LogError would make the filter useless.
            if (record.Level >= LogLevel.Error) UnityEngine.Debug.LogError(line);
            else UnityEngine.Debug.LogWarning(line);
        }

        public void Flush()
        {
            // The console has no buffer of ours to push.
        }
    }
}
