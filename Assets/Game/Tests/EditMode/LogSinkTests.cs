using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RTS.Game.Boot;
using RTS.Game.Diagnostics;
using RTS.Sim.Engine.Diagnostics;

namespace RTS.Game.Tests
{
    /// <summary>
    /// The Unity-side sinks. Headless tests cover the format and the facade; these cover what
    /// they cannot — real files, real paths, and Unity's console.
    /// </summary>
    [Category("Functional")]
    public class LogSinkTests
    {
        private string _directory = null!;

        [SetUp]
        public void SetUp()
        {
            // A temp directory, not persistentDataPath: a test must not prune the logs of a
            // real session or leave files behind in one.
            _directory = Path.Combine(Path.GetTempPath(), "rts-log-tests-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        /// <summary>Reads a file that something else still has open for writing.</summary>
        private static string ReadWhileOpen(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        private string[] LogFiles() =>
            Directory.Exists(_directory)
                ? Directory.GetFiles(_directory, FileLogSink.FilePrefix + "*" + FileLogSink.FileExtension)
                    .OrderBy(f => f, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();

        [Test]
        public void Opening_creates_a_file_and_writing_lands_in_it()
        {
            using (FileLogSink sink = FileLogSink.Open(_directory))
            {
                sink.Write(new LogRecord(LogLevel.Warn, LogChannel.Content, 12, "something to find"));
                sink.Flush();

                Assert.That(File.Exists(sink.Path), Is.True, sink.Path);

                // Read the way a tail tool must: File.ReadAllText opens with FileShare.Read,
                // which forbids a concurrent writer, so it fails against a live log with a
                // sharing violation. Sharing ReadWrite is what lets a reader follow the file
                // while the game is still writing it — the whole point of the file sink.
                string text = ReadWhileOpen(sink.Path);
                Assert.That(text, Does.Contain("something to find"));
                Assert.That(text, Does.Contain("[WARN ]"));
                Assert.That(text, Does.Contain("[Content "));
                Assert.That(text, Does.Contain("[Day   12]"));
            }
        }

        [Test]
        public void The_directory_is_created_if_it_does_not_exist()
        {
            Assert.That(Directory.Exists(_directory), Is.False);

            using (FileLogSink.Open(_directory))
            {
                Assert.That(Directory.Exists(_directory), Is.True);
            }
        }

        [Test]
        public void Each_run_gets_its_own_file()
        {
            using (FileLogSink first = FileLogSink.Open(_directory))
            using (FileLogSink second = FileLogSink.Open(_directory))
            {
                Assert.That(second.Path, Is.Not.EqualTo(first.Path),
                    "two runs in the same second must not share a file; the second would truncate the first");
            }

            Assert.That(LogFiles().Length, Is.EqualTo(2));
        }

        [Test]
        public void Pruning_keeps_the_newest_and_deletes_the_rest()
        {
            Directory.CreateDirectory(_directory);

            // Names are sortable by construction, so pruning can rely on them rather than on
            // file timestamps, which a copy or a sync tool can rewrite.
            string[] names =
            {
                "rts_20260101_000000.log", "rts_20260102_000000.log", "rts_20260103_000000.log",
                "rts_20260104_000000.log", "rts_20260105_000000.log",
            };

            foreach (string name in names) File.WriteAllText(Path.Combine(_directory, name), "x");

            int deleted = FileLogSink.Prune(_directory, keep: 2);

            Assert.That(deleted, Is.EqualTo(3));
            Assert.That(LogFiles().Select(Path.GetFileName),
                Is.EqualTo(new[] { "rts_20260104_000000.log", "rts_20260105_000000.log" }));
        }

        [Test]
        public void Pruning_leaves_files_that_are_not_ours_alone()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, "rts_20260101_000000.log"), "x");
            File.WriteAllText(Path.Combine(_directory, "notes.txt"), "important");

            FileLogSink.Prune(_directory, keep: 0);

            Assert.That(File.Exists(Path.Combine(_directory, "notes.txt")), Is.True,
                "a log pruner must not delete things it did not write");
        }

        [Test]
        public void Pruning_a_missing_directory_is_harmless()
        {
            Assert.DoesNotThrow(() => FileLogSink.Prune(Path.Combine(_directory, "nope"), keep: 3));
        }

        [Test]
        public void Opening_keeps_only_the_configured_number_of_runs()
        {
            Directory.CreateDirectory(_directory);
            for (int i = 1; i <= 4; i++)
                File.WriteAllText(Path.Combine(_directory, $"rts_2026010{i}_000000.log"), "x");

            using (FileLogSink.Open(_directory, keep: 3))
            {
                Assert.That(LogFiles().Length, Is.EqualTo(3), "the new file counts toward the limit");
            }
        }

        [Test]
        public void Writing_after_dispose_does_nothing_rather_than_throwing()
        {
            FileLogSink sink = FileLogSink.Open(_directory);
            sink.Dispose();

            Assert.DoesNotThrow(() => sink.Write(new LogRecord(LogLevel.Error, LogChannel.Boot, 1, "late")));
            Assert.DoesNotThrow(() => sink.Flush());
            Assert.DoesNotThrow(() => sink.Dispose());
        }

        [Test]
        public void The_console_sink_maps_levels_onto_unity_severities()
        {
            var sink = new UnityConsoleLogSink();

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("warn line"));
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("error line"));

            sink.Write(new LogRecord(LogLevel.Warn, LogChannel.Game, 1, "warn line"));
            sink.Write(new LogRecord(LogLevel.Error, LogChannel.Game, 1, "error line"));
        }

        [Test]
        public void The_console_sink_drops_everything_below_its_floor()
        {
            // The file is the record of what the engine did; the console is for noticing that
            // something is wrong while the editor happens to be open. A console carrying every
            // day boundary is a console nobody reads, and a real warning scrolls past unseen.
            //
            // LogAssert fails the test on any unexpected console entry, so writing these
            // without expecting them is the assertion.
            var sink = new UnityConsoleLogSink();

            Assert.That(sink.Minimum, Is.EqualTo(LogLevel.Warn));

            sink.Write(new LogRecord(LogLevel.Trace, LogChannel.Pipeline, 1, "trace line"));
            sink.Write(new LogRecord(LogLevel.Debug, LogChannel.Pipeline, 1, "debug line"));
            sink.Write(new LogRecord(LogLevel.Info, LogChannel.Game, 1, "info line"));
        }

        [Test]
        public void The_console_floor_can_be_lowered_when_something_is_being_hunted()
        {
            var sink = new UnityConsoleLogSink(LogLevel.Debug);

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("debug line"));

            sink.Write(new LogRecord(LogLevel.Trace, LogChannel.Pipeline, 1, "trace line"));
            sink.Write(new LogRecord(LogLevel.Debug, LogChannel.Pipeline, 1, "debug line"));
        }

        [Test]
        public void The_shipped_logging_config_is_where_boot_looks_for_it()
        {
            // LogBoot degrades quietly if this is missing, so nothing at runtime would complain.
            string path = ConfigFiles.PathTo(ConfigFiles.LoggingFile);

            Assert.That(File.Exists(path), Is.True, path);
        }
    }
}
