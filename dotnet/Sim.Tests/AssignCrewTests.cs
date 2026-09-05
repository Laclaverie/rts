using System.Linq;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Sim.Components;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Events;
using RTS.Sim.Engine.Pipeline;
using RTS.Sim.Systems;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// <see cref="AssignCrew"/> — the command that makes over-hiring playable rather than
    /// merely priced.
    /// </summary>
    [Category(TestCategories.Unit)]
    public class AssignCrewTests
    {
        private const string Goods = "id,base_price,volatility,heat_per_unit,supply,keep,sell_price\n" +
                                     "food,4,0.25,0.00,Local,0,1\n";

        private const string Buildings =
            "id,upkeep_coin,build_timber,build_iron,capacity,produces,output_per_day,staff\n" +
            "farm,1,0,0,0,food,6,1\n" +
            "tavern,2,0,0,0,,0,0\n";

        private const string Crew = "id,wage_coin,work_rate,food_per_day,rum_per_day\n" +
                                    "laborer,2,1.00,1.0,0.00\n";

        private World _world = null!;
        private BalanceTables _balance = null!;
        private EventQueue _events = null!;
        private EntityId _port;
        private AssignCrewHandler _handler = null!;

        private EntityId _worker;
        private EntityId _farm;
        private EntityId _tavern;

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
            _handler = new AssignCrewHandler();

            _worker = _world.CreateEntity();
            _world.Add(_worker, new CrewMember { RoleIndex = 0, Morale = 1f, Loyalty = 1f });
            TestPort.Own(_world, _worker, _port);

            _farm = _world.CreateEntity();
            _world.Add(_farm, new BuildingState { DefinitionIndex = Index("farm"), Condition = 1f });
            TestPort.Own(_world, _farm, _port);

            _tavern = _world.CreateEntity();
            _world.Add(_tavern, new BuildingState { DefinitionIndex = Index("tavern"), Condition = 1f });
            TestPort.Own(_world, _tavern, _port);
        }

        private int Index(string id) =>
            Enumerable.Range(0, _balance.Buildings.Count).First(i => _balance.Buildings[i].Id == id);

        private Context Ctx() => new Context(1, 0f, _events, rng: null, balance: _balance);

        private CommandRejection Validate(EntityId crew, EntityId building)
        {
            Context ctx = Ctx();
            return _handler.Validate(new AssignCrew(crew, building), _world, in ctx);
        }

        private void Apply(EntityId crew, EntityId building)
        {
            _events.BeginCause(CauseId.Root, 1);
            try
            {
                Context ctx = Ctx();
                _handler.Apply(new AssignCrew(crew, building), _world, in ctx);
            }
            finally
            {
                _events.EndCause();
            }
        }

        private EntityId AssignedTo(EntityId crew) =>
            _world.TryGet(crew, out Assignment a) ? a.Building : EntityId.None;

        // ------------------------------------------------------------------ applying

        [Test]
        public void An_idle_worker_can_be_put_to_work()
        {
            Assert.That(Validate(_worker, _farm), Is.EqualTo(CommandRejection.None));

            Apply(_worker, _farm);

            Assert.That(AssignedTo(_worker), Is.EqualTo(_farm));
            Assert.That(_events.Pending.Any(e => e.Is<CrewAssigned>()), Is.True);
        }

        [Test]
        public void A_worker_can_be_moved_between_buildings()
        {
            EntityId second = _world.CreateEntity();
            _world.Add(second, new BuildingState { DefinitionIndex = Index("farm"), Condition = 1f });
            TestPort.Own(_world, second, _port);

            Apply(_worker, _farm);
            Apply(_worker, second);

            Assert.That(AssignedTo(_worker), Is.EqualTo(second),
                "the same command whether they were idle or working elsewhere");
        }

        [Test]
        public void A_worker_can_be_taken_off_work()
        {
            Apply(_worker, _farm);

            Assert.That(Validate(_worker, EntityId.None), Is.EqualTo(CommandRejection.None));
            Apply(_worker, EntityId.None);

            Assert.That(AssignedTo(_worker).IsNone, Is.True);
        }

        [Test]
        public void Assigning_does_not_disturb_iteration_order()
        {
            // Overwriting must keep the entity where it was: reordering on write would change
            // the order systems see, and §7.1 makes that order part of determinism.
            EntityId second = _world.CreateEntity();
            _world.Add(second, new CrewMember { RoleIndex = 0, Morale = 1f, Loyalty = 1f });
            TestPort.Own(_world, second, _port);

            Apply(_worker, _farm);
            Apply(second, _farm);
            Apply(_worker, EntityId.None);

            ComponentStore<Assignment> assignments = _world.Store<Assignment>();
            Assert.That(assignments.Ids.ToArray(), Is.EqualTo(new[] { _worker, second }));
        }

        // ---------------------------------------------------------------- rejections

        [Test]
        public void Assigning_somebody_to_where_they_already_are_is_refused()
        {
            Apply(_worker, _farm);

            Assert.That(Validate(_worker, _farm), Is.EqualTo(CommandRejection.AlreadyInState));
        }

        [Test]
        public void Assigning_to_a_building_with_no_work_is_refused()
        {
            // Whether a building has work is data. When the port buildings of §5.5 gain staffing
            // needs, this rule follows them without being touched.
            Assert.That(Validate(_worker, _tavern), Is.EqualTo(CommandRejection.NotPermitted));
        }

        [Test]
        public void Assigning_a_dead_crew_member_is_refused()
        {
            _world.DestroyEntity(_worker);

            Assert.That(Validate(_worker, _farm), Is.EqualTo(CommandRejection.TargetGone));
        }

        [Test]
        public void Assigning_to_a_destroyed_building_is_refused()
        {
            _world.DestroyEntity(_farm);

            Assert.That(Validate(_worker, _farm), Is.EqualTo(CommandRejection.TargetGone));
        }

        [Test]
        public void Assigning_something_that_is_not_crew_is_refused()
        {
            Assert.That(Validate(_farm, _tavern), Is.EqualTo(CommandRejection.InvalidTarget));
        }

        [Test]
        public void Assigning_to_something_that_is_not_a_building_is_refused()
        {
            EntityId notABuilding = _world.CreateEntity();
            _world.Add(notABuilding, new Treasury { Coin = 1 });

            Assert.That(Validate(_worker, notABuilding), Is.EqualTo(CommandRejection.InvalidTarget));
        }

        [Test]
        public void Over_staffing_is_allowed()
        {
            // The farm wants one. Putting a second person there wastes their effort, and that is
            // the player's call: keeping someone in reserve at a building is a position, and it
            // costs wages either way.
            EntityId second = _world.CreateEntity();
            _world.Add(second, new CrewMember { RoleIndex = 0, Morale = 1f, Loyalty = 1f });
            TestPort.Own(_world, second, _port);

            Apply(_worker, _farm);

            Assert.That(Validate(second, _farm), Is.EqualTo(CommandRejection.None));
        }

        // ------------------------------------------------------------------- effect

        [Test]
        public void Moving_a_specialist_moves_the_bonus()
        {
            // The commoners working the farm are what produce the food; the specialist is what
            // makes them better at it. Posting them elsewhere costs the building its bonus, not
            // its output.
            _world.Store<BuildingState>().GetRef(_farm).Workers = 1;

            Apply(_worker, _farm);

            var production = new ProductionSystem();
            _events.BeginCause(CauseId.Root, 1);
            Context ctx = Ctx();
            production.Run(_world, in ctx);
            _events.EndCause();

            float improved = Port.UnitsOf(_world, _port, 0);
            Assert.That(improved,
                Is.EqualTo(6f * (1f + ProductionSystem.MaximumSpecialistBonus)).Within(1e-4f),
                "one farm worked, with an overseer on it");

            // Take them off the building. The hands stay, so the farm keeps working.
            Apply(_worker, EntityId.None);

            _events.BeginCause(CauseId.Root, 2);
            Context second_ctx = Ctx();
            production.Run(_world, in second_ctx);
            _events.EndCause();

            Assert.That(Port.UnitsOf(_world, _port, 0) - improved, Is.EqualTo(6f).Within(1e-4f),
                "the plain rate, with nobody overseeing it");
        }
    }
}
