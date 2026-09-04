using System.IO;
using System.Linq;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Time;
using RTS.Sim.Session;
using RTS.Sim.Systems;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// The event feed (BUILD_ORDER Phase 3; ARCHITECTURE §6.2).
    /// </summary>
    /// <remarks>
    /// At twenty minutes a day the player looks away, and what they need on returning is the
    /// story rather than the numbers. This is also the first thing to consume the causal DAG,
    /// which §6.2 built months early on the grounds that it could not be reconstructed later.
    /// </remarks>
    [Category(TestCategories.Functional)]
    public class EventFeedTests
    {
        private static string BalancePath(string file) =>
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Balance", file);

        private static CsvTable Table(string file) =>
            CsvTable.Parse(File.ReadAllText(BalancePath(file)), file);

        private static BalanceTables Balance()
        {
            var report = new ValidationReport();
            BalanceTables tables = BalanceTables.Load(new BalanceSources
            {
                Goods = Table(BalanceTables.GoodsFile),
                Buildings = Table(BalanceTables.BuildingsFile),
                CrewRoles = Table(BalanceTables.CrewRolesFile),
                Strata = Table(BalanceTables.StrataFile),
                Ladder = Table(BalanceTables.LadderFile),
                Repression = Table(BalanceTables.RepressionFile),
            }, report);

            report.ThrowIfInvalid();
            return tables;
        }

        private static GameSession Session() =>
            GameSession.Start(
                Balance(),
                new Clock(10f, new[] { 1, 2, 4 }),
                File.ReadAllText(BalancePath("pipeline.csv")),
                PortScenario.Default());

        private static string AllText(GameSession session) =>
            string.Join(" | ", session.Feed.Entries.Select(e => e.Text));

        // ------------------------------------------------------------- what it says

        [Test]
        public void A_quiet_day_still_reports_something()
        {
            GameSession session = Session();

            session.Step();

            Assert.That(session.Feed.Count, Is.GreaterThan(0), "a day always has a story");
        }

        [Test]
        public void Lines_carry_the_day_they_happened_on()
        {
            GameSession session = Session();

            session.Step();
            session.Step();

            Assert.That(session.Feed.Entries.Select(e => e.Day).Distinct().Count(),
                Is.GreaterThan(1));
        }

        [Test]
        public void The_routine_is_quieter_than_the_alarming()
        {
            // Every day pays wages and allocates labour. If all of it arrived at the same
            // weight, the day a riot started would look like every other day.
            GameSession session = Session();
            for (int i = 0; i < 5; i++) session.Step();

            int detail = session.Feed.Entries.Count(e => e.Importance == FeedImportance.Detail);

            Assert.That(detail, Is.GreaterThan(0), "the ordinary is still recorded");
            Assert.That(session.Feed.Recent(50, FeedImportance.Alarming), Is.Empty,
                "a working port raises no alarms");
        }

        [Test]
        public void Trouble_is_reported_and_named()
        {
            GameSession session = Session();

            session.Submit(new Shock(ShockKind.Theft, 100000f));
            for (int i = 0; i < 6; i++) session.Step();

            var alarming = session.Feed.Recent(50, FeedImportance.Alarming);

            Assert.That(alarming, Is.Not.Empty);
            Assert.That(AllText(session), Does.Contain("unpaid"));
        }

        [Test]
        public void Every_line_names_a_number()
        {
            // "Wages went unpaid" is a mood. "7 went unpaid, owed 12" is something a player can
            // act on, and §3.2 asks for a game that expects thought rather than reflexes.
            GameSession session = Session();
            session.Submit(new Shock(ShockKind.Theft, 100000f));
            for (int i = 0; i < 4; i++) session.Step();

            var lines = session.Feed.Entries
                .Where(e => e.Importance == FeedImportance.Alarming)
                .ToArray();

            Assert.That(lines, Is.Not.Empty);
            foreach (FeedEntry line in lines)
                Assert.That(line.Text.Any(char.IsDigit), Is.True, "no number in: " + line.Text);
        }

        // ------------------------------------------------------------- provenance

        [Test]
        public void A_command_appears_before_what_it_caused()
        {
            // A feed that listed the consequence above the decision would read backwards.
            GameSession session = Session();
            EntityId building = session.World.Store<BuildingState>().Ids[0];

            session.Submit(new MothballBuilding(building, mothballed: true));
            session.Step();

            int order = session.Feed.Entries.ToList().FindIndex(e => e.Text.StartsWith("you ordered"));
            int shut = session.Feed.Entries.ToList().FindIndex(e => e.Text.Contains("was shut"));

            Assert.That(order, Is.GreaterThanOrEqualTo(0), AllText(session));
            Assert.That(shut, Is.GreaterThan(order), AllText(session));
        }

        [Test]
        public void An_event_can_be_traced_to_the_command_that_caused_it()
        {
            // The payoff §6.2 was built for: "a building was shut" and "you ordered: shut a
            // building" are linked, not merely adjacent.
            GameSession session = Session();
            EntityId building = session.World.Store<BuildingState>().Ids[0];

            session.Submit(new MothballBuilding(building, mothballed: true));
            session.Step();

            FeedEntry shut = session.Feed.Entries.First(e => e.Text.Contains("was shut"));

            Assert.That(shut.HasCause, Is.True, "the day itself did not shut it");
            Assert.That(session.Feed.TryFindCause(in shut, out FeedEntry cause), Is.True);
            Assert.That(cause.Text, Does.StartWith("you ordered"));
        }

        [Test]
        public void A_command_lists_what_it_went_on_to_cause()
        {
            GameSession session = Session();
            EntityId building = session.World.Store<BuildingState>().Ids[0];

            session.Submit(new MothballBuilding(building, mothballed: true));
            session.Step();

            FeedEntry order = session.Feed.Entries.First(e => e.Text.StartsWith("you ordered"));

            Assert.That(session.Feed.Consequences(in order).Select(e => e.Text),
                Has.Some.Contains("was shut"));
        }

        [Test]
        public void The_day_boundary_is_a_cause_rather_than_a_missing_one()
        {
            // §6.2: Root is an answer. The day arriving is a real reason for people to eat.
            GameSession session = Session();
            session.Step();

            Assert.That(session.Feed.Entries.Any(e => !e.HasCause), Is.True);
        }

        [Test]
        public void A_refused_order_is_shown_rather_than_swallowed()
        {
            // A button that appears to do nothing is the worst outcome available: the player
            // cannot tell a refused order from a broken one, and stops trusting the controls.
            GameSession session = Session();

            session.Submit(new SuppressRiot(Harshness.Firm));   // nothing to put down
            session.Step();

            Assert.That(AllText(session), Does.Contain("refused"));
            Assert.That(AllText(session), Does.Contain("not yet"));
        }

        // ---------------------------------------------------------------- bounds

        [Test]
        public void The_feed_is_bounded()
        {
            // A feed that keeps everything becomes a log nobody reads. The command log is the
            // record, and it is already complete and replayable.
            var feed = new EventFeed(capacity: 8);
            GameSession session = Session();

            for (int i = 0; i < 40; i++) session.Step();

            Assert.That(session.Feed.Count, Is.LessThanOrEqualTo(EventFeed.DefaultCapacity));
            Assert.That(feed.Capacity, Is.EqualTo(8));
        }

        [Test]
        public void Dropping_old_lines_keeps_the_causes_of_the_ones_left()
        {
            // The lookup is rebuilt when the front is trimmed. If it were not, a surviving line
            // would point at an index belonging to something else entirely.
            var feed = new EventFeed(capacity: 4);
            GameSession session = Session();

            for (int i = 0; i < 20; i++) session.Step();

            foreach (FeedEntry entry in session.Feed.Entries)
            {
                if (!session.Feed.TryFindCause(in entry, out FeedEntry cause)) continue;

                Assert.That(cause.Id.Value, Is.EqualTo(entry.Cause.Value));
            }
        }

        [Test]
        public void Recent_returns_oldest_first_so_it_reads_downwards()
        {
            GameSession session = Session();
            for (int i = 0; i < 5; i++) session.Step();

            var recent = session.Feed.Recent(5);

            Assert.That(recent.Count, Is.EqualTo(5));
            for (int i = 1; i < recent.Count; i++)
                Assert.That(recent[i].Id.Value, Is.GreaterThan(recent[i - 1].Id.Value));
        }

        [Test]
        public void The_feed_is_not_part_of_the_simulation()
        {
            // It reads the event stream and never writes the world, so a port played with the
            // feed open must reach the same state as one played without it (§2, §7.1).
            GameSession watched = Session();
            GameSession ignored = Session();

            for (int i = 0; i < 10; i++)
            {
                watched.Step();
                watched.Feed.Recent(20);
                ignored.Step();
            }

            var a = new Engine.State.HashStateWriter();
            var b = new Engine.State.HashStateWriter();
            watched.World.WriteTo(a);
            ignored.World.WriteTo(b);

            Assert.That(a.Digest, Is.EqualTo(b.Digest));
        }
    }
}
