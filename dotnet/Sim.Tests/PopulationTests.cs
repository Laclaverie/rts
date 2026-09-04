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
    /// The town (GDD §5.2.2): commoners work the buildings, eat, and eventually leave.
    /// </summary>
    /// <remarks>
    /// They exist because the Phase 2 gate found the flagship system could not reach its own
    /// failure state. Every grievance pressure used to be a count of crew, so a mismanaged port
    /// emptied of people before the ladder could climb and then reported itself Calm.
    /// </remarks>
    [Category(TestCategories.Unit)]
    public class PopulationTests
    {
        private const string Goods = "id,base_price,volatility,heat_per_unit,supply,keep,sell_price\n" +
                                     "food,4,0.25,0.00,Local,0,1\n";

        private const string Buildings =
            "id,upkeep_coin,build_timber,build_iron,capacity,produces,output_per_day,staff\n" +
            "farm,1,0,0,0,food,6,2\n" +
            "mine,1,0,0,0,food,4,1\n" +
            "longhouse,1,0,0,8,,0,0\n";

        private const string Crew = "id,wage_coin,work_rate,food_per_day,rum_per_day\n" +
                                    "laborer,2,1.00,1.0,0.00\n";

        private const string Strata =
            "id,decay_per_day,relief_per_day,food_per_day,leave_after_days," +
            "hunger_weight,unpaid_weight,desertion_weight,idle_weight\n" +
            "Commoners,0.04,0.12,0.50,3,0.04,0.00,0.03,0.005\n" +
            "NamedCrew,0.05,0.15,0.00,0,0.03,0.12,0.08,0.00\n" +
            "Merchants,0.06,0.18,0.00,0,0.00,0.00,0.00,0.00\n";

        private World _world = null!;
        private BalanceTables _balance = null!;
        private EventQueue _events = null!;

        [SetUp]
        public void SetUp()
        {
            var report = new ValidationReport();
            _balance = BalanceTables.Load(new BalanceSources
            {
                Goods = CsvTable.Parse(Goods, "goods.csv"),
                Buildings = CsvTable.Parse(Buildings, "buildings.csv"),
                CrewRoles = CsvTable.Parse(Crew, "crew_roles.csv"),
                Strata = CsvTable.Parse(Strata, "strata.csv"),
            }, report);

            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Problems));

            _world = new World();
            _events = new EventQueue();
        }

        private EntityId Town(int commoners)
        {
            EntityId e = _world.CreateEntity();
            _world.Add(e, new Population { Commoners = commoners });
            return e;
        }

        private EntityId Build(string id, bool mothballed = false)
        {
            int index = Enumerable.Range(0, _balance.Buildings.Count)
                .First(i => _balance.Buildings[i].Id == id);

            EntityId e = _world.CreateEntity();
            _world.Add(e, new BuildingState
            {
                DefinitionIndex = index,
                Condition = 1f,
                Mothballed = mothballed,
            });
            return e;
        }

        private void Run(ISystem system)
        {
            _events.BeginCause(CauseId.Root, 1);
            try
            {
                var ctx = new Context(1, 0f, _events, rng: null, balance: _balance);
                system.Run(_world, in ctx);
            }
            finally
            {
                _events.EndCause();
            }
        }

        private Population Town() => _world.Store<Population>().Values[0];

        private int WorkersAt(EntityId building) =>
            _world.Store<BuildingState>().GetRef(building).Workers;

        // -------------------------------------------------------------------- labour

        [Test]
        public void Buildings_are_filled_in_order_until_the_town_runs_out()
        {
            // Filling each before moving on, rather than spreading people evenly. Half-staffed
            // buildings all producing at half rate is strictly less output than full ones and
            // idle ones, so an even split would quietly cost the player goods.
            Town(3);
            EntityId farm = Build("farm");     // wants 2
            EntityId mine = Build("mine");     // wants 1

            Run(new LabourSystem());

            Assert.That(WorkersAt(farm), Is.EqualTo(2));
            Assert.That(WorkersAt(mine), Is.EqualTo(1));
            Assert.That(LabourSystem.UnemployedIn(_world), Is.Zero);
        }

        [Test]
        public void A_town_too_small_leaves_the_last_building_short()
        {
            Town(3);
            EntityId farm = Build("farm");
            EntityId second = Build("farm");

            Run(new LabourSystem());

            Assert.That(WorkersAt(farm), Is.EqualTo(2));
            Assert.That(WorkersAt(second), Is.EqualTo(1), "one hand short");
        }

        [Test]
        public void Whoever_is_left_over_is_unemployed()
        {
            Town(5);
            Build("mine");     // wants 1

            Run(new LabourSystem());

            Assert.That(LabourSystem.UnemployedIn(_world), Is.EqualTo(4));
        }

        [Test]
        public void A_mothballed_building_employs_nobody()
        {
            // The cost of downsizing that §5.2.3 asks the player to weigh: the upkeep stops, and
            // so does the work of the people who no longer have anywhere to be.
            Town(2);
            EntityId shut = Build("farm", mothballed: true);

            Run(new LabourSystem());

            Assert.That(WorkersAt(shut), Is.Zero);
            Assert.That(LabourSystem.UnemployedIn(_world), Is.EqualTo(2));
        }

        [Test]
        public void A_building_that_wants_nobody_takes_nobody()
        {
            Town(4);
            EntityId longhouse = Build("longhouse");

            Run(new LabourSystem());

            Assert.That(WorkersAt(longhouse), Is.Zero);
        }

        // ------------------------------------------------------------------- eating

        [Test]
        public void The_town_eats()
        {
            Town(4);
            Port.Add(_world, 0, 10f);

            Run(new ConsumptionSystem());

            Assert.That(Port.UnitsOf(_world, 0), Is.EqualTo(8f).Within(1e-4f),
                "four commoners at half a unit each");
        }

        [Test]
        public void Crew_eat_before_the_town_does()
        {
            // Not a moral claim: somebody has to be first and the order has to be fixed for the
            // run to be deterministic. It does mean a short port starves its town first, which
            // is a consequence worth seeing rather than hiding.
            Town(4);
            EntityId member = _world.CreateEntity();
            _world.Add(member, new CrewMember { RoleIndex = 0, Morale = 1f, Loyalty = 1f });
            Port.Add(_world, 0, 1f);

            Run(new ConsumptionSystem());

            Assert.That(_world.Store<CrewMember>().GetRef(member).Morale,
                Is.EqualTo(1f).Within(1e-4f), "the crew member ate");
            Assert.That(_events.Pending.Any(e => e.Is<CommonersWentHungry>()), Is.True,
                "and the town did not");
        }

        [Test]
        public void Hunger_is_reported_with_how_long_it_has_gone_on()
        {
            // People leave over a streak, not over a day, so the streak is what gets reported.
            Town(2);

            Run(new ConsumptionSystem());
            _events.Drain();
            Run(new ConsumptionSystem());

            CommonersWentHungry hungry = _events.Pending
                .First(e => e.Is<CommonersWentHungry>()).Get<CommonersWentHungry>();

            Assert.That(hungry.Commoners, Is.EqualTo(2));
            Assert.That(hungry.ConsecutiveDays, Is.EqualTo(2));
        }

        [Test]
        public void One_fed_day_stops_the_clock()
        {
            Town(2);

            Run(new ConsumptionSystem());
            _events.Drain();
            Assert.That(Town().HungryDays, Is.EqualTo(1));

            Port.Add(_world, 0, 10f);
            Run(new ConsumptionSystem());

            Assert.That(Town().HungryDays, Is.Zero,
                "one good day does not undo a famine, but it does stop the exodus");
        }

        // ---------------------------------------------------------------- departure

        [Test]
        public void Sustained_starvation_drives_people_out()
        {
            Town(5);

            for (int day = 0; day < 3; day++)
            {
                Run(new ConsumptionSystem());
                _events.Drain();
            }

            Assert.That(Town().Commoners, Is.EqualTo(4), "leave_after_days is 3 in this fixture");
            Assert.That(Town().HungryDays, Is.EqualTo(3));
        }

        [Test]
        public void Nobody_leaves_before_the_streak_is_long_enough()
        {
            // The gap between this and crew desertion is the whole point. Crew go within days of
            // a missed payday; commoners live here, and leaving means abandoning a home. If they
            // left as readily, a collapsing port would empty before the ladder could climb.
            Town(5);

            Run(new ConsumptionSystem());
            _events.Drain();
            Run(new ConsumptionSystem());

            Assert.That(Town().Commoners, Is.EqualTo(5));
        }

        [Test]
        public void A_town_can_be_emptied_in_the_end()
        {
            Town(2);

            for (int day = 0; day < 20; day++)
            {
                Run(new ConsumptionSystem());
                _events.Drain();
            }

            Assert.That(Town().Commoners, Is.Zero);
        }

        [Test]
        public void Leaving_is_reported()
        {
            Town(5);

            for (int day = 0; day < 3; day++)
            {
                Run(new ConsumptionSystem());
                if (day < 2) _events.Drain();
            }

            CommonersLeft left = _events.Pending
                .First(e => e.Is<CommonersLeft>()).Get<CommonersLeft>();

            Assert.That(left.Left, Is.EqualTo(1));
            Assert.That(left.Remaining, Is.EqualTo(4));
        }

        [Test]
        public void A_port_with_no_town_is_left_alone()
        {
            // Context is a ref struct, so it cannot be captured in Assert.DoesNotThrow.
            Build("farm");
            Port.Add(_world, 0, 5f);

            Run(new ConsumptionSystem());
            Run(new LabourSystem());

            Assert.That(Port.UnitsOf(_world, 0), Is.EqualTo(5f).Within(1e-4f));
        }
    }
}
