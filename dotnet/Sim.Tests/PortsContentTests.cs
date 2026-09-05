using System.IO;
using System.Linq;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// The cities (GDD §5.3): where they are, what they are good at, and what that costs them.
    /// </summary>
    /// <remarks>
    /// "Trade only works because ports differ." These tests are that sentence made checkable —
    /// a world of identical cities has no differential, and therefore no economic game.
    /// </remarks>
    [Category(TestCategories.Unit)]
    public class PortsContentTests
    {
        private static string BalancePath(string file) =>
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Balance", file);

        private static CsvTable Table(string file) =>
            CsvTable.Parse(File.ReadAllText(BalancePath(file)), file);

        private static BalanceTables Shipped(out ValidationReport report)
        {
            report = new ValidationReport();
            return BalanceTables.Load(new BalanceSources
            {
                Goods = Table(BalanceTables.GoodsFile),
                Buildings = Table(BalanceTables.BuildingsFile),
                CrewRoles = Table(BalanceTables.CrewRolesFile),
                Strata = Table(BalanceTables.StrataFile),
                Ladder = Table(BalanceTables.LadderFile),
                Repression = Table(BalanceTables.RepressionFile),
                Ports = Table(BalanceTables.PortsFile),
            }, report);
        }

        private static BalanceTables From(string portsCsv, out ValidationReport report)
        {
            report = new ValidationReport();
            return BalanceTables.Load(new BalanceSources
            {
                Goods = Table(BalanceTables.GoodsFile),
                Buildings = Table(BalanceTables.BuildingsFile),
                CrewRoles = Table(BalanceTables.CrewRolesFile),
                Ports = CsvTable.Parse(portsCsv, BalanceTables.PortsFile),
            }, report);
        }

        // ------------------------------------------------------------- the shipped five

        [Test]
        public void The_shipped_cities_load()
        {
            BalanceTables balance = Shipped(out ValidationReport report);

            report.ThrowIfInvalid();
            Assert.That(balance.Ports.Count, Is.EqualTo(5));
        }

        [Test]
        public void Exactly_one_city_is_the_players()
        {
            BalanceTables balance = Shipped(out _);

            Assert.That(balance.Ports.Count(p => p.IsPlayer), Is.EqualTo(1));
        }

        [Test]
        public void Every_city_leans_towards_something()
        {
            // Variety is the wrong measure of specialisation, and this test started out getting
            // that wrong. Coldwater has one of each building and is the control case — it can
            // make a little of everything and enough of nothing, which is a legitimate city
            // rather than a mistake. What matters is that each city is *better* at something
            // than at the rest, because that gap is the differential §5.3 calls the game.
            BalanceTables balance = Shipped(out _);

            foreach (PortDefinition port in balance.Ports)
            {
                var output = new System.Collections.Generic.Dictionary<string, float>();

                foreach (Building building in port.Buildings.Select(id => balance.Buildings[id]))
                {
                    if (!building.IsProducer) continue;

                    output.TryGetValue(building.Produces, out float sofar);
                    output[building.Produces] = sofar + building.OutputPerDay;
                }

                float total = output.Values.Sum();
                float best = output.Values.Max();

                Assert.That(best / total, Is.GreaterThan(0.4f),
                    port.Id + " makes a little of everything and is good at nothing");
            }
        }

        [Test]
        public void Every_city_feeds_itself()
        {
            // This test used to assert the opposite, and the opposite was a bug. Cities were
            // written short of food on the theory that they would trade for it, and three of
            // the five were deposed by day forty-five with no player involvement — routes do
            // not exist yet, and a world that empties itself before the mechanic arrives has no
            // opportunities in it.
            //
            // Survive alone badly, prosper only by trading. Feeding yourself is the "survive"
            // half; the "prosper" half is the test below.
            BalanceTables balance = Shipped(out _);
            StratumRules commoners = balance.Strata.First(s => s.Stratum == Stratum.Commoners);

            foreach (PortDefinition port in balance.Ports)
            {
                float grown = port.Buildings
                    .Select(id => balance.Buildings[id])
                    .Where(b => b.IsProducer && b.Produces == "food")
                    .Sum(b => b.OutputPerDay);

                float eaten = port.Commoners * commoners.FoodPerDay +
                              port.Crew.Sum(c => balance.CrewRoles[c.Key].FoodPerDay * c.Value);

                Assert.That(grown, Is.GreaterThanOrEqualTo(eaten),
                    $"{port.Id} grows {grown:0.0} and eats {eaten:0.0}");
            }
        }

        [Test]
        public void No_city_produces_every_good()
        {
            // The "cannot produce all by yourself" half, stated as capability rather than
            // capacity. Saltmarsh makes no iron at all and Ironhold makes the only iron there
            // is — so once buildings consume goods as well as make them, neither can finish
            // alone. That is the differential §5.3 calls the economic game.
            BalanceTables balance = Shipped(out _);

            string[] everything = balance.Buildings
                .Where(b => b.IsProducer)
                .Select(b => b.Produces)
                .Distinct()
                .ToArray();

            foreach (PortDefinition port in balance.Ports)
            {
                string[] mine = Produces(balance, port);

                Assert.That(mine.Length, Is.LessThan(everything.Length),
                    port.Id + " makes everything and would never need a route");
            }
        }

        [Test]
        public void Every_city_can_produce_something()
        {
            // The other half. A city that produced nothing at all would not be a trading
            // partner, it would be a mouth — and a route to it a charity rather than a
            // differential.
            BalanceTables balance = Shipped(out _);

            foreach (PortDefinition port in balance.Ports)
            {
                Assert.That(port.Buildings.Any(id => balance.Buildings[id].IsProducer), Is.True,
                    port.Id + " produces nothing");
            }
        }

        [Test]
        public void The_cities_do_not_all_make_the_same_thing()
        {
            // Five cities all farming would satisfy every test above and still have no trade
            // in it. What matters is that the set of things each is good at differs.
            BalanceTables balance = Shipped(out _);

            var profiles = balance.Ports
                .Select(p => string.Join(",", p.Buildings
                    .Select(id => balance.Buildings[id])
                    .Where(b => b.IsProducer)
                    .Select(b => b.Produces)
                    .OrderBy(g => g)
                    .Distinct()))
                .ToArray();

            Assert.That(profiles.Distinct().Count(), Is.GreaterThan(1),
                "every city is good at the same things: " + string.Join(" | ", profiles));
        }

        [Test]
        public void Somebody_lacks_what_somebody_else_has_to_spare()
        {
            // Concretely: at least one pair of cities where one produces a good the other does
            // not produce at all. That pair is a route worth running.
            BalanceTables balance = Shipped(out _);

            bool found = false;

            foreach (PortDefinition seller in balance.Ports)
            {
                foreach (PortDefinition buyer in balance.Ports)
                {
                    if (ReferenceEquals(seller, buyer)) continue;

                    string[] sells = Produces(balance, seller);
                    string[] buys = Produces(balance, buyer);

                    if (sells.Any(g => !buys.Contains(g))) found = true;
                }
            }

            Assert.That(found, Is.True, "no city has anything another one lacks");
        }

        [Test]
        public void The_cities_are_far_enough_apart_to_make_a_route_a_commitment()
        {
            // §5.1 wants a round trip measured in days rather than a toggle. Distance is what
            // that will be computed from, so a world of neighbours a stone's throw apart would
            // quietly make routes free.
            BalanceTables balance = Shipped(out _);
            PortDefinition player = balance.Ports.First(p => p.IsPlayer);

            foreach (PortDefinition other in balance.Ports.Where(p => !p.IsPlayer))
                Assert.That(player.DistanceTo(other), Is.GreaterThan(5f), other.Id + " is next door");
        }

        // ----------------------------------------------------------------- validation

        [Test]
        public void A_world_with_no_player_is_rejected()
        {
            From(PortsLoader.Header +
                 "a,A,0,0,false,100,5,laborer:1,farm,food:5\n", out ValidationReport report);

            Assert.That(report.Problems.Any(p => p.Contains("Somebody has to be")), Is.True,
                string.Join("; ", report.Problems));
        }

        [Test]
        public void Two_players_are_rejected()
        {
            From(PortsLoader.Header +
                 "a,A,0,0,true,100,5,laborer:1,farm,food:5\n" +
                 "b,B,9,0,true,100,5,laborer:1,farm,food:5\n", out ValidationReport report);

            Assert.That(report.Problems.Any(p => p.Contains("Exactly one may be")), Is.True,
                string.Join("; ", report.Problems));
        }

        [Test]
        public void Two_cities_in_the_same_place_are_rejected()
        {
            From(PortsLoader.Header +
                 "a,A,3,4,true,100,5,laborer:1,farm,food:5\n" +
                 "b,B,3,4,false,100,5,laborer:1,farm,food:5\n", out ValidationReport report);

            Assert.That(report.Problems.Any(p => p.Contains("sits on top of")), Is.True,
                string.Join("; ", report.Problems));
        }

        [Test]
        public void A_city_naming_a_building_that_does_not_exist_is_rejected()
        {
            // Reads perfectly well on its own; produces a city silently missing a building.
            From(PortsLoader.Header +
                 "a,A,0,0,true,100,5,laborer:1,brewery,food:5\n", out ValidationReport report);

            Assert.That(report.Problems.Any(p => p.Contains("'brewery'")), Is.True,
                string.Join("; ", report.Problems));
        }

        [Test]
        public void A_city_naming_a_role_that_does_not_exist_is_rejected()
        {
            From(PortsLoader.Header +
                 "a,A,0,0,true,100,5,alchemist:1,farm,food:5\n", out ValidationReport report);

            Assert.That(report.Problems.Any(p => p.Contains("'alchemist'")), Is.True,
                string.Join("; ", report.Problems));
        }

        [Test]
        public void A_city_with_no_commoners_is_rejected()
        {
            From(PortsLoader.Header +
                 "a,A,0,0,true,100,0,laborer:1,farm,food:5\n", out ValidationReport report);

            Assert.That(report.Problems.Any(p => p.Contains("no commoners")), Is.True,
                string.Join("; ", report.Problems));
        }

        [Test]
        public void A_malformed_list_entry_is_rejected()
        {
            From(PortsLoader.Header +
                 "a,A,0,0,true,100,5,laborer,farm,food:5\n", out ValidationReport report);

            Assert.That(report.Problems.Any(p => p.Contains("is not 'id:count'")), Is.True,
                string.Join("; ", report.Problems));
        }

        private static string[] Produces(BalanceTables balance, PortDefinition port) =>
            port.Buildings
                .Select(id => balance.Buildings[id])
                .Where(b => b.IsProducer)
                .Select(b => b.Produces)
                .Distinct()
                .ToArray();
    }
}
