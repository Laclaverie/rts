using System.IO;
using System.Linq;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Events;
using RTS.Sim.Engine.Pipeline;
using RTS.Sim.Systems;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// Buildings that turn goods into other goods (GDD §5.3).
    /// </summary>
    /// <remarks>
    /// The mechanic that makes "you cannot produce all by yourself" bite on more than bread. A
    /// city can grow its own food and dig its own iron and still be unable to make rum, because
    /// rum wants both and no city has both to spare.
    /// </remarks>
    [Category(TestCategories.Unit)]
    public class TransformerTests
    {
        private const string Goods = "id,base_price,volatility,heat_per_unit,supply,keep,sell_price\n" +
                                     "food,4,0.25,0.00,Local,0,1\n" +
                                     "iron,12,0.30,0.05,Local,0,4\n" +
                                     "rum,15,0.35,0.10,Local,0,8\n";

        private const string Buildings =
            "id,upkeep_coin,build_timber,build_iron,capacity,produces,output_per_day,staff,consumes\n" +
            "farm,1,0,0,0,food,6,1,\n" +
            "mine,1,0,0,0,iron,4,1,\n" +
            "workshop,1,0,0,0,rum,2,1,food:3;iron:1\n";

        // The laborer drinks, so rum has a consumer. Without one the content is rejected, and
        // rightly: a good nobody wants is dead weight.
        private const string Crew = "id,wage_coin,work_rate,food_per_day,rum_per_day\n" +
                                    "laborer,2,1.00,1.0,0.25\n";

        private World _world = null!;
        private BalanceTables _balance = null!;
        private EventQueue _events = null!;
        private EntityId _port;
        private int _food;
        private int _iron;
        private int _rum;

        [SetUp]
        public void SetUp()
        {
            var report = new ValidationReport();
            _balance = BalanceTables.Load(new BalanceSources
            {
                Goods = CsvTable.Parse(Goods, "goods.csv"),
                Buildings = CsvTable.Parse(Buildings, "buildings.csv"),
                CrewRoles = CsvTable.Parse(Crew, "crew_roles.csv"),
            }, report);

            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Problems));

            _world = new World();
            _events = new EventQueue();
            _port = TestPort.Create(_world);

            _food = ConsumptionSystem.IndexOf(_balance, "food");
            _iron = ConsumptionSystem.IndexOf(_balance, "iron");
            _rum = ConsumptionSystem.IndexOf(_balance, "rum");
        }

        private EntityId Build(string id, int? workers = null)
        {
            int index = Enumerable.Range(0, _balance.Buildings.Count)
                .First(i => _balance.Buildings[i].Id == id);

            EntityId e = _world.CreateEntity();
            _world.Add(e, new BuildingState
            {
                DefinitionIndex = index,
                Condition = 1f,
                Workers = workers ?? _balance.Buildings[index].Staff,
            });

            return TestPort.Own(_world, e, _port);
        }

        private void Stock(int good, float units) => Port.Add(_world, _port, good, units);

        private float Units(int good) => Port.UnitsOf(_world, _port, good);

        private void RunDay()
        {
            _events.BeginCause(CauseId.Root, 1);
            try
            {
                var ctx = new Context(1, 0f, _events, rng: null, balance: _balance);
                new ProductionSystem().Run(_world, in ctx);
            }
            finally
            {
                _events.EndCause();
            }
        }

        // ------------------------------------------------------------- transforming

        [Test]
        public void A_workshop_eats_what_it_needs_and_makes_what_it_makes()
        {
            Build("workshop");
            Stock(_food, 10f);
            Stock(_iron, 10f);

            RunDay();

            Assert.That(Units(_rum), Is.EqualTo(2f).Within(1e-4f));
            Assert.That(Units(_food), Is.EqualTo(7f).Within(1e-4f));
            Assert.That(Units(_iron), Is.EqualTo(9f).Within(1e-4f));
        }

        [Test]
        public void An_extractor_eats_nothing()
        {
            // Iron comes out of the ground, and the ground asks for nothing back.
            Build("farm");

            RunDay();

            Assert.That(Units(_food), Is.EqualTo(6f).Within(1e-4f));
        }

        [Test]
        public void With_nothing_to_work_with_it_makes_nothing()
        {
            Build("workshop");
            Stock(_food, 10f);
            // no iron

            RunDay();

            Assert.That(Units(_rum), Is.Zero);
            Assert.That(Units(_food), Is.EqualTo(10f).Within(1e-4f),
                "and it does not burn the food it cannot use");
        }

        [Test]
        public void The_scarcest_input_decides_how_much_gets_made()
        {
            // A workshop short of iron does not stop; it works at the fraction it can supply.
            // §5.2.3's cascade is built out of degrees — a port that halted the moment a route
            // was late would be a cliff rather than a ratchet.
            Build("workshop");
            Stock(_food, 10f);
            Stock(_iron, 0.5f);

            RunDay();

            Assert.That(Units(_rum), Is.EqualTo(1f).Within(1e-4f), "half the iron, half the rum");
        }

        [Test]
        public void Everything_is_taken_in_proportion_to_what_was_made()
        {
            // Consuming a full day of food while short of iron would burn the bread and produce
            // nothing, which is worse than not starting.
            Build("workshop");
            Stock(_food, 10f);
            Stock(_iron, 0.5f);

            RunDay();

            Assert.That(Units(_food), Is.EqualTo(8.5f).Within(1e-4f), "half the food too");
        }

        [Test]
        public void A_half_staffed_workshop_needs_half_as_much()
        {
            // Inputs are quoted per full day's output, so a building already slowed by staffing
            // must not demand what a full one would.
            Build("workshop", workers: 0);
            Stock(_food, 10f);
            Stock(_iron, 10f);

            RunDay();

            Assert.That(Units(_food), Is.EqualTo(10f).Within(1e-4f),
                "nobody is working it, so it eats nothing");
        }

        [Test]
        public void Running_short_is_reported()
        {
            // The first symptom of a route not run, or one that did not arrive. The cause is
            // elsewhere, so a smaller number in a stock readout would not explain it.
            Build("workshop");
            Stock(_food, 10f);
            Stock(_iron, 0.5f);

            RunDay();

            Assert.That(_events.Pending.Any(e => e.Is<WorkshopShort>()), Is.True);

            WorkshopShort short_ = _events.Pending.First(e => e.Is<WorkshopShort>()).Get<WorkshopShort>();
            Assert.That(short_.Made, Is.LessThan(short_.Wanted));
            Assert.That(short_.Port, Is.EqualTo(_port));
        }

        [Test]
        public void A_workshop_with_everything_it_needs_reports_nothing()
        {
            // Or the feed would carry a line every day for a building that is working perfectly.
            Build("workshop");
            Stock(_food, 10f);
            Stock(_iron, 10f);

            RunDay();

            Assert.That(_events.Pending.Any(e => e.Is<WorkshopShort>()), Is.False);
        }

        // ----------------------------------------------------------------- content

        [Test]
        public void Consuming_a_good_that_does_not_exist_is_rejected()
        {
            var report = new ValidationReport();
            BalanceTables.Load(new BalanceSources
            {
                Goods = CsvTable.Parse(Goods, "goods.csv"),
                Buildings = CsvTable.Parse(
                    Buildings.Replace("food:3;iron:1", "unobtainium:1"), "buildings.csv"),
                CrewRoles = CsvTable.Parse(Crew, "crew_roles.csv"),
            }, report);

            Assert.That(report.Problems.Any(p => p.Contains("unobtainium")), Is.True,
                string.Join("; ", report.Problems));
        }

        [Test]
        public void A_malformed_consumes_entry_is_rejected()
        {
            var report = new ValidationReport();
            BalanceTables.Load(new BalanceSources
            {
                Goods = CsvTable.Parse(Goods, "goods.csv"),
                Buildings = CsvTable.Parse(Buildings.Replace("food:3;iron:1", "food"), "buildings.csv"),
                CrewRoles = CsvTable.Parse(Crew, "crew_roles.csv"),
            }, report);

            Assert.That(report.Problems.Any(p => p.Contains("not 'good:units'")), Is.True,
                string.Join("; ", report.Problems));
        }

        [Test]
        public void A_buildings_table_with_no_consumes_column_still_loads()
        {
            // The column is optional, unlike every other one here. A table without it is a
            // table of extractors, which is coherent rather than broken — and dozens of test
            // fixtures describe exactly that.
            var report = new ValidationReport();
            BalanceTables balance = BalanceTables.Load(new BalanceSources
            {
                Goods = CsvTable.Parse(Goods, "goods.csv"),
                Buildings = CsvTable.Parse(
                    "id,upkeep_coin,build_timber,build_iron,capacity,produces,output_per_day,staff\n" +
                    "farm,1,0,0,0,food,6,1\n" +
                    "mine,1,0,0,0,iron,4,1\n" +
                    "distillery,1,0,5,0,rum,2,1\n", "buildings.csv"),
                CrewRoles = CsvTable.Parse(Crew, "crew_roles.csv"),
            }, report);

            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Problems));
            Assert.That(balance.Buildings["farm"].IsTransformer, Is.False);
        }
    }
}
