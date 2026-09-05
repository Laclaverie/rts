using System;
using System.Collections.Generic;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Systems;

namespace RTS.Sim.Presentation
{
    /// <summary>A building standing in the square.</summary>
    public readonly struct PortBuilding
    {
        public PortBuilding(EntityId id, string name, MapPoint at, float condition,
            bool mothballed, int workers, int staff, bool isSeat)
        {
            Id = id;
            Name = name;
            At = at;
            Condition = condition;
            Mothballed = mothballed;
            Workers = workers;
            Staff = staff;
            IsSeat = isSeat;
        }

        public readonly EntityId Id;
        public readonly string Name;
        public readonly MapPoint At;

        /// <summary>0..1. A riot takes this off it every day (§5.2.2).</summary>
        public readonly float Condition;

        public readonly bool Mothballed;
        public readonly int Workers;
        public readonly int Staff;

        /// <summary>The longhouse — what the crowd is walking towards.</summary>
        public readonly bool IsSeat;

        public override string ToString() => Name + " " + At;
    }

    /// <summary>One body in the square, close enough to be a person rather than a dot.</summary>
    public readonly struct PortBody
    {
        public PortBody(EntityId id, MapPoint at, MobSide side, string name)
        {
            Id = id;
            At = at;
            Side = side;
            Name = name;
        }

        public readonly EntityId Id;
        public readonly MapPoint At;
        public readonly MobSide Side;

        /// <summary>The crew role standing there, or empty for an anonymous body.</summary>
        public readonly string Name;

        public bool IsNamed => !string.IsNullOrEmpty(Name);
    }

    /// <summary>
    /// One city from inside it (BUILD_ORDER Phase 5).
    /// </summary>
    /// <remarks>
    /// The map answers "where is everything"; this answers "what is happening here". They are
    /// separate models because they are separate questions: at map scale a revolt is a city that
    /// has turned red, and no amount of zooming makes twelve two-pixel dots into a crowd with
    /// faces in it.
    /// <para>
    /// <strong>This is the phase's gate.</strong> "The revolt reads as an event, not a number."
    /// A crowd needs somewhere to be — buildings to stand between, a longhouse to be walking at,
    /// a scale at which a named carpenter is visibly a person who went over to them. That is
    /// what is here.
    /// </para>
    /// <para>
    /// Coordinates are the mob's own: offsets from the city, in the units <c>ports.csv</c> is
    /// written in. The crowd already lives in that space, so nothing is converted and the two
    /// views cannot disagree about where anybody is.
    /// </para>
    /// </remarks>
    public sealed class PortModel
    {
        /// <summary>The building the crowd walks at, and the one the loyalists stand in front of.</summary>
        /// <remarks>
        /// By id rather than by position in a list: the seat of power is a specific building in
        /// <c>buildings.csv</c>, and a port that had it second would otherwise put the mob's
        /// target on a farm.
        /// </remarks>
        public const string SeatOfPower = "longhouse";

        private static readonly PortBuilding[] NoBuildings = new PortBuilding[0];
        private static readonly PortBody[] NoBodies = new PortBody[0];

        private PortModel(EntityId port, string name, IReadOnlyList<PortBuilding> buildings,
            IReadOnlyList<PortBody> crowd, LadderRung rung, float radius)
        {
            Port = port;
            Name = name;
            Buildings = buildings;
            Crowd = crowd;
            Rung = rung;
            Radius = radius;
        }

        /// <summary>
        /// The city this is a view of.
        /// </summary>
        /// <remarks>
        /// Named <c>Port</c> even though it shadows the <see cref="Systems.Port"/> helpers
        /// inside this class, which is why the two calls to them below are qualified. The
        /// property is read by everything that draws this and the helpers are used twice.
        /// </remarks>
        public EntityId Port { get; }

        public string Name { get; }

        public IReadOnlyList<PortBuilding> Buildings { get; }

        /// <summary>Everybody in the square. Empty unless the port has risen.</summary>
        public IReadOnlyList<PortBody> Crowd { get; }

        /// <summary>
        /// Where the port stands on the ladder.
        /// </summary>
        /// <remarks>
        /// Present here and deliberately absent from <see cref="MapPort"/>. §5.6 makes a
        /// neighbour's unrest something you buy with a stance or a scout, so who may see this at
        /// all is <see cref="Session.GameSession.CanLookInside"/>'s decision — today, your own
        /// city and nothing else. This model answers the question; it does not decide who is
        /// allowed to ask it.
        /// </remarks>
        public LadderRung Rung { get; }

        /// <summary>How far out anything in this square goes, so a view can frame it.</summary>
        public float Radius { get; }

        public static PortModel Empty { get; } =
            new PortModel(EntityId.None, string.Empty, NoBuildings, NoBodies, LadderRung.Calm, 1f);

