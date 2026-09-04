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
        private readonly LogLevel _minimumForWarning;

        public UnityConsoleLogSink(LogLevel minimumForWarning = LogLevel.Warn)
        {
            _minimumForWarning = minimumForWarning;
        }

        public void Write(in LogRecord record)
        {
            string line = TextWriterLogSink.Format(record, 0d);

            // Unity's console colours and filters by these three, and its Error entries are
            // what break a CI build or catch an eye in the editor. Mapping anything below Warn
            // onto LogError would make the filter useless.
            if (record.Level >= LogLevel.Error) UnityEngine.Debug.LogError(line);
            else if (record.Level >= _minimumForWarning) UnityEngine.Debug.LogWarning(line);
            else UnityEngine.Debug.Log(line);
        }

        public void Flush()
        {
            // The console has no buffer of ours to push.
        }
    }
}
