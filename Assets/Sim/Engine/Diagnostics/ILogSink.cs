namespace RTS.Sim.Engine.Diagnostics
{
    /// <summary>One log line, as the sim knows it.</summary>
    /// <remarks>
    /// No timestamp. <c>Sim</c> may not read a clock (§7.1), so the wall clock is the sink's
    /// business — it is presentation, added on the way out. The in-game <see cref="Day"/> is
    /// here instead, because that is the one a reader actually asks about.
    /// </remarks>
    public readonly struct LogRecord
    {
        public LogRecord(LogLevel level, LogChannel channel, int day, string message)
        {
            Level = level;
            Channel = channel;
            Day = day;
            Message = message;
        }

        public readonly LogLevel Level;
        public readonly LogChannel Channel;
        public readonly int Day;
        public readonly string Message;
    }

    /// <summary>
    /// Somewhere log lines go: a file, the Unity console, a test's list.
    /// </summary>
    /// <remarks>
    /// Sinks live outside <c>Sim</c> wherever they touch the machine — the file and console
    /// sinks are Unity-side. This interface is here so systems can log without knowing that.
    /// <para>
    /// A sink must not throw. A logging failure that takes down the sim would be a worse bug
    /// than whatever was being logged; <see cref="Log"/> swallows and disables a sink that
    /// throws rather than letting it propagate.
    /// </para>
    /// </remarks>
    public interface ILogSink
    {
        void Write(in LogRecord record);

        /// <summary>Pushes anything buffered. Called on shutdown, and after an Error.</summary>
        void Flush();
    }
}
