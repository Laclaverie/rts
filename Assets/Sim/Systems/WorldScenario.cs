using System;
using System.Collections.Generic;
using RTS.Content.Registries;
using RTS.Sim.Engine.Entities;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Builds a world of cities from <c>ports.csv</c> (GDD §5.3).
    /// </summary>
    /// <remarks>
    /// Each row becomes a port, built whole before the next begins. Creation order decides every
    /// id in the world (§7.1), so building one city at a time keeps a city's ids contiguous and
    /// means adding a sixth does not renumber the first five — which matters because a save is a
    /// seed and a command log, and a command names entities by id (§6.1).
    /// <para>
    /// Nothing here decides what a city is good at. That is content, and deliberately so: a
    /// sandbox whose world cannot be reproduced cannot be debugged, and a file is legible
    /// without replaying anything.
    /// </para>
    /// </remarks>
    public static class WorldScenario
    {
        /// <summary>Builds every city the content describes.</summary>
        public static World FromContent(BalanceTables balance)
        {
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            if (balance.Ports.Count == 0)
            {
                throw new InvalidOperationException(
                    "No ports are defined. A world needs at least the one being played.");
            }

            var world = new World();

            for (int i = 0; i < balance.Ports.Count; i++)
            {
                PortDefinition definition = balance.Ports[i];
                Scenario(definition).BuildInto(world, balance, i, definition.IsPlayer);
            }

            return world;
        }

        /// <summary>Turns one row of content into the scenario that builds that city.</summary>
        public static PortScenario Scenario(PortDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            var scenario = new PortScenario
            {
                StartingCoin = definition.StartingCoin,
                StartingCommoners = definition.Commoners,
            };

            foreach (KeyValuePair<string, int> hire in definition.Crew) scenario.Crew.Add(hire);
            foreach (string building in definition.Buildings) scenario.Buildings.Add(building);
            foreach (KeyValuePair<string, float> pile in definition.Stock) scenario.Stock.Add(pile);

            return scenario;
        }

        /// <summary>
        /// How many days a convoy takes between two cities.
        /// </summary>
        /// <remarks>
        /// Derived from the distance in <c>ports.csv</c> rather than written down, so that when
        /// a map exists it reads the same two numbers and cannot disagree with the day count.
        /// §5.1 wants a round trip measured in days — a commitment rather than a toggle — so
        /// this always returns at least one: a convoy that arrives the same day it left is a
        /// transfer, and there would be nothing to intercept.
        /// </remarks>
        public static int TravelDays(PortDefinition from, PortDefinition to,
            float unitsPerDay = UnitsPerDay)
        {
            if (from == null) throw new ArgumentNullException(nameof(from));
            if (to == null) throw new ArgumentNullException(nameof(to));
            if (unitsPerDay <= 0f) throw new ArgumentOutOfRangeException(nameof(unitsPerDay));

            int days = (int)Math.Ceiling(from.DistanceTo(to) / unitsPerDay);
            return days < 1 ? 1 : days;
        }

        /// <summary>
        /// How far a convoy travels in a day, in the units <c>ports.csv</c> uses.
        /// </summary>
        /// <remarks>
        /// Chosen against the shipped map so the nearest pair is two days apart and the furthest
        /// about five. Close enough that a route is worth running, far enough that losing one
        /// convoy is felt and that a raider has somewhere to be.
        /// </remarks>
        public const float UnitsPerDay = 5f;
    }
}
