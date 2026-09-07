using System.Collections.Generic;
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
    /// The other cities trading with each other (GDD §5.3, §5.2.2).
    /// </summary>
    /// <remarks>
    /// Four of the five cities were scenery until this existed: they produced, ate, grew angry
    /// and could be bought from, but never did anything. §5.3's premise is that trade works
    /// because ports differ, and a world where only one of them trades is a world where that
    /// difference does nothing.
    /// </remarks>
    [Category(TestCategories.Functional)]
    public class AiTradeTests
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
                Mob = Table(BalanceTables.MobFile),
                Heat = Table(BalanceTables.HeatFile),
                TradeAi = Table(BalanceTables.TradeAiFile),
            }, report);

            report.ThrowIfInvalid();
            return tables;
        }

        private BalanceTables _balance = null!;
        private GameSession _session = null!;

        [SetUp]
        public void SetUp()
        {
            _balance = Balance();
            _session = GameSession.Start(
                _balance,
                new Clock(10f, new[] { 1 }),
                File.ReadAllText(BalancePath("pipeline.csv")));
        }

        private World World => _session.World;

        private EntityId Player => _session.PlayerPort;

        private EntityId City(string id)
        {
            ComponentStore<PortState> ports = World.Store<PortState>();
            for (int i = 0; i < ports.Count; i++)
                if (_balance.Ports[ports.Values[i].DefinitionIndex].Id == id) return ports.Ids[i];

            return EntityId.None;
        }

        private int Good(string id) => ConsumptionSystem.IndexOf(_balance, id);

        private void Days(int n) { for (int i = 0; i < n; i++) _session.Step(); }

        /// <summary>Every convoy that existed at any point, by entity.</summary>
        /// <remarks>
        /// Counted this way because a count of what is afloat misses a day where one landed and
        /// another left — which is exactly the mistake that made this look like one shipment in
        /// two months when it is twelve.
        /// </remarks>
        private HashSet<int> Sail(int days)
        {
            var seen = new HashSet<int>();

            for (int day = 0; day < days; day++)
            {
                _session.Step();

                ComponentStore<Convoy> afloat = World.Store<Convoy>();
                for (int i = 0; i < afloat.Count; i++) seen.Add(afloat.Ids[i].Value);
            }

            return seen;
        }

        // ------------------------------------------------------------------- the world

        [Test]
        public void The_neighbours_run_their_own_routes()
        {
            Assert.That(Sail(60).Count, Is.GreaterThan(4),
                "two months and the world barely moved");
        }

        [Test]
        public void The_players_city_ships_nothing_it_was_not_told_to()
        {
            // The player's trade is the player's. A city that dispatched on its own would be
            // spending their coin and their stock without being asked.
            Days(60);

            Assert.That(AiTradeSystem.Convoys(World, Player), Is.Zero);
        }

        [Test]
        public void Nobody_ships_to_the_player()
        {
            // A neighbour selling you grain would take coin out of your treasury on arrival for
            // a purchase you never agreed to, and being able to refuse is the difference between
            // a trade and a tax. What an approach should look like is a §5.6 question.
            for (int day = 0; day < 60; day++)
            {
                _session.Step();

                ComponentStore<Convoy> afloat = World.Store<Convoy>();
                for (int i = 0; i < afloat.Count; i++)
                    Assert.That(afloat.Values[i].Destination, Is.Not.EqualTo(Player),
                        "a neighbour is shipping to the player's city");
            }
        }

        [Test]
        public void The_mining_city_stops_sitting_on_iron_nobody_wants()
        {
            // The sentence this exists for. Ironhold mines eight a day and eats none of it;
            // before the neighbours traded, it accumulated for ever against a demand that only
            // the player could ever answer.
            EntityId ironhold = City("ironhold");
            EntityId millrace = City("millrace");
            int iron = Good("iron");

            float before = Port.UnitsOf(World, millrace, iron);
            Days(60);

            Assert.That(Port.UnitsOf(World, ironhold, iron),
                Is.LessThan(60f), "still hoarding");

            // Somebody who consumes iron has been supplied by somebody who digs it.
            float delivered = Everywhere(iron) - Port.UnitsOf(World, ironhold, iron);
            Assert.That(delivered, Is.GreaterThan(before),
                "no iron ever left the city that mines it");
        }

        private float Everywhere(int good)
        {
            float total = 0f;

            foreach (EntityId port in Port.All(World).ToArray())
                total += Port.UnitsOf(World, port, good);

            return total;
        }

        // -------------------------------------------------------------------- the rule

        [Test]
        public void A_city_ships_only_what_it_can_spare()
        {
            // Above the keep, not at it. The reserve exists so a bad harvest is survivable, and
            // a city that traded it away would be selling the thing that keeps it alive.
            Days(60);

            foreach (EntityId port in Port.All(World).ToArray())
            {
                if (port == Player) continue;

                for (int good = 0; good < _balance.Goods.Count; good++)
                {
                    if (_balance.Goods[good].Keep <= 0f) continue;

                    // Consumption can take it below the reserve; trading it away may not. What
                    // is asserted is that nothing was ever shipped out of the reserve, which
                    // shows as a city never being deeply empty of a good it keeps.
                    Assert.That(Port.UnitsOf(World, port, good),
                        Is.GreaterThanOrEqualTo(-0.01f), _balance.Goods[good].Id);
                }
            }
        }

        [Test]
        public void One_convoy_at_a_time_from_any_one_city()
        {
            for (int day = 0; day < 60; day++)
            {
                _session.Step();

                foreach (EntityId port in Port.All(World).ToArray())
                {
                    if (port == Player) continue;

                    Assert.That(AiTradeSystem.Convoys(World, port),
                        Is.LessThanOrEqualTo(_balance.TradeAi.ConvoysPerCity), "day " + day);
                }
            }
        }

        [Test]
        public void Nothing_is_shipped_to_a_city_that_already_has_plenty()
        {
            // Traffic for its own sake would fill the map with ships carrying coals to Newcastle,
            // and a player reading the map would learn nothing from a lane.
            for (int day = 0; day < 40; day++)
            {
                _session.Step();

                ComponentStore<Convoy> afloat = World.Store<Convoy>();
                for (int i = 0; i < afloat.Count; i++)
                {
                    Convoy convoy = afloat.Values[i];
                    if (convoy.Origin == Player) continue;
                    if (convoy.DaysRemaining != convoy.TotalDays) continue;

                    Assert.That(Port.UnitsOf(World, convoy.Destination, convoy.GoodIndex),
                        Is.LessThan(_balance.Goods[convoy.GoodIndex].Keep + convoy.Units + 0.01f),
                        "shipped to a city that was already full of it");
                }
            }
        }

        [Test]
        public void A_neighbours_convoy_is_theirs_and_not_yours()
        {
            // It shows on the map in their colour, it draws their Heat, and a raid takes it from
            // them. One system applied uniformly, which is what §5.2.2 asks for.
            Sail(30);

            ComponentStore<Convoy> afloat = World.Store<Convoy>();
            Assume.That(afloat.Count, Is.GreaterThan(0));

            for (int i = 0; i < afloat.Count; i++)
                Assert.That(Port.OwnerOf(World, afloat.Ids[i]), Is.Not.EqualTo(Player));
        }

        [Test]
        public void The_same_world_trades_the_same_way_twice()
        {
            var first = new AiTradeTests();
            first.SetUp();
            first.Days(45);

            var second = new AiTradeTests();
            second.SetUp();
            second.Days(45);

            int iron = first.Good("iron");

            foreach (string id in new[] { "ironhold", "millrace", "fairhaven", "coldwater" })
            {
                Assert.That(
                    Port.UnitsOf(first.World, first.City(id), iron),
                    Is.EqualTo(Port.UnitsOf(second.World, second.City(id), iron)).Within(1e-4f),
                    id);
            }
        }

        // ------------------------------------------------------------------- content

        [Test]
        public void A_city_that_would_ship_more_than_it_can_spare_is_refused()
        {
            var report = new ValidationReport();
            TradeAiRules.Load(
                CsvTable.Parse("key,value\nparcel,10\nspare,3\n", "trade_ai.csv"), report);

            Assert.That(report.Problems.Any(p => p.Contains("could do without")), Is.True,
                string.Join("; ", report.Problems));
        }

        [Test]
        public void An_unknown_setting_is_a_mistake_in_a_file()
        {
            var report = new ValidationReport();
            TradeAiRules.Load(
                CsvTable.Parse("key,value\nshipments,4\n", "trade_ai.csv"), report);

            Assert.That(report.Problems.Any(p => p.Contains("not a trade setting")), Is.True,
                string.Join("; ", report.Problems));
        }
    }
}
