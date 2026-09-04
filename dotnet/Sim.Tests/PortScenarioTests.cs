using System.Linq;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.State;
using RTS.Sim.Systems;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// The starting port, and the table that reports it.
    /// </summary>
    /// <remarks>
    /// Both live in <c>Sim</c> so the console and the gate build and read the same port. These
    /// tests are what keeps that true.
    /// </remarks>
    [Category(TestCategories.Unit)]
    public class PortScenarioTests
    {
        private const string Goods = "id,base_price,volatility,heat_per_unit,supply,keep,sell_price\n" +
                                     "food,4,0.25,0.00,Local,0,1\n" +
                                     "timber,6,0.20,0.00,Local,0,1\n";

        private const string Buildings =
            "id,upkeep_coin,build_timber,build_iron,capacity,produces,output_per_day\n" +
            "farm,1,10,0,0,food,6\n" +
            "sawmill,2,0,0,0,timber,5\n";

        private const string Crew = "id,wage_coin,work_rate,food_per_day,rum_per_day\n" +
                                    "laborer,2,1.00,1.0,0.00\n";

        private static BalanceTables Balance()
        {
            var report = new ValidationReport();
            BalanceTables tables = BalanceTables.Load(
                CsvTable.Parse(Goods, "goods.csv"),
                CsvTable.Parse(Buildings, "buildings.csv"),
                CsvTable.Parse(Crew, "crew_roles.csv"),
                report);

            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Problems));
            return tables;
        }

        private static PortScenario Small()
        {
            var scenario = new PortScenario { StartingCoin = 50 };
            scenario.Crew.Add(new System.Collections.Generic.KeyValuePair<string, int>("laborer", 3));
            scenario.Buildings.Add("farm");
            scenario.Buildings.Add("sawmill");
            scenario.Stock.Add(new System.Collections.Generic.KeyValuePair<string, float>("food", 12f));
            return scenario;
        }

        [Test]
        public void A_scenario_builds_what_it_describes()
        {
            BalanceTables balance = Balance();
            World world = Small().Build(balance);

            Assert.That(world.Store<Treasury>().Values[0].Coin, Is.EqualTo(50));
            Assert.That(world.Store<CrewMember>().Count, Is.EqualTo(3));
            Assert.That(world.Store<BuildingState>().Count, Is.EqualTo(2));
            Assert.That(Port.UnitsOf(world, 0), Is.EqualTo(12f).Within(1e-4f));
        }

        [Test]
        public void Crew_and_buildings_start_whole()
        {
            World world = Small().Build(Balance());

            Assert.That(world.Store<CrewMember>().Values.ToArray().All(c => c.Morale == 1f), Is.True);
            Assert.That(world.Store<BuildingState>().Values.ToArray().All(b => b.Condition == 1f), Is.True);
            Assert.That(world.Store<BuildingState>().Values.ToArray().Any(b => b.Mothballed), Is.False);
        }

        [Test]
        public void The_same_scenario_builds_an_identical_world_every_time()
        {
            // Entity ids come from creation order, so a scenario that built in a different
            // order each run would give every replay a different world (§7.1).
            BalanceTables balance = Balance();

            var first = new HashStateWriter();
            var second = new HashStateWriter();

            Small().Build(balance).WriteTo(first);
            Small().Build(balance).WriteTo(second);

            Assert.That(second.Digest, Is.EqualTo(first.Digest));
        }

        [Test]
        public void An_unknown_id_in_a_scenario_throws_rather_than_being_skipped()
        {
            BalanceTables balance = Balance();

            var scenario = new PortScenario();
            scenario.Buildings.Add("cathedral");

            var e = Assert.Throws<System.ArgumentException>(() => scenario.Build(balance));
            Assert.That(e!.Message, Does.Contain("cathedral"));
        }

        [Test]
        public void The_default_scenario_is_consistent_with_the_shipped_content()
        {
            // It names buildings and roles by id, so it breaks the moment one is renamed —
            // which is the point of testing it rather than trusting it.
            Assert.That(PortScenario.Default().Buildings, Is.Not.Empty);
            Assert.That(PortScenario.Default().Crew, Is.Not.Empty);
        }

        // -------------------------------------------------------------------- report

        [Test]
        public void The_report_averages_over_the_crew_and_the_buildings()
        {
            BalanceTables balance = Balance();
            World world = Small().Build(balance);

            ComponentStore<CrewMember> crew = world.Store<CrewMember>();
            crew.GetRef(crew.Ids[0]).Morale = 0.4f;

            PortReport report = PortReport.Of(world, balance, day: 3);

            Assert.That(report.Day, Is.EqualTo(3));
            Assert.That(report.Crew, Is.EqualTo(3));
            Assert.That(report.AverageMorale, Is.EqualTo((0.4f + 1f + 1f) / 3f).Within(1e-4f));
            Assert.That(report.AverageCondition, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void The_report_has_a_column_for_every_good()
        {
            BalanceTables balance = Balance();
            PortReport report = PortReport.Of(Small().Build(balance), balance, day: 1);

            Assert.That(report.Stock.Count, Is.EqualTo(balance.Goods.Count));
        }

        [Test]
        public void Rows_line_up_under_the_header()
        {
            // A table whose columns wander is worse than no table.
            BalanceTables balance = Balance();
            PortReport report = PortReport.Of(Small().Build(balance), balance, day: 1);

            Assert.That(report.ToRow().Length, Is.EqualTo(PortReport.Header(balance).Length));
        }

        [Test]
        public void An_empty_world_reports_zeroes_rather_than_dividing_by_zero()
        {
            BalanceTables balance = Balance();
            PortReport report = PortReport.Of(new World(), balance, day: 1);

            Assert.That(report.AverageMorale, Is.EqualTo(0f));
            Assert.That(report.Coin, Is.EqualTo(0));
        }
    }
}
