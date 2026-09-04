using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RTS.Sim.Engine.Diagnostics;

namespace RTS.Game.Diagnostics
{
    /// <summary>
    /// Writes the log to a timestamped file, keeping the last few runs.
    /// </summary>
    /// <remarks>
    /// This is the artefact the workflow is built around: engine work happens without opening
    /// the editor, so the file — not the console — is the record, and a reader filters it by
    /// the fixed-width channel and level columns.
    /// <para>
    /// One file per run rather than one appended file, because "what happened in that session"
    /// is the question actually asked, and a single growing file makes it hard to answer.
    /// Old files are pruned so the folder does not grow without bound.
    /// </para>
    /// </remarks>
    public sealed class FileLogSink : ILogSink, IDisposable
    {
        public const string FilePrefix = "rts_";
        public const string FileExtension = ".log";

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly TextWriterLogSink _formatter;
        private readonly StreamWriter _writer;

        private bool _disposed;

        private FileLogSink(StreamWriter writer, string path)
        {
            _writer = writer;
            Path = path;
            _formatter = new TextWriterLogSink(writer, () => _clock.Elapsed.TotalSeconds);
        }

        /// <summary>The file being written.</summary>
        public string Path { get; }

        /// <summary>
        /// Opens a new log file in <paramref name="directory"/>, pruning older ones.
        /// </summary>
        /// <param name="keep">How many files to leave behind, including this one.</param>
        public static FileLogSink Open(string directory, int keep = 10)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("A directory is required.", nameof(directory));

            System.IO.Directory.CreateDirectory(directory);

            // UTC and sortable, so the folder sorts chronologically and two machines' logs do
            // not disagree about which came first.
            string name = FilePrefix +
                          DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) +
                          FileExtension;

            string path = System.IO.Path.Combine(directory, name);

            // Unique-ify rather than overwrite: two runs started in the same second would
            // otherwise silently share, and the second would truncate the first.
            int suffix = 1;
            while (File.Exists(path))
            {
                path = System.IO.Path.Combine(directory,
                    FilePrefix + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) +
                    "_" + suffix.ToString(CultureInfo.InvariantCulture) + FileExtension);
                suffix++;
            }

            // FileShare.ReadWrite, not Read: a reader tool must be able to tail this file while
            // the game is still writing it, and Unity's runtime refuses a plain File.ReadAllText
            // against a Read-shared handle with a sharing violation.
            var writer = new StreamWriter(
                new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var sink = new FileLogSink(writer, path);
            Prune(directory, keep);
            return sink;
        }

        /// <summary>
        /// Deletes all but the newest <paramref name="keep"/> log files. Failures are ignored:
        /// a locked or unreadable old log is not worth failing a launch over.
        /// </summary>
        public static int Prune(string directory, int keep)
        {
            if (keep < 1) keep = 1;
            if (!System.IO.Directory.Exists(directory)) return 0;

            FileInfo[] logs;
            try
            {
                logs = new DirectoryInfo(directory)
                    .GetFiles(FilePrefix + "*" + FileExtension)
                    // By name, not by timestamp: the name is sortable by construction, and file
                    // times can be rewritten by a copy or a sync tool.
                    .OrderByDescending(f => f.Name, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (IOException)
            {
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                return 0;
            }

            int deleted = 0;
            for (int i = keep; i < logs.Length; i++)
            {
                try
                {
                    logs[i].Delete();
                    deleted++;
                }
                catch (IOException)
                {
                    // In use, most likely by another running instance. Leave it.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return deleted;
        }

        public void Write(in LogRecord record)
        {
            if (_disposed) return;

            _formatter.Write(record);
        }

        public void Flush()
        {
            if (_disposed) return;

            _formatter.Flush();
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            try
            {
                _writer.Flush();
                _writer.Dispose();
            }
            catch (IOException)
            {
                // Nothing useful to do while shutting down.
            }
        }
    }
}
