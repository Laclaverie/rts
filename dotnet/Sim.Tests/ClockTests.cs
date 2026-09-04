using System.IO;
using System.Linq;
using RTS.Content.Loading;
using RTS.Content.Validation;
using RTS.Sim.Engine.Time;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// The boundary between real time and the simulation (GDD §3.2, §5.1).
    /// </summary>
    /// <remarks>
    /// Everything here is arithmetic on purpose. The clock is the only place a frame rate is
    /// allowed to exist, and the property worth protecting is that nothing about it reaches the
    /// world: it hands back whole days and no more.
    /// </remarks>
    [Category(TestCategories.Unit)]
    public class ClockTests
    {
        private static Clock Make(float secondsPerDay = 10f, params int[] speeds) =>
            new Clock(secondsPerDay, speeds.Length > 0 ? speeds : new[] { 1, 2, 4 });

        // --------------------------------------------------------------- counting

        [Test]
        public void Time_below_a_day_advances_nothing()
        {
            var clock = Make();

            Assert.That(clock.Advance(9.9f), Is.Zero);
        }

        [Test]
        public void A_day_of_real_time_is_a_day()
        {
            var clock = Make();

            Assert.That(clock.Advance(10f), Is.EqualTo(1));
        }

        [Test]
        public void The_remainder_is_kept_so_the_boundary_does_not_drift()
        {
            // Sixty frames of a sixtieth of a second must advance exactly as far as one frame of
            // a second. Dropping the remainder would make the day length depend on the frame
            // rate, which is the kind of thing that is invisible until a save disagrees.
            var stutter = Make();
            var smooth = Make();

            int stuttered = 0;
            for (int i = 0; i < 600; i++) stuttered += stutter.Advance(1f / 60f);

            int smoothed = smooth.Advance(10f);

            Assert.That(stuttered, Is.EqualTo(1));
            Assert.That(smoothed, Is.EqualTo(1));
        }

        [Test]
        public void Several_days_can_pass_in_one_call()
        {
            var clock = Make();

            Assert.That(clock.Advance(30f), Is.EqualTo(3));
        }

        [Test]
        public void Speed_multiplies_the_time_that_passes()
        {
            var clock = Make();
            clock.Speed = 4;

            Assert.That(clock.Advance(10f), Is.EqualTo(4));
        }

        [Test]
        public void Progress_through_the_day_is_readable()
        {
            var clock = Make();
            clock.Advance(5f);

            Assert.That(clock.DayProgress, Is.EqualTo(0.5f).Within(1e-4f));
        }

        // ------------------------------------------------------------------ pause

        [Test]
        public void A_paused_clock_advances_nothing_however_long_it_waits()
        {
            // §3.2: pause is the mechanism that separates decision complexity from reaction
            // speed. A paused game that quietly banked its time would hand the whole backlog
            // over the moment the player resumed, which is the opposite of thinking time.
            var clock = Make();
            clock.Pause();

            Assert.That(clock.Advance(1000f), Is.Zero);

            clock.Resume();

            Assert.That(clock.Advance(1f), Is.Zero, "and it banked nothing while it waited");
        }

        [Test]
        public void Pausing_keeps_the_progress_already_made()
        {
            // Otherwise pausing would cost the player part of a day, and a habit of pausing to
            // think would slow the game down in a way nobody asked for.
            var clock = Make();
            clock.Advance(6f);

            clock.Pause();
            clock.Advance(100f);
            clock.Resume();

            Assert.That(clock.Advance(4f), Is.EqualTo(1), "six seconds plus four is still a day");
        }

        [Test]
        public void Pause_toggles()
        {
            var clock = Make();

            clock.TogglePause();
            Assert.That(clock.Paused, Is.True);

            clock.TogglePause();
            Assert.That(clock.Paused, Is.False);
        }

        // ------------------------------------------------------------------ stalls

        [Test]
        public void A_stalled_frame_cannot_fast_forward_the_world()
        {
            // A breakpoint, a dragged window or a closed laptop lid all arrive as one enormous
            // delta. Running it would silently advance the port a fortnight while nobody was
            // watching, which is indistinguishable from a bug.
            var clock = Make();

            Assert.That(clock.Advance(10000f), Is.EqualTo(Clock.MaximumDaysPerAdvance));
        }

        [Test]
        public void Time_lost_to_a_stall_is_dropped_rather_than_banked()
        {
            // Paying it back over the following frames would leave the port racing for no
            // visible reason long after the stall ended. The clock paces; it does not account.
            var clock = Make();
            clock.Advance(10000f);

            Assert.That(clock.Advance(1f), Is.Zero);
        }

        // ------------------------------------------------------------------ config

        private static Clock Load(string csv, out ValidationReport report)
        {
            report = new ValidationReport();
            return Clock.Load(CsvTable.Parse(csv, "clock.csv"), report);
        }

        [Test]
        public void The_shipped_file_loads()
        {
            // Deliberately not pinned to the GDD's 1200. The whole reason this is a file is
            // that a playtest drops it to ten seconds a day without a rebuild, and a test that
            // failed whenever somebody did that would teach them to stop running the tests.
            // §5.1's twenty minutes is recorded in the file's own comments, where the person
            // changing it will read it.
            string path = Path.Combine(
                TestContext.CurrentContext.TestDirectory, "Config", "clock.csv");

            var report = new ValidationReport();
            Clock clock = Clock.Load(
                CsvTable.Parse(File.ReadAllText(path), "clock.csv"), report);

            report.ThrowIfInvalid();

            Assert.That(clock.SecondsPerDay, Is.GreaterThan(0f));
            Assert.That(clock.Speeds, Is.Not.Empty);
            Assert.That(clock.Speed, Is.EqualTo(clock.Speeds[0]));
        }

        [Test]
        public void A_clock_starts_at_the_slowest_speed_offered()
        {
            Clock clock = Load("key,value\nseconds_per_day,10\nspeeds,1;2;4\n", out _);

            Assert.That(clock.Speed, Is.EqualTo(1));
        }

        [Test]
        public void An_unknown_setting_is_rejected_rather_than_ignored()
        {
            // A setting that silently keeps its default is worse than one that fails to load:
            // the file says one thing and the game does another, and nothing says so.
            Load("key,value\nseconds_per_day,10\nsecnds_per_day,5\n", out ValidationReport report);

            Assert.That(report.Problems.Any(p => p.Contains("not a clock setting")), Is.True,
                string.Join("; ", report.Problems));
        }

        [Test]
        public void A_setting_given_twice_is_rejected()
        {
            Load("key,value\nseconds_per_day,10\nseconds_per_day,20\n", out ValidationReport report);

            Assert.That(report.Problems.Any(p => p.Contains("set twice")), Is.True,
                string.Join("; ", report.Problems));
        }

        [Test]
        public void A_day_that_takes_no_time_is_rejected()
        {
            Load("key,value\nseconds_per_day,0\n", out ValidationReport report);

            Assert.That(report.Problems.Any(p => p.Contains("positive number of seconds")), Is.True,
                string.Join("; ", report.Problems));
        }

        [Test]
        public void Speeds_out_of_order_are_rejected()
        {
            // They are drawn as buttons in this order, and a row reading "x4 x1 x2" would be a
            // content mistake showing up as a user-interface one.
            Load("key,value\nseconds_per_day,10\nspeeds,4;1;2\n", out ValidationReport report);

            Assert.That(report.Problems.Any(p => p.Contains("ascending order")), Is.True,
                string.Join("; ", report.Problems));
        }

        [Test]
        public void A_clock_with_no_speeds_is_rejected()
        {
            Load("key,value\nseconds_per_day,10\nspeeds,\n", out ValidationReport report);

            Assert.That(report.Problems.Any(), Is.True);
        }
    }
}
