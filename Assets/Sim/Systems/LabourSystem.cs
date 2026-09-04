using System;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Puts commoners to work in the port's buildings (GDD §5.2.2, §5.5).
    /// </summary>
    /// <remarks>
    /// Commoners want "food, work, safety". This is the work: every unmothballed building takes
    /// up to its <c>staff</c> in commoners, and whoever is left over is unemployed and resents
    /// it. Nobody assigns them by hand — they are anonymous, they take whatever work exists, and
    /// the player steers employment by building and mothballing rather than by placing people.
    /// That is the difference between them and named crew, who are placed one at a time by
    /// <see cref="AssignCrew"/>.
    /// <para>
    /// Allocation runs in entity order and fills each building before moving on, which is
    /// arbitrary but fixed. Determinism (§7.1) needs a rule, and "spread them evenly" would be a
    /// worse one: half-staffed buildings all producing at half rate is strictly less output than
    /// full buildings and idle ones, so the even split would quietly cost the player goods.
    /// </para>
    /// <para>
    /// Runs before Production and before Unrest: today's output depends on who is working today,
    /// and today's unemployment is a grievance the same day it happens.
    /// </para>
    /// </remarks>
    public sealed class LabourSystem : ISystem
    {
        public const string SystemId = "Labour";

        public string Id => SystemId;

        public void Run(World world, in Context ctx)
        {
            BalanceTables balance = ctx.Balance;
            if (balance == null) return;

            ReadOnlySpan<EntityId> ports = Port.All(world);
            for (int i = 0; i < ports.Length; i++) Allocate(world, ports[i], balance, ctx);
        }

        /// <summary>One city's labour. Its own people fill its own buildings.</summary>
        private static void Allocate(World world, EntityId port, BalanceTables balance,
            in Context ctx)
        {
            ComponentStore<BuildingState> buildings = world.Store<BuildingState>();
            if (buildings.Count == 0) return;

            int available = CommonersIn(world, port);
            int employed = 0;

            for (int i = 0; i < buildings.Count; i++)
            {
                if (!Port.BelongsTo(world, buildings.Ids[i], port)) continue;

                ref BuildingState state = ref buildings.GetRef(buildings.Ids[i]);
                Building definition = balance.Buildings[state.DefinitionIndex];

                // A shut building employs nobody. That is the cost of mothballing that §5.2.3
                // asks the player to weigh: the upkeep stops, and so do the wages of the people
                // who no longer have anywhere to be.
                int wanted = state.Mothballed ? 0 : definition.Staff;
                int given = wanted < available ? wanted : available;

                state.Workers = given;
                available -= given;
                employed += given;
            }

            ctx.Events.Emit(new LabourAllocated
            {
                Port = port, Employed = employed, Unemployed = available,
            });
        }

        /// <summary>How many commoners a city has, or zero if it has no population.</summary>
        public static int CommonersIn(World world, EntityId port)
        {
            ComponentStore<Population> population = world.Store<Population>();

            for (int i = 0; i < population.Count; i++)
                if (Port.BelongsTo(world, population.Ids[i], port))
                    return population.Values[i].Commoners;

            return 0;
        }

        /// <summary>Commoners with no building to work today, in one city.</summary>
        public static int UnemployedIn(World world, EntityId port)
        {
            ComponentStore<BuildingState> buildings = world.Store<BuildingState>();
            int employed = 0;
            for (int i = 0; i < buildings.Count; i++)
                if (Port.BelongsTo(world, buildings.Ids[i], port))
                    employed += buildings.Values[i].Workers;

            int idle = CommonersIn(world, port) - employed;
            return idle > 0 ? idle : 0;
        }
    }

    /// <summary>Who found work today, and who did not.</summary>
    public struct LabourAllocated
    {
        /// <summary>Which city this happened to. One world holds several (§5.3).</summary>
        public EntityId Port;

        public int Employed;
        public int Unemployed;
    }
}
