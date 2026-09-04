using System;
using System.IO;
using System.Linq;
using RTS.Content.Loading;
using RTS.Content.Validation;
using RTS.Sim.Engine.Diagnostics;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// <see cref="Log"/> is static, so every test here restores it. NUnit runs fixtures in
    /// parallel across assemblies but not within one by default, and these deliberately do not
    /// opt in — shared mutable state is the price of a logger that call sites will actually use.
    /// </summary>
    [Category(TestCategories.Unit)]
    [NonParallelizable]
    public class LogTests
    {
        private CaptureLogSink _sink = null!;

        [SetUp]
        public void SetUp()
        {
            Log.ClearSinks();
            Log.Enabled = true;
            Log.Day = 0;
            Log.DefaultLevel = LogLevel.Trace;
            Log.SetAllLevels(LogLevel.Trace);

            _sink = new CaptureLogSink();
            Log.AddSink(_sink);
        }

        [TearDown]
        public void TearDown()
        {
            Log.ClearSinks();
            Log.Enabled = true;
            Log.DefaultLevel = LogLevel.Info;
            Log.SetAllLevels(LogLevel.Info);
            Log.Day = 0;
        }

        private static LogChannel Ch(string name) => Log.Channel(name);

        // ------------------------------------------------------------- kill switch

        [Test]
        public void Disabling_silences_everything()
        {
            LogChannel channel = Ch("KillSwitch");
            Log.Enabled = false;

            Log.Error(channel, "should not appear");

            Assert.That(_sink.Count, Is.EqualTo(0));
            Assert.That(Log.On(channel, LogLevel.Error), Is.False,
                "the guard must agree with the switch, or callers still pay for formatting");
        }

        [Test]
        public void Re_enabling_resumes_without_losing_configuration()
        {
            LogChannel channel = Ch("Resume");
            Log.SetLevel(channel, LogLevel.Warn);

            Log.Enabled = false;
            Log.Warn(channel, "dropped");
            Log.Enabled = true;
            Log.Warn(channel, "kept");
            Log.Info(channel, "still below the threshold");

            Assert.That(_sink.Snapshot().Select(r => r.Message), Is.EqualTo(new[] { "kept" }));
        }

        // ------------------------------------------------------------------ levels

        [Test]
        public void A_channel_logs_at_its_level_and_above()
        {
            LogChannel channel = Ch("Levels");
            Log.SetLevel(channel, LogLevel.Warn);

            Log.Trace(channel, "no");
            Log.Debug(channel, "no");
            Log.Info(channel, "no");
            Log.Warn(channel, "yes");
            Log.Error(channel, "yes");

            Assert.That(_sink.Snapshot().Select(r => r.Message), Is.EqualTo(new[] { "yes", "yes" }));
        }

        [Test]
        public void Off_silences_one_channel_and_leaves_the_others()
        {
            LogChannel quiet = Ch("Quiet");
            LogChannel loud = Ch("Loud");
            Log.SetLevel(quiet, LogLevel.Off);

            Log.Error(quiet, "silenced");
            Log.Info(loud, "heard");

            Assert.That(_sink.Snapshot().Select(r => r.Channel), Is.EqualTo(new[] { "Loud" }));
        }

        [Test]
        public void An_unconfigured_channel_takes_the_default()
        {
            Log.DefaultLevel = LogLevel.Warn;
            LogChannel fresh = Ch("NeverConfigured");

            Log.Info(fresh, "below");
            Log.Error(fresh, "above");

            Assert.That(_sink.Snapshot().Select(r => r.Message), Is.EqualTo(new[] { "above" }));
        }

        [Test]
        public void On_is_false_when_no_sink_is_listening()
        {
            // Nothing to format for. A headless run with no sinks should cost nothing.
            Log.ClearSinks();

            Assert.That(Log.On(Ch("Nobody"), LogLevel.Error), Is.False);
        }

        // ---------------------------------------------------------------- channels

        [Test]
        public void The_same_name_resolves_to_the_same_channel()
        {
            LogChannel first = Ch("Repeat");
            LogChannel again = Ch("Repeat");

            Assert.That(again, Is.EqualTo(first));
            Assert.That(Ch("Different"), Is.Not.EqualTo(first));
        }

        [Test]
        public void Channel_names_are_case_sensitive()
        {
            Assert.That(Ch("Case"), Is.Not.EqualTo(Ch("case")));
        }

        [Test]
        public void A_nameless_channel_is_refused()
        {
            Assert.Throws<ArgumentException>(() => Log.Channel(""));
            Assert.Throws<ArgumentException>(() => Log.Channel("   "));
            Assert.Throws<ArgumentException>(() => Log.Channel(null));
        }

        [Test]
        public void Records_carry_the_channel_level_and_day()
        {
            Log.Day = 12;
            Log.Warn(Ch("Content"), "goods.csv(31): out of range");

            LogRecord record = _sink.Snapshot().Single();

            Assert.That(record.Channel, Is.EqualTo("Content"));
            Assert.That(record.Level, Is.EqualTo(LogLevel.Warn));
            Assert.That(record.Day, Is.EqualTo(12));
        }

        // ------------------------------------------------------------------- sinks

        [Test]
        public void Every_sink_receives_every_line()
        {
            var second = new CaptureLogSink();
            Log.AddSink(second);

            Log.Info(Ch("Fanout"), "one");

            Assert.That(_sink.Count, Is.EqualTo(1));
            Assert.That(second.Count, Is.EqualTo(1));
        }

        [Test]
        public void A_sink_that_throws_is_dropped_rather_than_taking_the_sim_down()
        {
            Log.AddSink(new ThrowingSink());

            Assert.DoesNotThrow(() => Log.Info(Ch("Throwing"), "first"));

            Assert.That(Log.SinkCount, Is.EqualTo(1), "the throwing sink is gone");
            Assert.DoesNotThrow(() => Log.Info(Ch("Throwing"), "second"));
            Assert.That(_sink.Count, Is.EqualTo(2), "the healthy sink kept working");
        }

        [Test]
        public void Removing_a_sink_stops_it_receiving()
        {
            Assert.That(Log.RemoveSink(_sink), Is.True);

            Log.Error(Ch("Removed"), "nobody home");

            Assert.That(_sink.Count, Is.EqualTo(0));
            Assert.That(Log.RemoveSink(_sink), Is.False);
        }

        [Test]
        public void The_capture_sink_drops_the_oldest_rather_than_growing_without_bound()
        {
            Log.ClearSinks();
            var small = new CaptureLogSink(capacity: 3);
            Log.AddSink(small);

            LogChannel channel = Ch("Bounded");
            for (int i = 0; i < 5; i++) Log.Info(channel, i.ToString());

            Assert.That(small.Count, Is.EqualTo(3));
            Assert.That(small.Dropped, Is.EqualTo(2));
            Assert.That(small.Snapshot().Select(r => r.Message), Is.EqualTo(new[] { "2", "3", "4" }));
        }

        // ----------------------------------------------------------------- format

        [Test]
        public void The_line_format_has_fixed_width_leading_fields()
        {
            // A reader filters on these columns, so they must not move.
            string line = TextWriterLogSink.Format(
                new LogRecord(LogLevel.Warn, "Content", 12, "goods.csv(31): out of range"), 42.7d);

            Assert.That(line, Is.EqualTo("[00042.7][Day   12][WARN ][Content ] goods.csv(31): out of range"));
        }

        [TestCase(LogLevel.Trace, "TRACE")]
        [TestCase(LogLevel.Debug, "DEBUG")]
        [TestCase(LogLevel.Info, "INFO ")]
        [TestCase(LogLevel.Warn, "WARN ")]
        [TestCase(LogLevel.Error, "ERROR")]
        public void Every_level_abbreviates_to_five_characters(LogLevel level, string expected)
        {
            string line = TextWriterLogSink.Format(new LogRecord(level, "X", 1, "m"), 0d);

            Assert.That(line, Does.Contain("[" + expected + "]"));
            Assert.That(expected.Length, Is.EqualTo(5));
        }

        [Test]
        public void A_long_channel_name_is_not_truncated_even_though_it_widens_the_column()
        {
            // Losing the name would be worse than losing the alignment: a filter needs the
            // whole name to match on.
            string line = TextWriterLogSink.Format(
                new LogRecord(LogLevel.Info, "AVeryLongChannelName", 1, "m"), 0d);

            Assert.That(line, Does.Contain("[AVeryLongChannelName]"));
        }

        [Test]
        public void The_elapsed_stamp_is_invariant_of_machine_locale()
        {
            // A decimal comma would break every reader that splits on the fields.
            string line = TextWriterLogSink.Format(new LogRecord(LogLevel.Info, "X", 0, "m"), 1234.5d);

            Assert.That(line, Does.StartWith("[01234.5]"));
        }

        [Test]
        public void The_writer_sink_emits_one_line_per_record()
        {
            var text = new StringWriter();
            var sink = new TextWriterLogSink(text, () => 1.0d);

            sink.Write(new LogRecord(LogLevel.Info, "A", 1, "first"));
            sink.Write(new LogRecord(LogLevel.Error, "B", 2, "second"));
            sink.Flush();

            string[] lines = text.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

            Assert.That(lines.Length, Is.EqualTo(2));
            Assert.That(lines[0], Does.EndWith("first"));
            Assert.That(lines[1], Does.EndWith("second"));
        }

        // --------------------------------------------------------------- settings

        private static LogSettings LoadSettings(string body, out ValidationReport report)
        {
            report = new ValidationReport();
            return LogSettings.Load(CsvTable.Parse("channel,level\n" + body, "logging.csv"), report);
        }

        [Test]
        public void Settings_read_channel_levels_and_the_default()
        {
            LogSettings settings = LoadSettings("*,Warn\nContent,Debug\nPipeline,Off\n", out ValidationReport report);

            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Problems));
            Assert.That(settings.DefaultLevel, Is.EqualTo(LogLevel.Warn));
            Assert.That(settings.Levels.Count, Is.EqualTo(2));
        }

        [Test]
        public void Applying_settings_changes_what_is_logged()
        {
            LogSettings settings = LoadSettings("*,Error\nApplied,Debug\n", out ValidationReport _);
            settings.Apply();

            LogChannel applied = Ch("Applied");
            LogChannel other = Ch("SomethingElse");

            Log.Debug(applied, "configured channel");
            Log.Info(other, "below the new default");
            Log.Error(other, "above it");

            Assert.That(_sink.Snapshot().Select(r => r.Message),
                Is.EqualTo(new[] { "configured channel", "above it" }));
        }

        [Test]
        public void A_misspelled_level_is_a_load_failure_not_a_silent_off()
        {
            LoadSettings("Content,Verbose\n", out ValidationReport report);

            Assert.That(report.Count, Is.EqualTo(1));
            Assert.That(report.Problems.Single(), Does.Contain("Expected one of"));
            Assert.That(report.Problems.Single(), Does.Contain("logging.csv(2)"));
        }

        [Test]
        public void A_channel_configured_twice_is_reported()
        {
            LoadSettings("Content,Info\nContent,Debug\n", out ValidationReport report);

            Assert.That(report.Problems.Single(), Does.Contain("already configured on line 2"));
        }

        private sealed class ThrowingSink : ILogSink
        {
            public void Write(in LogRecord record) => throw new InvalidOperationException("sink is broken");

            public void Flush() => throw new InvalidOperationException("sink is broken");
        }
    }
}
