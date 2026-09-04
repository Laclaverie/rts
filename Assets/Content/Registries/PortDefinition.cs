using System;
using System.Collections.Generic;

namespace RTS.Content.Registries
{
    /// <summary>
    /// One city: where it is, what it is good at, and what it starts with (GDD §5.3).
    /// </summary>
    /// <remarks>
    /// <strong>Trade only works because ports differ.</strong> §5.3 puts it plainly: each port
    /// produces some goods and demands others, prices move with local supply, and finding and
    /// protecting a profitable differential <em>is</em> the economic game. A world of identical
    /// ports has no differential and therefore no game.
    /// <para>
    /// Specialisation is deliberately partial. A city that produced only iron would starve the
    /// moment a route closed, which makes the route a lifeline rather than a decision; a city
    /// that produced everything would never trade at all. Each has a lean toward one or two
    /// goods and a thin capability in the rest, so trade is worth doing and losing it is a
    /// setback rather than an ending.
    /// </para>
    /// <para>
    /// In a file rather than generated, because a sandbox that cannot be reproduced cannot be
    /// debugged. A seeded generator can write one of these later; the run is deterministic
    /// either way, but a file is legible without replaying anything.
    /// </para>
    /// </remarks>
    public sealed class PortDefinition : IHasId
    {
        public PortDefinition(string id, string name, float x, float y, bool isPlayer,
            int startingCoin, int commoners, IReadOnlyList<KeyValuePair<string, int>> crew,
            IReadOnlyList<string> buildings, IReadOnlyList<KeyValuePair<string, float>> stock)
        {
            Id = id;
            Name = name;
            X = x;
            Y = y;
            IsPlayer = isPlayer;
            StartingCoin = startingCoin;
            Commoners = commoners;
            Crew = crew;
            Buildings = buildings;
            Stock = stock;
        }

        public string Id { get; }

        /// <summary>What a player is shown. The id is what a file refers to.</summary>
        public string Name { get; }

        /// <summary>
        /// Where the city sits, in arbitrary units.
        /// </summary>
        /// <remarks>
        /// Carried before anything draws a map, because route length has to come from somewhere
        /// and a hand-written number of days would be a second source of truth that disagrees
        /// with the map the moment one exists. Distance is computed from these; the map, when it
        /// arrives, reads the same two numbers.
        /// </remarks>
        public float X { get; }

        public float Y { get; }

        /// <summary>Whether the player runs this one. Exactly one port may say yes.</summary>
        public bool IsPlayer { get; }

        public int StartingCoin { get; }

        /// <summary>Civilians. They work the buildings and they eat (§5.2.2).</summary>
        public int Commoners { get; }

        /// <summary>Crew role id and how many, in hiring order.</summary>
        public IReadOnlyList<KeyValuePair<string, int>> Crew { get; }

        /// <summary>Building ids, in build order. This is what the city is good at.</summary>
        public IReadOnlyList<string> Buildings { get; }

        /// <summary>Good id and starting units.</summary>
        public IReadOnlyList<KeyValuePair<string, float>> Stock { get; }

        /// <summary>How far away another city is, as the ship sails.</summary>
        public float DistanceTo(PortDefinition other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));

            float dx = other.X - X;
            float dy = other.Y - Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public override string ToString() => $"{Name} ({Id})";
    }
}
