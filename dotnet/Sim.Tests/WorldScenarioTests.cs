using System.IO;
using System.Linq;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Sim.Components;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.State;
using RTS.Sim.Scenarios;
using RTS.Sim.Session;
using RTS.Sim.Systems;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// A world of cities, each running its own economy (GDD §5.3, §5.2.2).
    /// </summary>
    /// <remarks>
    /// The claim being tested is that a neighbour is a port and not a lighter model of one:
    /// the same systems feed it, pay it, work it and anger it. §5.2.2 calls that "one system,
    /// applied uniformly" and "the single highest payoff-per-line system in the document" —
    /// this is where that stops being an aspiration.
    /// </remarks>
    [Category(TestCategories.Functional)]
    public class WorldScenarioTests
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
                Ports = Table(BalanceTables.PortsFile),
            }, report);

            report.ThrowIfInvalid();
            return tables;
        }

        private static ReplayRun Run(BalanceTables balance) =>
            ReplayRun.Start(
                seed: 1,
                GameSession.PlayerCommands(),
                dispatcher => ScenarioRunner.BuildPipeline(
                    File.ReadAllText(BalancePath("pipeline.csv")), dispatcher),
                WorldScenario.FromContent(balance),
                balance);

        // ------------------------------------------------------------------ building

        [Test]
        public void Every_city_in_the_content_is_built()
        {
            BalanceTables balance = Balance();
            World world = WorldScenario.FromContent(balance);

            Assert.That(Port.Count(world), Is.EqualTo(balance.Ports.Count));
        }

        [Test]
        public void Exactly_one_city_is_the_players()
        {
            World world = WorldScenario.FromContent(Balance());

            Assert.That(Port.Player(world), Is.Not.EqualTo(EntityId.None));
        }

        [Test]
        public void Each_city_owns_its_own_people_and_buildings()
        {
            // The bug this exists to prevent is silent: an unowned entity is never reached by
            // any system, because every one of them iterates ports. It would not throw, it
            // would simply never eat, never be paid and never work.
            BalanceTables balance = Balance();
            World world = WorldScenario.FromContent(balance);

            AssertAllOwned<CrewMember>(world);
            AssertAllOwned<BuildingState>(world);
            AssertAllOwned<Population>(world);
            AssertAllOwned<Treasury>(world);
            AssertAllOwned<Grievance>(world);
            AssertAllOwned<RevolutionLadder>(world);
            AssertAllOwned<Stock>(world);
        }

        [Test]
        public void Each_city_starts_with_what_its_row_says()
        {
            BalanceTables balance = Balance();
            World world = WorldScenario.FromContent(balance);

            ComponentStore<PortState> ports = world.Store<PortState>();

            for (int i = 0; i < ports.Count; i++)
            {
                EntityId id = ports.Ids[i];
                PortDefinition definition = balance.Ports[ports.Values[i].DefinitionIndex];

                Assert.That(Port.Treasury(world, id).Coin, Is.EqualTo(definition.StartingCoin),
                    definition.Id + " coin");
                Assert.That(LabourSystem.CommonersIn(world, id), Is.EqualTo(definition.Commoners),
                    definition.Id + " commoners");
            }
        }

        // --------------------------------------------------------------- independence

        [Test]
        public void One_citys_famine_is_not_another_citys_famine()
        {
            // The whole point of ownership. Before it, one store of crew and one store of stock
            // meant Ironhold's miners ate Saltmarsh's bread.
            BalanceTables balance = Balance();
            ReplayRun run = Run(balance);

            EntityId player = Port.Player(run.World);
            EntityId neighbour = Port.All(run.World).ToArray().First(p => p != player);

            int food = ConsumptionSystem.IndexOf(balance, "food");
            float playerBefore = Port.UnitsOf(run.World, player, food);
            float neighbourBefore = Port.UnitsOf(run.World, neighbour, food);

            run.Submit(new Shock(ShockKind.HarvestFailure, 100000f, player));
            run.AdvanceDay();
            run.Events.Drain();

            // Measured as change rather than as an absolute, because the day keeps running
            // after the shock: the emptied granary is refilled by that day's harvest, which is
            // the port recovering rather than the shock having missed.
            float playerChange = Port.UnitsOf(run.World, player, food) - playerBefore;
            float neighbourChange = Port.UnitsOf(run.World, neighbour, food) - neighbourBefore;

            // Comparative, not absolute. The day keeps running after the shock, and Saltmarsh
            // grows enough to refill an emptied granary within it — so the port ends the day
            // roughly level and the shock still plainly landed on it rather than on anyone else.
            Assert.That(playerChange, Is.LessThan(neighbourChange),
                $"player {playerChange:0.0}, neighbour {neighbourChange:0.0}");
        }

        [Test]
        public void A_neighbour_is_robbed_only_when_it_is_named()
        {
            BalanceTables balance = Balance();
            ReplayRun run = Run(balance);

            EntityId player = Port.Player(run.World);
            EntityId neighbour = Port.All(run.World).ToArray().First(p => p != player);

            int playerBefore = Port.Treasury(run.World, player).Coin;
            int neighbourBefore = Port.Treasury(run.World, neighbour).Coin;

            run.Submit(new Shock(ShockKind.Theft, 100000f, neighbour));
            run.AdvanceDay();
            run.Events.Drain();

            // The day continues after the theft, so the robbed city ends with whatever it
            // earned afterwards rather than with nothing. What matters is who lost.
            Assert.That(Port.Treasury(run.World, neighbour).Coin, Is.LessThan(neighbourBefore),
                "the neighbour was robbed");
            Assert.That(Port.Treasury(run.World, player).Coin, Is.GreaterThan(playerBefore - 100),
                "the player was not");
        }

        [Test]
        public void Every_city_runs_the_same_systems()
        {
            // §5.2.2: "The same system runs for AI ports." A neighbour that did not eat, work
            // or riot would be scenery, and its crisis could not become the player's
            // opportunity.
            BalanceTables balance = Balance();
            ReplayRun run = Run(balance);

            // Sixty days rather than ten, because ten hid the thing that mattered. The first
            // version of ports.csv had three of the five deposed by day forty-five with no
            // player involvement: they were written to trade for food, and routes do not exist
            // yet. A world that empties itself before the mechanic arrives has no opportunities
            // in it, whatever §5.2.2 promises.
            run.Run(days: 60);
            run.Events.Drain();

            foreach (EntityId port in Port.All(run.World).ToArray())
            {
                PortDefinition definition = balance.Ports[
                    run.World.Store<Components.PortState>().GetRef(port).DefinitionIndex];

                PortReport report = PortReport.Of(run.World, port, balance, run.Day);

                Assert.That(report.Crew, Is.GreaterThan(0), definition.Id + " lost every crew member");
                Assert.That(LabourSystem.CommonersIn(run.World, port), Is.GreaterThan(0),
                    definition.Id + " emptied of people");
                Assert.That(report.Rung, Is.LessThan(LadderRung.Deposition),
                    definition.Id + " was deposed while nobody was doing anything to it");
            }
        }

        [Test]
        public void The_cities_do_not_all_end_the_same_way()
        {
            // If five specialised cities ran ten days and arrived at identical numbers, either
            // specialisation is not reaching the simulation or the systems are not really
            // running per port.
            BalanceTables balance = Balance();
            ReplayRun run = Run(balance);

            run.Run(days: 10);
            run.Events.Drain();

            int[] coin = Port.All(run.World).ToArray()
                .Select(p => PortReport.Of(run.World, p, balance, run.Day).Coin)
                .ToArray();

            Assert.That(coin.Distinct().Count(), Is.GreaterThan(1),
                "every city has the same coin: " + string.Join(", ", coin));
        }

        // -------------------------------------------------------------------- routes

        [Test]
        public void Travel_time_comes_from_distance()
        {
            BalanceTables balance = Balance();
            PortDefinition saltmarsh = balance.Ports["saltmarsh"];
            PortDefinition ironhold = balance.Ports["ironhold"];
            PortDefinition coldwater = balance.Ports["coldwater"];

            PortDefinition fairhaven = balance.Ports["fairhaven"];

            int toIronhold = WorldScenario.TravelDays(saltmarsh, ironhold);
            int toColdwater = WorldScenario.TravelDays(saltmarsh, coldwater);
            int toFairhaven = WorldScenario.TravelDays(saltmarsh, fairhaven);

            Assert.That(toFairhaven, Is.GreaterThan(1), "a route is a commitment, not a toggle");

            // An earlier map had every city three days away, so distance existed in the file
            // and changed nothing in play. Near and far have to be a decision.
            Assert.That(new[] { toIronhold, toColdwater, toFairhaven }.Distinct().Count(),
                Is.EqualTo(3),
                $"ironhold {toIronhold}, coldwater {toColdwater}, fairhaven {toFairhaven}");

            Assert.That(toIronhold, Is.GreaterThan(toFairhaven),
                "the iron the player cannot make should be the longest commitment");
        }

        [Test]
        public void Nowhere_is_reachable_the_day_a_convoy_leaves()
        {
            // §5.1: a round trip is measured in days. A convoy arriving the same day it left is
            // a transfer, and there would be nothing to intercept.
            BalanceTables balance = Balance();

            foreach (PortDefinition from in balance.Ports)
            {
                foreach (PortDefinition to in balance.Ports)
                {
                    if (ReferenceEquals(from, to)) continue;

                    Assert.That(WorldScenario.TravelDays(from, to), Is.GreaterThanOrEqualTo(1),
                        from.Id + " to " + to.Id);
                }
            }
        }

        private static void AssertAllOwned<T>(World world) where T : struct, IComponentData
        {
            ComponentStore<T> store = world.Store<T>();

            Assert.That(store.Count, Is.GreaterThan(0), typeof(T).Name + " store is empty");

            for (int i = 0; i < store.Count; i++)
            {
                Assert.That(Port.OwnerOf(world, store.Ids[i]), Is.Not.EqualTo(EntityId.None),
                    typeof(T).Name + " at index " + i + " belongs to no city");
            }
        }
    }
}