        /// <summary>
        /// Reads one city.
        /// </summary>
        /// <param name="dayProgress">
        /// How far into the day the clock has run, 0..1, so the crowd walks between day
        /// boundaries. Nothing derived from it reaches the world (§7.1).
        /// </param>
        public static PortModel Of(World world, BalanceTables balance, EntityId port,
            float dayProgress)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!world.IsAlive(port) || !world.Has<PortState>(port)) return Empty;

            var buildings = new List<PortBuilding>();
            var crowd = new List<PortBody>();

            ComponentStore<BuildingState> states = world.Store<BuildingState>();
            int placed = 0;

            for (int i = 0; i < states.Count; i++)
            {
                if (!Systems.Port.BelongsTo(world, states.Ids[i], port)) continue;

                BuildingState state = states.Values[i];
                Building definition = Definition(balance, state.DefinitionIndex);
                if (definition == null) continue;

                bool seat = definition.Id == SeatOfPower;

                buildings.Add(new PortBuilding(
                    id: states.Ids[i],
                    name: definition.Id,
                    at: seat ? new MapPoint(0f, 0f) : Around(placed++),
                    condition: state.Condition,
                    mothballed: state.Mothballed,
                    workers: state.Workers,
                    staff: definition.Staff,
                    isSeat: seat));
            }

            float fraction = dayProgress < 0f ? 0f : dayProgress > 1f ? 1f : dayProgress;
            float radius = BuildingRing + 1f;

            ComponentStore<MobAgent> agents = world.Store<MobAgent>();
            for (int i = 0; i < agents.Count; i++)
            {
                if (!Systems.Port.BelongsTo(world, agents.Ids[i], port)) continue;

                MobAgent agent = agents.Values[i];
                float x = agent.PreviousX + ((agent.X - agent.PreviousX) * fraction);
                float y = agent.PreviousY + ((agent.Y - agent.PreviousY) * fraction);

                crowd.Add(new PortBody(
                    id: agents.Ids[i],
                    at: new MapPoint(x, y),
                    side: agent.Side,
                    name: RoleOf(world, balance, agent.Crew)));

                float reach = (float)Math.Sqrt((x * x) + (y * y));
                if (reach > radius) radius = reach;
            }

            return new PortModel(
                port,
                NameOf(world, balance, port),
                buildings,
                crowd,
                RevolutionLadderSystem.RungOf(world, port),
                radius);
        }

        /// <summary>How far out the buildings stand from the longhouse.</summary>
        /// <remarks>
        /// Inside the mob's muster radius and outside where it presses, so the crowd arrives
        /// from beyond the town and closes through it. Those numbers are content and this one
        /// is not, which is a seam worth knowing about: a port laid out from a file would want
        /// all three in the same place.
        /// </remarks>
        public const float BuildingRing = 1.7f;

        /// <summary>
        /// Where the nth building stands.
        /// </summary>
        /// <remarks>
        /// A ring, spaced by index. Deliberately arithmetic rather than authored: §5.5 keeps the
        /// port to five building types and nobody is placing them by hand, so a layout in a file
        /// would be one more thing to keep in step with <c>ports.csv</c> for no decision gained.
        /// When a port becomes a place a player builds in, this is what a real layout replaces.
        /// <para>
        /// Two rings, because a port with a dozen buildings on one circle is a fence. The second
        /// starts where a ring of eight stops looking like a town.
        /// </para>
        /// </remarks>
        private static MapPoint Around(int index)
        {
            int ring = index / 8;
            int slot = index % 8;

            // Each ring is turned half a slot so buildings do not line up radially.
            float angle = ((slot / 8f) + (ring * 0.0625f)) * 6.2831853f;
            float radius = BuildingRing + (ring * 0.75f);

            return new MapPoint((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);
        }

        private static string RoleOf(World world, BalanceTables balance, EntityId crew)
        {
            if (crew.IsNone || balance == null) return string.Empty;
            if (!world.TryGet(crew, out CrewMember member)) return string.Empty;
            if (member.RoleIndex < 0 || member.RoleIndex >= balance.CrewRoles.Count)
                return string.Empty;

            return balance.CrewRoles[member.RoleIndex].Id;
        }

        private static string NameOf(World world, BalanceTables balance, EntityId port)
        {
            if (balance == null || !world.TryGet(port, out PortState state)) return string.Empty;
            if (state.DefinitionIndex < 0 || state.DefinitionIndex >= balance.Ports.Count)
                return string.Empty;

            return balance.Ports[state.DefinitionIndex].Name;
        }

        private static Building Definition(BalanceTables balance, int index)
        {
            if (balance == null || index < 0 || index >= balance.Buildings.Count) return null;

            return balance.Buildings[index];
        }
    }
}
