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
    /// The four day-boundary systems, one behaviour per test (§8.1).
    /// </summary>
    [Category(TestCategories.Unit)]
    public class EconomySystemsTests
    {
        // A deliberately tiny economy: one good, one producer, one role.
        private const string Goods = "id,base_price,volatility,heat_per_unit,supply\n" +
                                     "food,4,0.25,0.00,Local\n";

        private const string Buildings = "id,upkeep_coin,build_timber,build_iron,capacity,produces,output_per_day\n" +
                                         "farm,3,0,0,0,food,6\n" +
                                         "longhouse,2,0,0,8,,0\n";

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

            // The fixture must be valid, or every test below is measuring the wrong thing.
            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Problems));
            return tables;
        }

        private sealed class Port
        {
            public World World = new World();
            public EventQueue Events = new EventQueue();
            public BalanceTables Tables = Balance();

            public Context Context(int day = 1) =>
                new Context(day, 0f, Events, rng: null, balance: Tables);

            public EntityId AddTreasury(int coin)
            {
                EntityId e = World.CreateEntity();
                World.Add(e, new Treasury { Coin = coin });
                return e;
            }

            public EntityId AddCrew(float morale = 1f, float loyalty = 1f, int roleIndex = 0)
            {
                EntityId e = World.CreateEntity();
                World.Add(e, new CrewMember { RoleIndex = roleIndex, Morale = morale, Loyalty = loyalty });
                return e;
            }

            public EntityId AddBuilding(string id, float condition = 1f, bool mothballed = false)
            {
                int index = Enumerable.Range(0, Tables.Buildings.Count)
                    .First(i => Tables.Buildings[i].Id == id);

                EntityId e = World.CreateEntity();
                World.Add(e, new BuildingState
                {
                    DefinitionIndex = index,
                    Condition = condition,
                    Mothballed = mothballed,
                });
                return e;
            }

            public void AddFood(float units) => Systems.Port.Add(World, 0, units);

            public float Food => Systems.Port.UnitsOf(World, 0);

            public int Coin => World.Store<Treasury>().Values[0].Coin;

            public int Arrears => World.Store<Treasury>().Values[0].Arrears;

            public CrewMember Crew(EntityId id) => World.Store<CrewMember>().GetRef(id);

            public BuildingState Building(EntityId id) => World.Store<BuildingState>().GetRef(id);

            /// <summary>Runs one system inside a cause scope, as the pipeline would.</summary>
            public void Run(ISystem system, int day = 1)
            {
                Events.BeginCause(CauseId.Root, day);
                try
                {
                    Context ctx = Context(day);
                    system.Run(World, in ctx);
                }
                finally
                {
                    Events.EndCause();
                }
            }

            public bool Emitted<T>() where T : struct => Events.Pending.Any(e => e.Is<T>());

            public T First<T>() where T : struct => Events.Pending.First(e => e.Is<T>()).Get<T>();
        }

        // ------------------------------------------------------------- consumption

        [Test]
        public void Crew_eat_from_stock()
        {
            var port = new Port();
            port.AddCrew();
            port.AddFood(10f);

            port.Run(new ConsumptionSystem());

            Assert.That(port.Food, Is.EqualTo(9f).Within(1e-4f));
        }

        [Test]
        public void Unfed_crew_lose_morale_and_the_shortfall_is_reported()
        {
            var port = new Port();
            EntityId member = port.AddCrew(morale: 1f);
            // No food at all.

            port.Run(new ConsumptionSystem());

            Assert.That(port.Crew(member).Morale,
                Is.EqualTo(1f - ConsumptionSystem.HungerMoralePenalty).Within(1e-4f));
            Assert.That(port.Emitted<FoodShortfall>(), Is.True);
            Assert.That(port.First<FoodShortfall>().Crew, Is.EqualTo(1));
        }

        [Test]
        public void Fed_crew_recover_morale_more_slowly_than_they_lose_it()
        {
            var port = new Port();
            EntityId member = port.AddCrew(morale: 0.5f);
            port.AddFood(10f);

            port.Run(new ConsumptionSystem());

            Assert.That(port.Crew(member).Morale,
                Is.EqualTo(0.5f + ConsumptionSystem.FedMoraleRecovery).Within(1e-4f));
            Assert.That(ConsumptionSystem.FedMoraleRecovery,
                Is.LessThan(ConsumptionSystem.HungerMoralePenalty),
                "recovery must be slower than loss, or hunger has no lasting cost");
        }

        [Test]
        public void Partial_food_feeds_some_and_starves_the_rest()
        {
            var port = new Port();
            port.AddCrew();
            port.AddCrew();
            port.AddCrew();
            port.AddFood(2f);   // enough for two of the three

            port.Run(new ConsumptionSystem());

            Assert.That(port.Food, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(port.First<FoodShortfall>().Crew, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ wages

        [Test]
        public void Wages_are_paid_from_the_treasury()
        {
            var port = new Port();
            port.AddTreasury(10);
            port.AddCrew();
            port.AddCrew();

            port.Run(new WagesSystem());

            Assert.That(port.Coin, Is.EqualTo(6));
            Assert.That(port.Emitted<WagesPaid>(), Is.True);
        }

        [Test]
        public void Unpaid_wages_cost_morale_and_loyalty_and_accrue_arrears()
        {
            // The first link in the cascade (§5.2.3).
            var port = new Port();
            port.AddTreasury(0);
            EntityId member = port.AddCrew(morale: 1f, loyalty: 1f);

            port.Run(new WagesSystem());

            CrewMember state = port.Crew(member);
            Assert.That(state.Morale, Is.EqualTo(1f - WagesSystem.UnpaidMoralePenalty).Within(1e-4f));
            Assert.That(state.Loyalty, Is.EqualTo(1f - WagesSystem.UnpaidLoyaltyPenalty).Within(1e-4f));
            Assert.That(port.Arrears, Is.EqualTo(2), "what was owed is not forgiven silently");
            Assert.That(port.Emitted<WagesUnpaid>(), Is.True);
        }

        [Test]
        public void Wages_are_paid_in_full_per_member_until_the_coin_runs_out()
        {
            // Partial pay would be a negotiation, and this port does not negotiate.
            var port = new Port();
            port.AddTreasury(3);        // enough for one of two at 2 coin each
            port.AddCrew();
            port.AddCrew();

            port.Run(new WagesSystem());

            Assert.That(port.Coin, Is.EqualTo(1));
            Assert.That(port.First<WagesUnpaid>().Crew, Is.EqualTo(1));
        }

        // ----------------------------------------------------------------- upkeep

        [Test]
        public void Upkeep_is_charged_whether_or_not_the_building_earned_anything()
        {
            var port = new Port();
            port.AddTreasury(10);
            port.AddBuilding("longhouse");   // produces nothing

            port.Run(new UpkeepSystem());

            Assert.That(port.Coin, Is.EqualTo(8));
            Assert.That(port.Emitted<UpkeepPaid>(), Is.True);
        }

        [Test]
        public void Unpaid_upkeep_decays_the_building()
        {
            var port = new Port();
            port.AddTreasury(0);
            EntityId farm = port.AddBuilding("farm", condition: 1f);

            port.Run(new UpkeepSystem());

            Assert.That(port.Building(farm).Condition,
                Is.EqualTo(1f - UpkeepSystem.NeglectDecay).Within(1e-4f));
            Assert.That(port.Emitted<UpkeepUnpaid>(), Is.True);
        }

        [Test]
        public void A_mothballed_building_costs_nothing_and_does_not_decay()
        {
            // One of the explicit exits from the spiral (§5.2.3).
            var port = new Port();
            port.AddTreasury(0);
            EntityId farm = port.AddBuilding("farm", condition: 0.5f, mothballed: true);

            port.Run(new UpkeepSystem());

            Assert.That(port.Building(farm).Condition, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(port.Emitted<UpkeepUnpaid>(), Is.False);
        }

        [Test]
        public void A_building_reaching_zero_condition_is_reported_once()
        {
            var port = new Port();
            port.AddTreasury(0);
            port.AddBuilding("farm", condition: UpkeepSystem.NeglectDecay);

            port.Run(new UpkeepSystem());
            Assert.That(port.Emitted<BuildingDerelict>(), Is.True);

            port.Events.Drain();
            port.Run(new UpkeepSystem(), day: 2);

            Assert.That(port.Emitted<BuildingDerelict>(), Is.False,
                "already derelict; reporting it every day would drown the feed");
        }

        // ------------------------------------------------------------- production

        [Test]
        public void A_producer_adds_its_output()
        {
            var port = new Port();
            port.AddBuilding("farm");
            port.AddCrew();

            port.Run(new ProductionSystem());

            Assert.That(port.Food, Is.EqualTo(6f).Within(1e-4f));
        }

        [Test]
        public void Output_scales_with_condition()
        {
            var port = new Port();
            port.AddBuilding("farm", condition: 0.5f);
            port.AddCrew();

            port.Run(new ProductionSystem());

            Assert.That(port.Food, Is.EqualTo(3f).Within(1e-4f));
        }

        [Test]
        public void Nothing_is_produced_without_crew()
        {
            var port = new Port();
            port.AddBuilding("farm");

            port.Run(new ProductionSystem());

            Assert.That(port.Food, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void A_mothballed_producer_produces_nothing()
        {
            var port = new Port();
            port.AddBuilding("farm", mothballed: true);
            port.AddCrew();

            port.Run(new ProductionSystem());

            Assert.That(port.Food, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void Low_morale_lowers_output()
        {
            // The link that gives the cascade its teeth: unpaid crew produce less, so income
            // falls, so wages get harder to pay (§5.2.3).
            var eager = new Port();
            eager.AddBuilding("farm");
            eager.AddCrew(morale: 1f);
            eager.Run(new ProductionSystem());

            var resentful = new Port();
            resentful.AddBuilding("farm");
            resentful.AddCrew(morale: 0f);
            resentful.Run(new ProductionSystem());

            Assert.That(resentful.Food, Is.LessThan(eager.Food));
            Assert.That(resentful.Food, Is.GreaterThan(0f),
                "people who are fed up work badly; they do not evaporate");
        }

        [Test]
        public void Two_producers_and_one_worker_split_the_labour()
        {
            var port = new Port();
            port.AddBuilding("farm");
            port.AddBuilding("farm");
            port.AddCrew();

            port.Run(new ProductionSystem());

            // One worker across two producers: each runs at half staffing, 6 * 0.5 twice.
            Assert.That(port.Food, Is.EqualTo(6f).Within(1e-4f));
        }

        // ------------------------------------------------------------ a whole day

        private static Pipeline DayBoundary() =>
            Pipeline.Build(
                CsvTable.Parse(
                    "phase,order,system,enabled\n" +
                    "DayBoundary,10,Consumption,true\n" +
                    "DayBoundary,20,Wages,true\n" +
                    "DayBoundary,30,Upkeep,true\n" +
                    "DayBoundary,40,Production,true\n",
                    "pipeline.csv"),
                new ISystem[]
                {
                    new ConsumptionSystem(), new WagesSystem(),
                    new UpkeepSystem(), new ProductionSystem(),
                });

        private static void RunDays(Port port, int days)
        {
            Pipeline pipeline = DayBoundary();

            for (int day = 1; day <= days; day++)
            {
                Context ctx = port.Context(day);
                pipeline.Run(Phase.DayBoundary, port.World, ctx);
                port.Events.Drain();
            }
        }

        [Test]
        public void A_funded_port_holds_steady()
        {
            var port = new Port();
            port.AddTreasury(500);
            EntityId member = port.AddCrew();
            EntityId farm = port.AddBuilding("farm");
            port.AddFood(20f);

            RunDays(port, days: 10);

            Assert.That(port.Crew(member).Morale, Is.EqualTo(1f).Within(1e-3f), "fed and paid");
            Assert.That(port.Building(farm).Condition, Is.EqualTo(1f).Within(1e-3f), "maintained");
            Assert.That(port.Food, Is.GreaterThan(20f), "one worker outproduces one eater");
            Assert.That(port.Arrears, Is.EqualTo(0));
        }

        [Test]
        public void An_unfunded_port_degrades_on_every_axis()
        {
            // Not the Phase 1 gate — that measures the shape of the curve. This only asserts
            // that the links exist at all, so the gate has something to measure.
            var port = new Port();
            port.AddTreasury(0);
            EntityId member = port.AddCrew();
            EntityId farm = port.AddBuilding("farm");

            RunDays(port, days: 5);

            // Direction, not magnitude. How fast this falls is exactly what the Phase 1 gate
            // tunes, and asserting a threshold here would mean re-editing this test on every
            // balance change until someone deleted it.
            //
            // Worth knowing while tuning: an unpaid crew that can still feed itself declines at
            // about 0.05 morale a day — the -0.10 for going unpaid against the +0.05 for
            // eating — so this is a slow drift rather than a spiral. Whether that is the right
            // shape is the gate's question.
            Assert.That(port.Crew(member).Morale, Is.LessThan(1f), "unpaid");
            Assert.That(port.Building(farm).Condition, Is.LessThan(1f), "upkeep unpaid");
            Assert.That(port.Arrears, Is.GreaterThan(0));
        }

        [Test]
        public void Falling_morale_visibly_feeds_back_into_output()
        {
            // The loop, over two days: unpaid on day one, so day two produces less than day one.
            var port = new Port();
            port.AddTreasury(0);
            port.AddCrew(morale: 1f);
            port.AddBuilding("farm");

            Pipeline pipeline = DayBoundary();

            Context first = port.Context(1);
            pipeline.Run(Phase.DayBoundary, port.World, first);
            port.Events.Drain();
            float afterDayOne = port.Food;

            Context second = port.Context(2);
            pipeline.Run(Phase.DayBoundary, port.World, second);
            port.Events.Drain();
            float dayTwoOutput = port.Food - afterDayOne;

            Assert.That(dayTwoOutput, Is.LessThan(afterDayOne),
                "morale fell overnight, so the second day produced less than the first");
        }
    }
}
