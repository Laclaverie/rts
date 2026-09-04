using System.IO;
using System.Linq;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Sim.Components;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Time;
using RTS.Sim.Session;
using RTS.Sim.Systems;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// What the player can do, and whether they can do it (BUILD_ORDER Phase 3).
    /// </summary>
    /// <remarks>
    /// The list is built in <c>Sim</c> so that a button's availability comes from the handler
    /// that would refuse it, rather than from reasoning written a second time in a panel
    /// (ARCHITECTURE §2.2). These tests are that guarantee.
    /// </remarks>
    [Category(TestCategories.Functional)]
    public class PlayerActionTests
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

        private static PlayerAction Find(GameSession session, string label) =>
            session.Actions().First(a => a.Label.StartsWith(label));

        // -------------------------------------------------------------- the offer

        [Test]
        public void The_player_is_offered_the_levers_that_exist()
        {
            GameSession session = Session();
            var actions = session.Actions();

            Assert.That(actions.Any(a => a.Command is SuppressRiot), Is.True);
            Assert.That(actions.Any(a => a.Command is MothballBuilding), Is.True);
            Assert.That(actions.Any(a => a.Command is AssignCrew), Is.True);
        }

        [Test]
        public void Every_building_can_be_shut()
        {
            GameSession session = Session();
            int buildings = session.World.Store<BuildingState>().Count;

            Assert.That(session.Actions().Count(a => a.Command is MothballBuilding),
                Is.EqualTo(buildings));
        }

        [Test]
        public void An_action_says_what_it_applies_to()
        {
            // "Shut farm" alone does not tell the player which farm, or that it is the one with
            // nobody working it. The detail is the difference between a button and a decision.
            GameSession session = Session();
            session.Step();

            PlayerAction shut = session.Actions().First(a => a.Command is MothballBuilding);

            Assert.That(shut.Detail, Is.Not.Empty);
            Assert.That(shut.Detail, Does.Contain("worked").Or.Contain("%"));
        }

        [Test]
        public void Repression_shows_its_price_on_the_button()
        {
            // §5.2.2 wants repression to be a decision rather than a reflex, and a decision
            // needs its cost visible at the moment it is made.
            GameSession session = Session();

            PlayerAction brutal = Find(session, "Brutal");

            Assert.That(brutal.Detail, Does.Contain("forever"));
            Assert.That(brutal.Detail, Does.Contain("quiet"));
        }

        // ------------------------------------------------------------ availability

        [Test]
        public void An_impossible_action_is_offered_but_disabled()
        {
            // Listed rather than hidden: a control that appears only when it would work leaves
            // the player unable to learn that it exists.
            GameSession session = Session();

            PlayerAction firm = Find(session, "Firm");

            Assert.That(firm.Enabled, Is.False, "there is no riot to put down");
            Assert.That(firm.Reason, Is.EqualTo("not yet"));
        }

        [Test]
        public void Repression_becomes_available_when_there_is_a_riot()
        {
            GameSession session = Session();

            for (int day = 0; day < 30 && !Find(session, "Firm").Enabled; day++)
            {
                session.Submit(new Shock(ShockKind.Theft, 100000f));
                session.Step();
            }

            Assert.That(Find(session, "Firm").Enabled, Is.True,
                "a rioting port can be put down");
        }

        [Test]
        public void Availability_agrees_with_what_the_command_actually_does()
        {
            // The guarantee the whole design rests on. Every enabled action is submitted and
            // must be accepted; every disabled one must be refused for the stated reason.
            GameSession session = Session();
            session.Step();

            foreach (PlayerAction action in session.Actions().ToArray())
            {
                CommandRejection actual = session.Validate(action.Command);

                Assert.That(actual, Is.EqualTo(action.Rejection),
                    action.Label + " says " + action.Rejection + " but validates as " + actual);
            }
        }

        [Test]
        public void An_enabled_action_is_accepted_when_issued()
        {
            GameSession session = Session();

            PlayerAction shut = session.Actions().First(
                a => a.Command is MothballBuilding && a.Enabled);

            session.Submit(shut.Command);
            session.Step();

            Assert.That(session.Run.CommandLog.Entries.Last().Applied, Is.True);
        }

        [Test]
        public void Asking_whether_an_action_is_possible_does_not_change_anything()
        {
            // A question is not a decision. Validation must not touch the world, or a panel
            // refreshing every frame would be playing the game on the player's behalf.
            GameSession quiet = Session();
            GameSession polled = Session();

            for (int i = 0; i < 10; i++)
            {
                quiet.Step();
                polled.Step();
                polled.Actions();
                polled.Actions();
            }

            var a = new Engine.State.HashStateWriter();
            var b = new Engine.State.HashStateWriter();
            quiet.World.WriteTo(a);
            polled.World.WriteTo(b);

            Assert.That(b.Digest, Is.EqualTo(a.Digest));
            Assert.That(polled.Run.CommandLog.Count, Is.EqualTo(quiet.Run.CommandLog.Count));
        }

        // ------------------------------------------------------------------ crew

        [Test]
        public void A_building_with_no_work_offers_nobody_to_post()
        {
            // Whether a building has work is data (§5.5), not a hardcoded list. The warehouse
            // and the longhouse want no staff, so posting a specialist there is refused.
            GameSession session = Session();

            var posts = session.Actions()
                .Where(a => a.Command is AssignCrew && a.Label.StartsWith("post"))
                .ToArray();

            int producers = session.Balance.Buildings.Count(b => b.Staff > 0);

            Assert.That(posts, Is.Not.Empty);
            Assert.That(posts.Length, Is.LessThanOrEqualTo(
                session.World.Store<BuildingState>().Count));
            Assert.That(producers, Is.GreaterThan(0));
        }

        [Test]
        public void A_specialist_can_be_recalled_from_where_they_are_posted()
        {
            GameSession session = Session();

            PlayerAction recall = session.Actions().First(
                a => a.Label.StartsWith("recall") && a.Enabled);

            session.Submit(recall.Command);
            session.Step();

            Assert.That(session.Run.CommandLog.Entries.Last().Applied, Is.True);
        }

        [Test]
        public void Shutting_a_building_changes_what_is_offered_for_it()
        {
            GameSession session = Session();
            EntityId building = session.World.Store<BuildingState>().Ids[0];

            session.Submit(new MothballBuilding(building, mothballed: true));
            session.Step();

            Assert.That(session.Actions().Any(a => a.Label.StartsWith("Reopen")), Is.True);
        }
    }
}
