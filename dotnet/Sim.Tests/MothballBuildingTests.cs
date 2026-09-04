using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Sim.Components;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Events;
using RTS.Sim.Engine.Pipeline;
using RTS.Sim.Systems;
using System.Linq;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// Shutting a building (GDD §5.2.3): the first exit from the upkeep spiral.
    /// </summary>
    /// <remarks>
    /// "Deliberate downsizing must be a viable, respected strategy." A port that cannot pay its
    /// bills needs something to do about it other than wait, and this is it: no upkeep, no
    /// output, and the crew handed back.
    /// </remarks>
    [Category(TestCategories.Unit)]
    public class MothballBuildingTests
    {
        private const string Goods = "id,base_price,volatility,heat_per_unit,supply,keep,sell_price\n" +
                                     "food,4,0.25,0.00,Local,0,1\n";

        private const string Buildings =
            "id,upkeep_coin,build_timber,build_iron,capacity,produces,output_per_day,staff\n" +
            "farm,3,0,0,0,food,6,2\n";

        private const string Crew = "id,wage_coin,work_rate,food_per_day,rum_per_day\n" +
                                    "laborer,2,1.00,1.0,0.00\n";

        private World _world = null!;
        private BalanceTables _balance = null!;
        private EventQueue _events = null!;
        private EntityId _farm;
        private EntityId _worker;

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

            _farm = _world.CreateEntity();
            _world.Add(_farm, new BuildingState { DefinitionIndex = 0, Condition = 1f });

            _worker = _world.CreateEntity();
            _world.Add(_worker, new CrewMember { RoleIndex = 0, Morale = 1f, Loyalty = 1f });
            _world.Add(_worker, new Assignment { Building = _farm });
        }

        private Context Ctx() => new Context(1, 0f, _events, rng: null, balance: _balance);

        private CommandRejection Validate(EntityId building, bool mothballed)
        {
            Context ctx = Ctx();
            return new MothballBuildingHandler()
                .Validate(new MothballBuilding(building, mothballed), _world, in ctx);
        }

        private void Apply(EntityId building, bool mothballed)
        {
            _events.BeginCause(CauseId.Root, 1);
            try
            {
                Context ctx = Ctx();
                new MothballBuildingHandler()
                    .Apply(new MothballBuilding(building, mothballed), _world, in ctx);
            }
            finally
            {
                _events.EndCause();
            }
        }

        // ----------------------------------------------------------------- shutting

        [Test]
        public void Shutting_a_building_marks_it_shut()
        {
            Apply(_farm, mothballed: true);

            Assert.That(_world.Store<BuildingState>().GetRef(_farm).Mothballed, Is.True);
            Assert.That(_events.Pending.Any(e => e.Is<BuildingMothballed>()), Is.True);
        }

        [Test]
        public void Shutting_a_building_releases_its_crew()
        {
            // Leaving them assigned to a place that produces nothing would be a silent waste:
            // still eating, still drawing wages, and the port believing they were employed.
            // Handing them back makes the cost of downsizing visible as idleness, which is a
            // grievance the player can see and act on (§5.2.2).
            Apply(_farm, mothballed: true);

            Assert.That(_world.Store<Assignment>().GetRef(_worker).Building, Is.EqualTo(EntityId.None));

            BuildingMothballed shut = _events.Pending.First(e => e.Is<BuildingMothballed>())
                .Get<BuildingMothballed>();
            Assert.That(shut.CrewReleased, Is.EqualTo(1));
        }

        [Test]
        public void Reopening_a_building_does_not_staff_it()
        {
            // Somebody has to be sent back deliberately. Reopening a building and finding it
            // already crewed would quietly undo whatever the player did with those people in
            // the meantime.
            Apply(_farm, mothballed: true);
            Apply(_farm, mothballed: false);

            Assert.That(_world.Store<BuildingState>().GetRef(_farm).Mothballed, Is.False);
            Assert.That(_world.Store<Assignment>().GetRef(_worker).Building, Is.EqualTo(EntityId.None));
        }

        [Test]
        public void Condition_survives_being_shut()
        {
            // Mothballing is not demolition. A building put away at half condition comes back at
            // half condition, or shutting one would be a decision the player could not undo.
            _world.Store<BuildingState>().GetRef(_farm).Condition = 0.5f;

            Apply(_farm, mothballed: true);
            Apply(_farm, mothballed: false);

            Assert.That(_world.Store<BuildingState>().GetRef(_farm).Condition,
                Is.EqualTo(0.5f).Within(1e-4f));
        }

        // --------------------------------------------------------------- rejections

        [Test]
        public void A_building_already_in_that_state_is_rejected()
        {
            // Not an error worth stopping for, but not a no-op either: it would appear in the
            // command log as though something happened.
            Assert.That(Validate(_farm, mothballed: false), Is.EqualTo(CommandRejection.AlreadyInState));

            Apply(_farm, mothballed: true);

            Assert.That(Validate(_farm, mothballed: true), Is.EqualTo(CommandRejection.AlreadyInState));
        }

        [Test]
        public void A_building_that_is_gone_is_rejected()
        {
            _world.DestroyEntity(_farm);

            Assert.That(Validate(_farm, mothballed: true), Is.EqualTo(CommandRejection.TargetGone));
        }

        [Test]
        public void Something_that_is_not_a_building_is_rejected()
        {
            Assert.That(Validate(_worker, mothballed: true), Is.EqualTo(CommandRejection.InvalidTarget));
        }

        // ------------------------------------------------------------------- upkeep

        [Test]
        public void A_shut_building_costs_nothing_to_keep()
        {
            // The whole point. §5.2.3's spiral is upkeep on things the port cannot afford, and
            // this is the way out of it.
            EntityId treasury = _world.CreateEntity();
            _world.Add(treasury, new Treasury { Coin = 100 });

            RunUpkeep();
            int afterOpenDay = _world.Store<Treasury>().GetRef(treasury).Coin;

            Apply(_farm, mothballed: true);
            _events.Drain();

            RunUpkeep();
            int afterShutDay = _world.Store<Treasury>().GetRef(treasury).Coin;

            Assert.That(100 - afterOpenDay, Is.EqualTo(3), "an open farm costs its upkeep");
            Assert.That(afterOpenDay - afterShutDay, Is.Zero, "a shut one costs nothing");
        }

        private void RunUpkeep()
        {
            _events.BeginCause(CauseId.Root, 1);
            try
            {
                Context ctx = Ctx();
                new UpkeepSystem().Run(_world, in ctx);
            }
            finally
            {
                _events.EndCause();
            }

            _events.Drain();
        }
    }
}
