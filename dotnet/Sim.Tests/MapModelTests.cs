using System.IO;
using System.Linq;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Sim.Components;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Time;
using RTS.Sim.Presentation;
using RTS.Sim.Session;
using RTS.Sim.Systems;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// The map (BUILD_ORDER Phase 5, GDD P1).
    /// </summary>
    /// <remarks>
    /// Phase 5 asks for "positions, movement, a real map". These are the positions and the
    /// movement, tested without an engine — where a city is drawn and where a ship has got to
    /// are questions with right answers, and answers only a running editor can check are answers
    /// nobody checks.
    /// </remarks>
    [Category(TestCategories.Functional)]
    public class MapModelTests
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

        private MapModel Map(float dayProgress = 0f) =>
            MapModel.Of(_session.World, _balance, dayProgress);

        private EntityId City(string id)
        {
            ComponentStore<PortState> ports = _session.World.Store<PortState>();
            for (int i = 0; i < ports.Count; i++)
                if (_balance.Ports[ports.Values[i].DefinitionIndex].Id == id) return ports.Ids[i];

            return EntityId.None;
        }

        private int Good(string id) => ConsumptionSystem.IndexOf(_balance, id);

        private void Days(int n) { for (int i = 0; i < n; i++) _session.Step(); }

        // -------------------------------------------------------------------- cities

        [Test]
        public void Every_city_is_on_the_map_where_the_content_puts_it()
        {
            MapModel map = Map();

            Assert.That(map.Ports.Count, Is.EqualTo(_balance.Ports.Count));

            foreach (PortDefinition definition in _balance.Ports)
            {
                MapPort drawn = map.Ports.Single(p => p.Name == definition.Name);
                Assert.That(drawn.At.X, Is.EqualTo(definition.X).Within(1e-4f), definition.Name);
                Assert.That(drawn.At.Y, Is.EqualTo(definition.Y).Within(1e-4f), definition.Name);
            }
        }

        [Test]
        public void The_map_reads_the_same_coordinates_the_crossing_does()
        {
            // The reason the coordinates were carried before anything drew anything. A hand
            // written day count would be a second source of truth that disagrees with the map
            // the moment one exists; here the further city on screen is the longer voyage,
            // necessarily, because there is only one set of numbers.
            MapModel map = Map();
            EntityId home = _session.PlayerPort;

            MapPoint at = map.Ports.Single(p => p.Id == home).At;

            MapPort near = map.Ports.Single(p => p.Name == "Fairhaven");
            MapPort far = map.Ports.Single(p => p.Name == "Ironhold");

            Assert.That(Distance(at, near.At), Is.LessThan(Distance(at, far.At)));
            Assert.That(
                ConvoySystem.DaysBetween(_session.World, _balance, home, near.Id),
                Is.LessThan(ConvoySystem.DaysBetween(_session.World, _balance, home, far.Id)));
        }

        [Test]
        public void Exactly_one_city_is_the_players()
        {
            Assert.That(Map().Ports.Count(p => p.IsPlayer), Is.EqualTo(1));
        }

        [Test]
        public void A_marker_says_nothing_a_neighbour_would_not_tell_you()
        {
            // §5.6 makes what you know about a neighbour something you buy with a stance or a
            // scout. A map that shipped their reserves and their unrest because the world
            // happens to be one process would give the intelligence game away before it exists,
            // and it would be a hard thing to take back once players could see it.
            string[] fields = typeof(MapPort).GetFields().Select(f => f.Name).ToArray();

            Assert.That(fields, Is.EquivalentTo(new[] { "Id", "Name", "At", "IsPlayer" }));
        }

        // ------------------------------------------------------------------- bounds

        [Test]
        public void The_bounds_hold_every_city()
        {
            MapModel map = Map();

            foreach (MapPort port in map.Ports)
            {
                MapPoint unit = map.Bounds.Normalize(port.At);
                Assert.That(unit.X, Is.InRange(0f, 1f), port.Name);
                Assert.That(unit.Y, Is.InRange(0f, 1f), port.Name);
            }
        }

        [Test]
        public void The_extremes_land_on_the_edges()
        {
            var bounds = new MapBounds(-10f, -4f, 30f, 16f);

            Assert.That(bounds.Normalize(new MapPoint(-10f, -4f)).X, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(bounds.Normalize(new MapPoint(30f, 16f)).X, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(bounds.Normalize(new MapPoint(10f, 6f)).Y, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void A_world_with_no_width_is_centred_rather_than_divided_by_zero()
        {
            // One city, or several in a line. Not a hypothetical: the world was one port until
            // Phase 4 and a fixture can still build one.
            var bounds = new MapBounds(5f, 0f, 5f, 10f);

            Assert.That(bounds.Normalize(new MapPoint(5f, 5f)).X, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(bounds.Normalize(new MapPoint(5f, 5f)).Y, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void An_empty_map_can_be_asked_for_anyway()
        {
            // A front end exists before a session does, and building the panel should not depend
            // on the order boot happens to do things in.
            Assert.That(MapModel.Empty.Ports, Is.Empty);
            Assert.That(MapModel.Empty.Convoys, Is.Empty);
        }

        // ----------------------------------------------------------------- movement

        [Test]
        public void A_convoy_is_somewhere_between_the_two_cities()
        {
            EntityId ironhold = City("ironhold");
            _session.Submit(new BuyFrom(ironhold, Good("iron"), 4f));
            Days(2);

            MapConvoy convoy = Map().Convoys.Single();

            Assert.That(convoy.Progress, Is.InRange(0f, 1f));
            Assert.That(convoy.At.X, Is.InRange(
                System.Math.Min(convoy.From.X, convoy.To.X),
                System.Math.Max(convoy.From.X, convoy.To.X)));
            Assert.That(convoy.At.Y, Is.InRange(
                System.Math.Min(convoy.From.Y, convoy.To.Y),
                System.Math.Max(convoy.From.Y, convoy.To.Y)));
        }

        [Test]
        public void It_gets_nearer_every_day()
        {
            EntityId ironhold = City("ironhold");
            _session.Submit(new BuyFrom(ironhold, Good("iron"), 4f));

            float last = -1f;
            for (int day = 0; day < 4; day++)
            {
                Days(1);
                float progress = Map().Convoys.Single().Progress;

                Assert.That(progress, Is.GreaterThan(last), "day " + day);
                last = progress;
            }
        }

        [Test]
        public void It_moves_between_day_boundaries_too()
        {
            // What makes a crossing read as a journey rather than five jumps. The fraction of
            // the day is the only place real time touches any of this, and nothing derived from
            // it reaches the simulation (§7.1).
            _session.Submit(new BuyFrom(City("ironhold"), Good("iron"), 4f));
            Days(1);

            Assert.That(Map(0.75f).Convoys.Single().Progress,
                Is.GreaterThan(Map(0.25f).Convoys.Single().Progress));
        }

        [Test]
        public void It_sets_out_from_the_seller_and_is_bound_for_the_buyer()
        {
            EntityId ironhold = City("ironhold");
            _session.Submit(new BuyFrom(ironhold, Good("iron"), 4f));
            Days(1);

            MapModel map = Map();
            MapConvoy convoy = map.Convoys.Single();
            MapPoint seller = map.Ports.Single(p => p.Id == ironhold).At;
            MapPoint home = map.Ports.Single(p => p.Id == _session.PlayerPort).At;

            Assert.That(convoy.From.X, Is.EqualTo(seller.X).Within(1e-4f));
            Assert.That(convoy.To.X, Is.EqualTo(home.X).Within(1e-4f));
            Assert.That(convoy.IsPlayers, Is.True, "bought and paid for, so it is theirs to lose");
        }

        [Test]
        public void A_landed_convoy_is_off_the_map()
        {
            _session.Submit(new BuyFrom(City("fairhaven"), Good("food"), 3f));
            Days(1);
            Assert.That(Map().Convoys, Is.Not.Empty);

            Days(4);

            Assert.That(Map().Convoys, Is.Empty);
        }

        [Test]
        public void Progress_never_runs_past_the_destination()
        {
            // The last day is the one that lands it, so a fraction of a day added to a full
            // count must not overshoot and draw a ship inland.
            var convoy = new Convoy { TotalDays = 5, DaysRemaining = 1 };

            Assert.That(MapModel.Progress(in convoy, 1f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(MapModel.Progress(in convoy, 2f), Is.EqualTo(1f).Within(1e-4f));
        }

        // ---------------------------------------------------------------- selection

        [Test]
        public void The_player_starts_looking_at_their_own_city()
        {
            Assert.That(_session.Selected, Is.EqualTo(_session.PlayerPort));
        }

        [Test]
        public void Selecting_a_neighbour_offers_the_routes_to_that_one_city()
        {
            _session.Select(City("ironhold"));

            string[] labels = _session.Actions().Select(a => a.Label).ToArray();

            Assert.That(labels, Is.Not.Empty);
            Assert.That(labels.All(l => l.Contains("Ironhold")), Is.True, string.Join(" | ", labels));
        }

        [Test]
        public void Selecting_home_offers_the_orders_you_give_at_home()
        {
            _session.Select(City("ironhold"));
            _session.SelectHome();

            string[] groups = _session.Actions().Select(a => a.Group).Distinct().ToArray();

            Assert.That(groups, Does.Contain("Unrest"));
            Assert.That(groups, Does.Contain("Buildings"));
            Assert.That(groups, Does.Contain("Trade"));
        }

        [Test]
        public void Clicking_open_water_changes_nothing()
        {
            // A miss is a miss, not an error. A front end should not have to know what is
            // selectable before it asks.
            EntityId crew = _session.World.Store<CrewMember>().Ids[0];

            Assert.That(_session.Select(crew), Is.False);
            Assert.That(_session.Select(EntityId.None), Is.False);
            Assert.That(_session.Selected, Is.EqualTo(_session.PlayerPort));
        }

        [Test]
        public void Selecting_what_is_already_selected_is_not_a_change()
        {
            // So a front end can redraw on a real change rather than on every click.
            Assert.That(_session.Select(_session.PlayerPort), Is.False);
            Assert.That(_session.Select(City("ironhold")), Is.True);
            Assert.That(_session.Select(City("ironhold")), Is.False);
        }

        [Test]
        public void A_neighbour_shows_its_name_and_the_crossing_and_nothing_else()
        {
            _session.Select(City("ironhold"));

            string[] labels = _session.SelectionReadouts().Select(r => r.Label).ToArray();

            Assert.That(labels, Is.EqualTo(new[] { "City", "Crossing" }));
            Assert.That(_session.SelectionReadouts().Single(r => r.Label == "Crossing").Value,
                Is.EqualTo("5 days"));
        }

        [Test]
        public void Your_own_city_has_readouts_rather_than_a_selection_card()
        {
            Assert.That(_session.SelectionReadouts(), Is.Empty);
            Assert.That(_session.Readouts(), Is.Not.Empty);
        }

        private static double Distance(in MapPoint a, in MapPoint b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return System.Math.Sqrt((dx * dx) + (dy * dy));
        }
    }
}
