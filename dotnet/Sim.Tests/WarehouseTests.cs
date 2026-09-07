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
    /// Storage, and deciding what to keep (GDD §5.5, §5.3, §5.2.1).
    /// </summary>
    /// <remarks>
    /// The <c>capacity</c> column has been in <c>buildings.csv</c> since Phase 1 and did nothing
    /// at all. What a port kept was a constant in <c>goods.csv</c>, the same for every city and
    /// unchangeable — which meant rum and spice, whose keep is zero, were sold the day they
    /// appeared and could never be held, shipped or seen by anybody.
    /// </remarks>
    [Category(TestCategories.Functional)]
    public class WarehouseTests
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

        private EntityId Home => _session.PlayerPort;

        private int Good(string id) => ConsumptionSystem.IndexOf(_balance, id);

        private float Units(string good) => Port.UnitsOf(World, Home, Good(good));

        private float Reserve(string good) => Port.ReserveOf(World, Home, Good(good));

        private float Capacity => Port.Capacity(World, Home, _balance);

        private void Days(int n) { for (int i = 0; i < n; i++) _session.Step(); }

        private EntityId BuildingOf(string id)
        {
            ComponentStore<BuildingState> buildings = World.Store<BuildingState>();

            for (int i = 0; i < buildings.Count; i++)
            {
                if (!Port.BelongsTo(World, buildings.Ids[i], Home)) continue;
                if (_balance.Buildings[buildings.Values[i].DefinitionIndex].Id == id)
                    return buildings.Ids[i];
            }

            return EntityId.None;
        }

        // ------------------------------------------------------------------- capacity

        [Test]
        public void A_port_can_store_what_its_buildings_hold()
        {
            // The column that did nothing for four phases.
            Assert.That(Capacity, Is.GreaterThan(0f));
            Assert.That(Capacity,
                Is.EqualTo(_balance.Buildings["warehouse"].Capacity +
                           _balance.Buildings["longhouse"].Capacity).Within(0.01f));
        }

        [Test]
        public void Shutting_the_warehouse_takes_the_room_with_it()
        {
            // Mothballing saves two coin a day. §5.5 wants a building to be a decision rather
            // than a line of upkeep, and this is the other side of that one.
            float before = Capacity;

            _session.Submit(new MothballBuilding(BuildingOf("warehouse"), true));
            Days(1);

            Assert.That(Capacity, Is.LessThan(before));
            Assert.That(Capacity,
                Is.EqualTo(_balance.Buildings["longhouse"].Capacity).Within(0.01f));
        }

        [Test]
        public void What_will_not_fit_is_sold_whatever_the_port_would_rather_do()
        {
            // The storage cap made real. Sold rather than spoiled, because a merchant is standing
            // right there and burning it would be a punishment rather than a constraint.
            Days(3);
            float stored = _session.Stored();
            Assume.That(stored, Is.GreaterThan(_balance.Buildings["longhouse"].Capacity));

            _session.Submit(new MothballBuilding(BuildingOf("warehouse"), true));
            Days(1);

            Assert.That(_session.Stored(),
                Is.LessThanOrEqualTo(_balance.Buildings["longhouse"].Capacity + 1f),
                "the port is holding more than it has room for");
        }

        [Test]
        public void The_fullest_pile_is_the_one_that_goes()
        {
            // A port drowning in timber dumps timber.
            var world = new World();
            EntityId port = TestPort.Create(world);

            Port.Add(world, port, Good("food"), 4f);
            Port.Add(world, port, Good("timber"), 40f);

            // Both reserved in full, so the ordinary sale takes nothing and what happens next is
            // the overflow alone — which is also the claim that it ignores a reserve. Wanting to
            // keep something is not an argument against it not fitting.
            Port.SetReserve(world, port, Good("food"), 4f);
            Port.SetReserve(world, port, Good("timber"), 40f);

            EntityId shed = world.CreateEntity();
            world.Add(shed, new BuildingState
            {
                DefinitionIndex = Index("longhouse"), Condition = 1f,
            });
            TestPort.Own(world, shed, port);

            EntityId purse = world.CreateEntity();
            world.Add(purse, new Treasury { Coin = 0 });
            TestPort.Own(world, purse, port);

            RunMarket(world);

            Assert.That(Port.UnitsOf(world, port, Good("food")), Is.EqualTo(4f).Within(0.01f),
                "the small pile was untouched");
            Assert.That(Port.UnitsOf(world, port, Good("timber")), Is.LessThan(40f),
                "nothing was shed, though the port has room for eight and is holding forty-four");
        }

        private int Index(string building) =>
            Enumerable.Range(0, _balance.Buildings.Count)
                .First(i => _balance.Buildings[i].Id == building);

        private void RunMarket(World world)
        {
            var events = new RTS.Sim.Engine.Events.EventQueue();
            events.BeginCause(RTS.Sim.Engine.Events.CauseId.Root, 1);
            try
            {
                var ctx = new RTS.Sim.Engine.Pipeline.Context(1, 0f, events, rng: null,
                    balance: _balance);
                new MarketSystem().Run(world, in ctx);
            }
            finally
            {
                events.EndCause();
            }
        }

        // -------------------------------------------------------------------- reserve

        [Test]
        public void A_port_starts_keeping_what_the_content_says()
        {
            // So nothing about the economy moved when what a port keeps stopped being a constant.
            for (int i = 0; i < _balance.Goods.Count; i++)
                Assert.That(Port.ReserveOf(World, Home, i),
                    Is.EqualTo(_balance.Goods[i].Keep).Within(0.01f), _balance.Goods[i].Id);
        }

        [Test]
        public void Keeping_more_means_the_merchant_takes_less()
        {
            var greedy = new WarehouseTests();
            greedy.SetUp();

            _session.Submit(new SetReserve(Good("food"), 40f));

            Days(12);
            greedy.Days(12);

            Assert.That(Units("food"), Is.GreaterThan(greedy.Units("food")),
                "holding grain back did not hold any grain back");
        }

        [Test]
        public void Rum_can_be_held_at_last()
        {
            // The finding this phase was for. Rum's keep is zero, so it was sold the day it was
            // distilled and could never be shipped, stockpiled, or noticed by anybody — which
            // made the good the content calls valuable invisible to Heat and to trade alike.
            // Said before the first day rather than after six, because Saltmarsh's workshop runs
            // out of iron within the week and a port that has stopped distilling proves nothing
            // either way.
            _session.Submit(new SetReserve(Good("rum"), 10f));
            Days(4);

            Assert.That(Units("rum"), Is.GreaterThan(0f), "still sold the day it is made");
        }

        [Test]
        public void Holding_more_is_seen_by_more_people()
        {
            // §5.2 everywhere: the counter to one pressure feeds another. A fuller warehouse is
            // a more visible one.
            var quiet = new WarehouseTests();
            quiet.SetUp();

            _session.Submit(new SetReserve(Good("iron"), 40f));
            _session.Submit(new SetReserve(Good("rum"), 30f));

            Days(30);
            quiet.Days(30);

            Assert.That(HeatSystem.Of(World, Home),
                Is.GreaterThan(HeatSystem.Of(quiet.World, quiet.Home)));
        }

        [Test]
        public void You_cannot_reserve_more_than_you_have_room_for()
        {
            Assert.That(_session.Validate(new SetReserve(Good("food"), Capacity + 50f)),
                Is.EqualTo(CommandRejection.NotYet));
        }

        [Test]
        public void The_room_is_shared_between_goods()
        {
            // One warehouse holds whatever you put in it, so filling it with grain is a decision
            // not to fill it with iron.
            // Filled to within a few units of the ceiling, counting what the other goods are
            // already holding back. Asking for the whole capacity in one good would be refused
            // on its own and would prove nothing about sharing.
            float room = Capacity - Port.Reserved(World, Home) + Reserve("food") - 5f;

            _session.Submit(new SetReserve(Good("food"), room));
            Days(1);
            Assume.That(Reserve("food"), Is.EqualTo(room).Within(0.01f), "the first order was refused");

            Assert.That(_session.Validate(new SetReserve(Good("iron"), Reserve("iron") + 30f)),
                Is.EqualTo(CommandRejection.NotYet));
        }

        [Test]
        public void Setting_a_reserve_to_what_it_already_is_is_refused()
        {
            Assert.That(_session.Validate(new SetReserve(Good("food"), Reserve("food"))),
                Is.EqualTo(CommandRejection.AlreadyInState));
        }

        [Test]
        public void A_negative_reserve_is_refused()
        {
            Assert.That(_session.Validate(new SetReserve(Good("food"), -5f)),
                Is.EqualTo(CommandRejection.InvalidTarget));
        }

        // ------------------------------------------------------------------- on screen

        [Test]
        public void Storage_is_on_the_readout_with_what_it_is_against()
        {
            Readout storage = _session.Readouts().Single(r => r.Label == "Storage");

            Assert.That(storage.Value, Does.Contain("/"));
        }

        [Test]
        public void A_good_says_what_is_being_held_back()
        {
            Readout food = _session.Readouts().Single(r => r.Label == "food");

            Assert.That(food.Value, Does.Contain("keep"));
        }

        [Test]
        public void The_orders_offer_holding_more_and_less()
        {
            PlayerAction[] storage = _session.Actions()
                .Where(a => a.Group == "Storage").ToArray();

            Assert.That(storage.Any(a => a.Label.Contains("Keep more food")), Is.True);
            Assert.That(storage.Any(a => a.Label.Contains("Keep less food")), Is.True);
            Assert.That(storage.All(a => a.Command is SetReserve), Is.True);
        }

        [Test]
        public void A_good_the_port_has_never_seen_is_not_offered()
        {
            // Offering to stockpile spice in a city that has never had any is a row that teaches
            // nothing.
            Assert.That(
                _session.Actions().Any(a => a.Group == "Storage" && a.Label.Contains("spice")),
                Is.False);
        }
    }
}
