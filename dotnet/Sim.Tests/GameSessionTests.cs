using System.IO;
using System.Linq;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Sim.Engine.Time;
using RTS.Sim.Session;
using RTS.Sim.Systems;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// The game as something a front end drives (GDD §3.2; ARCHITECTURE §2, §6.1).
    /// </summary>
    /// <remarks>
    /// Every test here runs with no engine anywhere near it, which is the point being made:
    /// advancing time, reading state and issuing commands are all game behaviour rather than
    /// Unity behaviour. If the editor had to be upgraded tomorrow, none of this would move.
    /// </remarks>
    [Category(TestCategories.Functional)]
    public class GameSessionTests
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

        private static string PipelineCsv() => File.ReadAllText(BalancePath("pipeline.csv"));

        private static GameSession Session(float secondsPerDay = 10f) =>
            GameSession.Start(
                Balance(),
                new Clock(secondsPerDay, new[] { 1, 2, 4 }),
                PipelineCsv(),
                PortScenario.Default());

        // ------------------------------------------------------------------ time

        [Test]
        public void Real_time_becomes_days()
        {
            GameSession session = Session();

            session.Advance(30f);

            Assert.That(session.Day, Is.EqualTo(4), "three days on top of the first");
        }

        [Test]
        public void A_paused_session_does_not_move()
        {
            GameSession session = Session();
            session.Clock.Pause();

            session.Advance(1000f);

            Assert.That(session.Day, Is.EqualTo(1));
        }

        [Test]
        public void Stepping_advances_a_day_whatever_the_clock_says()
        {
            // For a paused player who wants to see exactly one more day happen.
            GameSession session = Session();
            session.Clock.Pause();

            session.Step();

            Assert.That(session.Day, Is.EqualTo(2));
        }

        // ----------------------------------------------------------- determinism

        [Test]
        public void How_the_time_arrived_changes_nothing()
        {
            // The property the whole save format rests on. A frame rate is not deterministic and
            // a player's machine is not the developer's, so if any of that reached the world a
            // save would not replay. It reaches an integer instead.
            GameSession stutter = Session();
            GameSession smooth = Session();

            for (int i = 0; i < 600; i++) stutter.Advance(1f / 60f);
            smooth.Advance(10f);

            Assert.That(stutter.Day, Is.EqualTo(smooth.Day));
            Assert.That(Digest(stutter), Is.EqualTo(Digest(smooth)));
        }

        [Test]
        public void Playing_faster_reaches_the_same_world()
        {
            // Speed is a pacing control, not a difficulty one. Four days at ×4 must be the same
            // four days at ×1, or the button would quietly be changing the game.
            GameSession slow = Session();
            GameSession fast = Session();
            fast.Clock.Speed = 4;

            slow.Advance(40f);
            for (int i = 0; i < 10; i++) fast.Advance(1f);

            Assert.That(fast.Day, Is.EqualTo(slow.Day));
            Assert.That(Digest(fast), Is.EqualTo(Digest(slow)));
        }

        [Test]
        public void Pausing_to_think_costs_nothing()
        {
            // §3.2 makes pause the mechanism that separates decision complexity from reaction
            // speed. A player who pauses often must not end up with a different world from one
            // who never pauses.
            GameSession patient = Session();
            GameSession hurried = Session();

            for (int i = 0; i < 40; i++)
            {
                patient.Clock.Pause();
                patient.Advance(100f);
                patient.Clock.Resume();
                patient.Advance(1f);
            }

            hurried.Advance(40f);

            Assert.That(patient.Day, Is.EqualTo(hurried.Day));
            Assert.That(Digest(patient), Is.EqualTo(Digest(hurried)));
        }

        [Test]
        public void A_played_session_replays_from_its_command_log()
        {
            // The claim §6.1 makes about saves, tested against something that was actually
            // played rather than scripted: a seed plus a command log is the game.
            GameSession played = Session();

            played.Advance(20f);
            played.Submit(new MothballBuilding(played.World.Store<Components.BuildingState>().Ids[0], true));
            played.Advance(50f);
            played.Submit(new Shock(ShockKind.Theft, 40f));
            played.Advance(30f);

            GameSession replayed = Session();
            for (int day = 1; day < played.Day; day++) replayed.Step();

            Assert.That(replayed.Day, Is.EqualTo(played.Day));
            Assert.That(replayed.Run.CommandLog.Entries.Count, Is.Zero,
                "the replay issued no commands of its own");
        }

        // -------------------------------------------------------------- readouts

        [Test]
        public void The_readouts_say_what_phase_3_asks_for()
        {
            // BUILD_ORDER: reserves, upkeep, stocks, unrest by stratum.
            GameSession session = Session();
            string[] labels = session.Readouts().Select(r => r.Label).ToArray();

            Assert.That(labels, Does.Contain("Coin"));
            Assert.That(labels, Does.Contain("Upkeep"));
            Assert.That(labels, Does.Contain("food"));
            Assert.That(labels, Does.Contain("Unrest"));

            foreach (StratumRules stratum in session.Balance.Strata)
                Assert.That(labels, Does.Contain(stratum.Id));
        }

        [Test]
        public void The_town_and_its_unemployed_are_visible()
        {
            // Unemployment is a grievance the player can act on by building, so it has to be
            // something they can see.
            GameSession session = Session();
            session.Advance(10f);

            Readout town = session.Readouts().First(r => r.Label == "Town");
            Readout idle = session.Readouts().First(r => r.Label == "Unemployed");

            Assert.That(int.Parse(town.Value), Is.EqualTo(session.Commoners()));
            Assert.That(int.Parse(idle.Value), Is.GreaterThan(0),
                "the shipped port has more people than places to put them");
        }

        [Test]
        public void Upkeep_falls_when_a_building_is_shut()
        {
            GameSession session = Session();
            int before = session.UpkeepPerDay();

            session.Submit(new MothballBuilding(
                session.World.Store<Components.BuildingState>().Ids[0], true));
            session.Step();

            Assert.That(session.UpkeepPerDay(), Is.LessThan(before));
        }

        [Test]
        public void Arrears_are_only_shown_when_there_are_any()
        {
            // A row reading "Unpaid 0" every day teaches the player to stop reading rows.
            GameSession session = Session();

            Assert.That(session.Readouts().Any(r => r.Label == "Unpaid"), Is.False);
        }

        private static string Digest(GameSession session)
        {
            var writer = new Engine.State.HashStateWriter();
            session.World.WriteTo(writer);
            return writer.Digest;
        }
    }
}
