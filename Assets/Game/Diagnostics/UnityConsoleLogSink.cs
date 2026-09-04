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
        public UnityConsoleLogSink(LogLevel minimum = LogLevel.Warn)
        {
            _minimum = minimum;
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

        public void Write(in LogRecord record)
        {
            if (record.Level < _minimum) return;

            string line = TextWriterLogSink.Format(record, 0d);

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
