using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace RTS.Sim.Engine.Diagnostics
{
    /// <summary>
    /// Formats records as fixed-width lines and writes them to any <see cref="TextWriter"/>.
    /// </summary>
    /// <remarks>
    /// The format exists to be filtered by a reader or by grep, so the leading fields are
    /// fixed width and never move:
    /// <code>
    /// [00042.7][Day  12][WARN ][Content ] goods.csv(31): volatility 1.4 outside 0..1
    /// </code>
    /// which makes <c>findstr /C:"[Content "</c> or <c>grep '\[WARN '</c> work with no parser.
    /// <para>
    /// Knowing nothing about files is the point: Unity-side code opens the file, or the console,
    /// and hands over a writer. That keeps path resolution out of <c>Sim</c> (C5) and makes the
    /// formatting testable against a StringWriter.
    /// </para>
    /// <para>
    /// The elapsed-seconds stamp comes from a caller-supplied function, so tests can pass a
    /// fixed one and the sim never reads a clock itself (§7.1).
    /// </para>
    /// </remarks>
    public sealed class TextWriterLogSink : ILogSink
    {
        private const int ChannelWidth = 8;

        private readonly TextWriter _writer;
        private readonly Func<double> _elapsedSeconds;
        private readonly bool _autoFlush;
        private readonly object _gate = new object();
        private readonly StringBuilder _line = new StringBuilder(160);

        public TextWriterLogSink(TextWriter writer, Func<double> elapsedSeconds = null, bool autoFlush = false)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _elapsedSeconds = elapsedSeconds ?? (() => 0d);
            _autoFlush = autoFlush;
        }

        public void Write(in LogRecord record)
        {
            string line = Format(record, _elapsedSeconds());

            // Sinks are shared; two threads writing half-lines into one file is a class of
            // corruption that is very annoying to diagnose from the file afterwards.
            lock (_gate)
            {
                _writer.Write(line);
                _writer.Write('\n');
                if (_autoFlush) _writer.Flush();
            }
        }

        public void Flush()
        {
            lock (_gate) _writer.Flush();
        }

        /// <summary>The exact line, exposed so tests and a reader tool agree on the format.</summary>
        public static string Format(in LogRecord record, double elapsedSeconds)
        {
            var text = new StringBuilder(160);

            text.Append('[')
                .Append(elapsedSeconds.ToString("00000.0", CultureInfo.InvariantCulture))
                .Append("][Day ")
                .Append(record.Day.ToString(CultureInfo.InvariantCulture).PadLeft(4))
                .Append("][")
                .Append(Abbreviate(record.Level))
                .Append("][")
                .Append(Pad(record.Channel ?? "?", ChannelWidth))
                .Append("] ")
                .Append(record.Message ?? string.Empty);

            return text.ToString();
        }

        /// <summary>Five characters, always, so the column does not move.</summary>
        private static string Abbreviate(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Trace: return "TRACE";
                case LogLevel.Debug: return "DEBUG";
                case LogLevel.Info: return "INFO ";
                case LogLevel.Warn: return "WARN ";
                case LogLevel.Error: return "ERROR";
                default: return "OFF  ";
            }
        }

        private static string Pad(string value, int width) =>
            value.Length >= width ? value : value.PadRight(width);
    }
}
