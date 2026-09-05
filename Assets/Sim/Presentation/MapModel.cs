using System;
using System.Collections.Generic;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Systems;

namespace RTS.Sim.Presentation
{
    /// <summary>A place on the map, in the arbitrary units <c>ports.csv</c> is written in.</summary>
    public readonly struct MapPoint
    {
        public MapPoint(float x, float y)
        {
            X = x;
            Y = y;
        }

        public readonly float X;
        public readonly float Y;

        public override string ToString() => $"({X:0.##}, {Y:0.##})";
    }

    /// <summary>
    /// The extent of the inhabited world, and how to fit it into a rectangle.
    /// </summary>
    /// <remarks>
    /// Computed here rather than in the renderer so that a headless test can assert the layout.
    /// Where a city appears on screen is a question with a right answer, and a right answer that
    /// only a running Unity editor can check is one nobody checks.
    /// </remarks>
    public readonly struct MapBounds
    {
        public MapBounds(float minX, float minY, float maxX, float maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public readonly float MinX;
        public readonly float MinY;
        public readonly float MaxX;
        public readonly float MaxY;

        public float Width => MaxX - MinX;

        public float Height => MaxY - MinY;

        /// <summary>
        /// Where a point sits within the bounds, each axis 0..1.
        /// </summary>
        /// <remarks>
        /// Y is returned as it is stored, increasing northward. Screens count downward, and
        /// flipping it is the renderer's business — a model that pre-flipped for one front end
        /// would be wrong for a printout, a minimap drawn the other way, or a test.
        /// <para>
        /// A world with no extent on an axis — one city, or several in a line — normalises to
        /// the middle of it rather than dividing by zero. That is the sensible picture: with
        /// nothing to spread out, everything is centred.
        /// </para>
        /// </remarks>
        public MapPoint Normalize(in MapPoint point) => new MapPoint(
            Width > 0f ? (point.X - MinX) / Width : 0.5f,
            Height > 0f ? (point.Y - MinY) / Height : 0.5f);

        /// <summary>
        /// The extent of everything there is to draw, or a unit square if there is nothing.
        /// </summary>
        /// <remarks>
        /// The crowd counts, not only the cities. A revolt musters well outside its own city —
        /// a couple of units, against cities twenty-odd apart — so bounds taken from the cities
        /// alone would put the far side of the outermost port's crowd past the edge of the view
        /// and quietly clip the thing the player is meant to be watching.
        /// </remarks>
        public static MapBounds Around(IReadOnlyList<MapPort> ports, IReadOnlyList<MapBody> crowd)
        {
            if (ports == null || ports.Count == 0) return new MapBounds(0f, 0f, 1f, 1f);

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            for (int i = 0; i < ports.Count; i++) Widen(ports[i].At, ref minX, ref minY, ref maxX, ref maxY);

            if (crowd != null)
                for (int i = 0; i < crowd.Count; i++)
                    Widen(crowd[i].At, ref minX, ref minY, ref maxX, ref maxY);

            return new MapBounds(minX, minY, maxX, maxY);
        }

        private static void Widen(in MapPoint at, ref float minX, ref float minY, ref float maxX,
            ref float maxY)
        {
            if (at.X < minX) minX = at.X;
            if (at.Y < minY) minY = at.Y;
            if (at.X > maxX) maxX = at.X;
            if (at.Y > maxY) maxY = at.Y;
        }
    }

    /// <summary>A city as the map shows it.</summary>
    /// <remarks>
    /// Deliberately thin. §5.6 makes what you know about a neighbour something you have to buy
    /// with a stance or a scout, so a map marker carrying their reserves and their unrest would
    /// be giving away the intelligence game for free. What is here is what anyone can see from
    /// the water: where it is and what it is called.
    /// </remarks>
    public readonly struct MapPort
    {
        public MapPort(EntityId id, string name, MapPoint at, bool isPlayer)
        {
            Id = id;
            Name = name;
            At = at;
            IsPlayer = isPlayer;
        }

        public readonly EntityId Id;
        public readonly string Name;
        public readonly MapPoint At;
        public readonly bool IsPlayer;

        public override string ToString() => $"{Name} {At}";
    }

    /// <summary>A convoy as the map shows it: somewhere between two cities, and how far along.</summary>
    /// <remarks>
    /// The entity P1 exists to make visible. "Wealth is cargo, cargo moves along a route on the
    /// map, and anything on the map can be intercepted" — a convoy that was only a countdown in
    /// a list is a countdown, whatever the design document says. Here it is a thing at a place,
    /// which is what makes intercepting it a thought the player can have.
    /// </remarks>
    public readonly struct MapConvoy
    {
        public MapConvoy(EntityId id, MapPoint at, MapPoint from, MapPoint to, float progress,
            int goodIndex, float units, int daysRemaining, bool isPlayers)
        {
            Id = id;
            At = at;
            From = from;
            To = to;
            Progress = progress;
            GoodIndex = goodIndex;
            Units = units;
            DaysRemaining = daysRemaining;
            IsPlayers = isPlayers;
        }

        public readonly EntityId Id;

        /// <summary>Where it is now, including the part of today that has elapsed.</summary>
        public readonly MapPoint At;

        public readonly MapPoint From;
        public readonly MapPoint To;

        /// <summary>How much of the crossing is done, 0..1.</summary>
        public readonly float Progress;

        public readonly int GoodIndex;
        public readonly float Units;
        public readonly int DaysRemaining;

        /// <summary>Whether the player owns what is on the water — who stands to lose it.</summary>
        public readonly bool IsPlayers;
    }

    /// <summary>One body in a revolt, placed on the map (GDD §5.2.2 rung 5).</summary>
    /// <remarks>
    /// The mob stores an offset from its own city, because a revolt happens in one square and
    /// the square is wherever that city is. This is that offset added to the city, so a renderer
    /// has a place rather than an arithmetic problem.
    /// </remarks>
    public readonly struct MapBody
    {
        public MapBody(EntityId port, MapPoint at, MobSide side, bool isNamed)
        {
            Port = port;
            At = at;
            Side = side;
            IsNamed = isNamed;
        }

        /// <summary>The city whose square this is.</summary>
        public readonly EntityId Port;

        public readonly MapPoint At;
        public readonly MobSide Side;

        /// <summary>A named crew member rather than an anonymous body (§5.2.2).</summary>
        public readonly bool IsNamed;
    }

    /// <summary>
    /// Everything on the map right now (BUILD_ORDER Phase 5).
    /// </summary>
    /// <remarks>
    /// Built in <c>Sim</c>, like <c>PlayerAction</c> and <c>Readout</c> before it, so that where
    /// things are is game state rather than screen state (ARCHITECTURE §2.2). A front end
    /// positions and colours these; it does not work out where a ship is.
    /// <para>
    /// The coordinates are the ones already in <c>ports.csv</c>, which route length has been
    /// computed from since Phase 4 — so the map and the crossing cannot disagree, because there
    /// is only one set of numbers. That was the reason to carry them before anything drew
    /// anything.
    /// </para>
    /// </remarks>
    public sealed class MapModel
    {
        private static readonly MapPort[] NoPorts = new MapPort[0];
        private static readonly MapConvoy[] NoConvoys = new MapConvoy[0];
        private static readonly MapBody[] NoCrowd = new MapBody[0];

        private MapModel(IReadOnlyList<MapPort> ports, IReadOnlyList<MapConvoy> convoys,
            IReadOnlyList<MapBody> crowd, MapBounds bounds)
        {
            Ports = ports;
            Convoys = convoys;
            Crowd = crowd;
            Bounds = bounds;
        }

        public IReadOnlyList<MapPort> Ports { get; }

        public IReadOnlyList<MapConvoy> Convoys { get; }

        /// <summary>Every body on every street. Empty unless somewhere has risen.</summary>
        public IReadOnlyList<MapBody> Crowd { get; }

        public MapBounds Bounds { get; }

        /// <summary>An empty world, for a front end built before a session exists.</summary>
        public static MapModel Empty { get; } =
            new MapModel(NoPorts, NoConvoys, NoCrowd, new MapBounds(0f, 0f, 1f, 1f));

        /// <summary>
        /// Reads the world.
        /// </summary>
        /// <param name="dayProgress">
        /// How far into the current day the clock has run, 0..1. The only place real time
        /// touches this: it moves ships between day boundaries so a crossing reads as a journey
        /// rather than five jumps. Nothing derived from it reaches the simulation, so a played
        /// session and a headless replay still produce the same days (§7.1).
        /// </param>
        public static MapModel Of(World world, BalanceTables balance, float dayProgress)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            var ports = new List<MapPort>();
            var byId = new Dictionary<EntityId, MapPoint>();

            ComponentStore<PortState> states = world.Store<PortState>();
            for (int i = 0; i < states.Count; i++)
            {
                PortDefinition definition = Definition(balance, states.Values[i].DefinitionIndex);
                if (definition == null) continue;

                var at = new MapPoint(definition.X, definition.Y);
                ports.Add(new MapPort(states.Ids[i], definition.Name, at,
                    states.Values[i].IsPlayer));
                byId[states.Ids[i]] = at;
            }

            EntityId player = Port.Player(world);
            float fraction = Clamp01(dayProgress);
            var convoys = new List<MapConvoy>();

            ComponentStore<Convoy> sailing = world.Store<Convoy>();
            for (int i = 0; i < sailing.Count; i++)
            {
                Convoy convoy = sailing.Values[i];

                if (!byId.TryGetValue(convoy.Origin, out MapPoint from) ||
                    !byId.TryGetValue(convoy.Destination, out MapPoint to))
                {
                    continue;
                }

                float progress = Progress(in convoy, fraction);
                bool mine = world.TryGet(sailing.Ids[i], out Owner owner) && owner.Port == player;

                convoys.Add(new MapConvoy(
                    id: sailing.Ids[i],
                    at: Between(in from, in to, progress),
                    from: from,
                    to: to,
                    progress: progress,
                    goodIndex: convoy.GoodIndex,
                    units: convoy.Units,
                    daysRemaining: convoy.DaysRemaining,
                    isPlayers: mine));
            }

            var crowd = new List<MapBody>();

            ComponentStore<MobAgent> bodies = world.Store<MobAgent>();
            for (int i = 0; i < bodies.Count; i++)
            {
                EntityId home = Port.OwnerOf(world, bodies.Ids[i]);
                if (!byId.TryGetValue(home, out MapPoint square)) continue;

                MobAgent body = bodies.Values[i];

                // Between where it stood this morning and where it stands tonight. The sim
                // moves in whole days, so without this the crowd would jump once a day and a
                // revolt would read as a report rather than as something happening.
                float x = body.PreviousX + ((body.X - body.PreviousX) * fraction);
                float y = body.PreviousY + ((body.Y - body.PreviousY) * fraction);

                crowd.Add(new MapBody(
                    port: home,
                    at: new MapPoint(square.X + x, square.Y + y),
                    side: body.Side,
                    isNamed: !body.Crew.IsNone));
            }

            return new MapModel(ports, convoys, crowd, MapBounds.Around(ports, crowd));
        }

        /// <summary>
        /// How much of the crossing is behind it.
        /// </summary>
        /// <remarks>
        /// Days already sailed is <c>TotalDays - DaysRemaining</c>, plus however much of today
        /// has passed. A convoy is destroyed the moment its last day ticks over, so this reaches
        /// one exactly as the ship lands and never runs past it.
        /// </remarks>
        public static float Progress(in Convoy convoy, float dayProgress)
        {
            if (convoy.TotalDays <= 0) return 1f;

            float sailed = convoy.TotalDays - convoy.DaysRemaining + Clamp01(dayProgress);
            return Clamp01(sailed / convoy.TotalDays);
        }

        private static MapPoint Between(in MapPoint from, in MapPoint to, float t) =>
            new MapPoint(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t));

        private static PortDefinition Definition(BalanceTables balance, int index)
        {
            if (balance == null || index < 0 || index >= balance.Ports.Count) return null;

            return balance.Ports[index];
        }

        private static float Clamp01(float value) =>
            value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
