using System;
using System.IO;
using System.Linq;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Time;
using RTS.Sim.Presentation;
using RTS.Sim.Session;
using RTS.Sim.Systems;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// One city from inside it (BUILD_ORDER Phase 5's gate).
    /// </summary>
    /// <remarks>
    /// Whether a revolt <em>reads</em> as an event is a question for a person watching one.
    /// What can be settled here is everything the picture is made of: that the square has the
    /// port's own buildings in it, that the longhouse is where the crowd is walking, and that a
    /// named face is identifiable as the crew member it belongs to.
    /// </remarks>
    [Category(TestCategories.Functional)]
    public class PortModelTests
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

        private PortModel Look(float dayProgress = 0f) =>
            PortModel.Of(_session.World, _balance, _session.PlayerPort, dayProgress);

        private EntityId City(string id)
        {
            ComponentStore<PortState> ports = _session.World.Store<PortState>();
            for (int i = 0; i < ports.Count; i++)
                if (_balance.Ports[ports.Values[i].DefinitionIndex].Id == id) return ports.Ids[i];

            return EntityId.None;
        }

        /// <summary>Robs the port daily until its people are in the square.</summary>
        private void DriveIntoTheSquare()
        {
            for (int day = 0; day < 120; day++)
            {
                _session.Submit(new Shock(ShockKind.Theft, 100000f));
                _session.Step();

                if (MobSystem.Bodies(_session.World, _session.PlayerPort) > 0) return;
            }

            Assert.Fail("the port never rose");
        }

        private static float Reach(in MapPoint at) =>
            (float)Math.Sqrt((at.X * at.X) + (at.Y * at.Y));

        // ------------------------------------------------------------------- the town

        [Test]
        public void The_square_holds_the_ports_own_buildings_and_nobody_elses()
        {
            PortModel home = Look();

            Assert.That(home.Buildings, Is.Not.Empty);
            Assert.That(home.Name, Is.EqualTo("Saltmarsh"));

            int owned = 0;
            ComponentStore<BuildingState> buildings = _session.World.Store<BuildingState>();
            for (int i = 0; i < buildings.Count; i++)
                if (Port.BelongsTo(_session.World, buildings.Ids[i], _session.PlayerPort)) owned++;

            Assert.That(home.Buildings.Count, Is.EqualTo(owned));
        }

        [Test]
        public void Each_city_is_its_own_town()
        {
            // Five cities in one world, and looking at one must not draw another's warehouses.
            PortModel home = Look();
            PortModel ironhold = PortModel.Of(_session.World, _balance, City("ironhold"), 0f);

            Assert.That(ironhold.Name, Is.EqualTo("Ironhold"));
            Assert.That(ironhold.Buildings.Select(b => b.Name),
                Is.Not.EqualTo(home.Buildings.Select(b => b.Name)));
        }

        [Test]
        public void The_longhouse_is_where_the_crowd_is_walking()
        {
            // The mob steers at its city's centre, so the seat of power has to be the thing
            // standing there. A longhouse placed in the ring like everything else would leave
            // the crowd converging on an empty patch of ground.
            PortBuilding seat = Look().Buildings.Single(b => b.IsSeat);

            Assert.That(seat.Name, Is.EqualTo(PortModel.SeatOfPower));
            Assert.That(Reach(seat.At), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void Everything_else_stands_around_it()
        {
            foreach (PortBuilding building in Look().Buildings.Where(b => !b.IsSeat))
                Assert.That(Reach(building.At), Is.GreaterThan(1f), building.Name);
        }

        [Test]
        public void No_two_buildings_stand_in_the_same_place()
        {
            PortBuilding[] buildings = Look().Buildings.ToArray();

            for (int i = 0; i < buildings.Length; i++)
            {
                for (int j = i + 1; j < buildings.Length; j++)
                {
                    float dx = buildings[i].At.X - buildings[j].At.X;
                    float dy = buildings[i].At.Y - buildings[j].At.Y;

                    Assert.That(Math.Sqrt((dx * dx) + (dy * dy)), Is.GreaterThan(0.3f),
                        $"{buildings[i].Name} and {buildings[j].Name}");
                }
            }
        }

        [Test]
        public void A_port_with_a_dozen_buildings_is_a_town_rather_than_a_fence()
        {
            // One ring of twelve is a circle of huts with a hole in it. The second ring is the
            // difference between a place and a perimeter.
            var seen = new System.Collections.Generic.HashSet<int>();

            for (int i = 0; i < 12; i++)
            {
                EntityId shed = _session.World.CreateEntity();
                _session.World.Add(shed, new BuildingState { DefinitionIndex = 1, Condition = 1f });
                _session.World.Add(shed, new Owner { Port = _session.PlayerPort });
            }

            foreach (PortBuilding building in Look().Buildings.Where(b => !b.IsSeat))
                seen.Add((int)Math.Round(Reach(building.At) * 10f));

            Assert.That(seen.Count, Is.GreaterThan(1), "everything is on one circle");
        }

        [Test]
        public void A_building_carries_what_the_player_would_want_to_read_off_it()
        {
            PortBuilding any = Look().Buildings.First(b => !b.IsSeat);

            Assert.That(any.Condition, Is.InRange(0f, 1f));
            Assert.That(any.Name, Is.Not.Empty);
            Assert.That(any.Staff, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void You_may_look_inside_your_own_city_and_no_other()
        {
            // §5.6 makes what you know about a neighbour something you buy with a stance or a
            // scout, and the inside of a city is its buildings, their condition and its rung —
            // the intelligence game entire. The map already withholds all of it; the close-up
            // must not hand it over because the world happens to be one process.
            Assert.That(_session.CanLookInside(_session.PlayerPort), Is.True);
            Assert.That(_session.CanLookInside(City("ironhold")), Is.False);
            Assert.That(_session.CanLookInside(EntityId.None), Is.False);
        }

        [Test]
        public void Somewhere_that_is_not_a_city_draws_nothing()
        {
            EntityId crew = _session.World.Store<CrewMember>().Ids[0];

            Assert.That(PortModel.Of(_session.World, _balance, crew, 0f).Buildings, Is.Empty);
            Assert.That(PortModel.Of(_session.World, _balance, EntityId.None, 0f).Name,
                Is.Empty);
        }

        // ------------------------------------------------------------------ the revolt

        [Test]
        public void A_calm_port_has_an_empty_square()
        {
            Assert.That(Look().Crowd, Is.Empty);
            Assert.That(Look().Rung, Is.EqualTo(LadderRung.Calm));
        }

        [Test]
        public void A_risen_port_has_its_people_in_it()
        {
            DriveIntoTheSquare();

            PortModel home = Look();

            Assert.That(home.Crowd, Is.Not.Empty);
            Assert.That(home.Rung, Is.GreaterThanOrEqualTo(MobSystem.MustersAt));
        }

        [Test]
        public void A_named_face_says_who_it_is()
        {
            // The half of §5.2.2 the map view cannot deliver. At map scale a face is a slightly
            // larger dot; here it is the carpenter, and which way they went is the sentence the
            // rung is made of.
            var world = new World();
            EntityId port = world.CreateEntity();
            world.Add(port, new PortState { DefinitionIndex = 0, IsPlayer = true });

            EntityId member = world.CreateEntity();
            world.Add(member, new CrewMember { RoleIndex = 0, Loyalty = 0.1f });
            world.Add(member, new Owner { Port = port });

            EntityId body = world.CreateEntity();
            world.Add(body, new MobAgent { Side = MobSide.Rioter, Crew = member, X = 1f });
            world.Add(body, new Owner { Port = port });

            PortBody drawn = PortModel.Of(world, _balance, port, 0f).Crowd.Single();

            Assert.That(drawn.IsNamed, Is.True);
            Assert.That(drawn.Name, Is.EqualTo(_balance.CrewRoles[0].Id));
            Assert.That(drawn.Side, Is.EqualTo(MobSide.Rioter));
        }

        [Test]
        public void An_anonymous_body_has_no_name_to_give()
        {
            DriveIntoTheSquare();

            Assert.That(Look().Crowd.Any(b => !b.IsNamed), Is.True);
        }

        [Test]
        public void The_crowd_walks_between_day_boundaries_here_too()
        {
            // The same interpolation the map does, from the same two positions, so the two
            // views cannot disagree about where anybody is standing.
            DriveIntoTheSquare();
            _session.Step();

            PortBody early = Look(0.1f).Crowd.First();
            PortBody late = Look(0.9f).Crowd.First(b => b.Id == early.Id);

            Assert.That(Reach(late.At), Is.LessThan(Reach(early.At)));
        }

        [Test]
        public void The_view_is_framed_wide_enough_to_hold_what_is_in_it()
        {
            DriveIntoTheSquare();

            PortModel home = Look();

            foreach (PortBody body in home.Crowd)
                Assert.That(Reach(body.At), Is.LessThanOrEqualTo(home.Radius + 1e-3f));

            foreach (PortBuilding building in home.Buildings)
                Assert.That(Reach(building.At), Is.LessThanOrEqualTo(home.Radius + 1e-3f));
        }

        [Test]
        public void The_town_stands_between_where_they_gather_and_where_they_stop()
        {
            // So the crowd arrives from beyond the buildings and closes through them, rather
            // than materialising inside the town or stopping outside it.
            Assert.That(_balance.Mob.PressRadius, Is.LessThan(PortModel.BuildingRing),
                "they stop before they reach the town");
            Assert.That(_balance.Mob.MusterRadius, Is.GreaterThan(PortModel.BuildingRing),
                "they gather beyond it");
        }
    }
}
