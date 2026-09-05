using System;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Maintains the buildings, or lets them decay (GDD §5.2.3).
    /// </summary>
    /// <remarks>
    /// "Every building carries maintenance ... growth therefore raises your fixed costs
    /// permanently while income stays variable. That asymmetry is the whole failure model."
    /// Upkeep is charged whether or not a building earned anything today.
    /// <para>
    /// A mothballed building costs nothing and produces nothing. That is one of the explicit
    /// exits from the spiral, and deliberate downsizing is meant to be respectable play rather
    /// than losing slowly.
    /// </para>
    /// </remarks>
    public sealed class UpkeepSystem : ISystem
    {
        public const string SystemId = "Upkeep";

        /// <summary>Condition lost by a building whose upkeep went unpaid today.</summary>
        public const float NeglectDecay = 0.10f;

        /// <summary>Condition regained by a maintained building. Slower than it is lost.</summary>
        public const float MaintainedRecovery = 0.02f;

        public string Id => SystemId;

        public void Run(World world, in Context ctx)
        {
            BalanceTables balance = ctx.Balance;
            if (balance == null) return;

            ReadOnlySpan<EntityId> ports = Port.All(world);
            for (int i = 0; i < ports.Length; i++) Maintain(world, ports[i], balance, ctx);
        }

        /// <summary>One port's bills. Each maintains its own buildings from its own coin.</summary>
        private static void Maintain(World world, EntityId port, BalanceTables balance,
            in Context ctx)
        {
            if (!Port.HasTreasury(world, port)) return;

            ComponentStore<BuildingState> buildings = world.Store<BuildingState>();
            if (buildings.Count == 0) return;

            int owed = 0;
            int paidCoin = 0;
            int paidCount = 0;
            int decayed = 0;

            for (int i = 0; i < buildings.Count; i++)
            {
                if (!Port.BelongsTo(world, buildings.Ids[i], port)) continue;

                ref BuildingState state = ref buildings.GetRef(buildings.Ids[i]);
                if (state.Mothballed) continue;

                Building definition = balance.Buildings[state.DefinitionIndex];
                int cost = definition.UpkeepCoin;
                owed += cost;

                ref Treasury treasury = ref Port.Treasury(world, port);

                if (treasury.Coin >= cost)
                {
                    treasury.Coin -= cost;
                    paidCoin += cost;
                    paidCount++;
                    state.Condition = ConsumptionSystem.Clamp01(state.Condition + MaintainedRecovery);
                    continue;
                }

                treasury.Arrears += cost;

                float before = state.Condition;
                state.Condition = ConsumptionSystem.Clamp01(state.Condition - NeglectDecay);
                decayed++;

                if (before > 0f && state.Condition <= 0f)
                {
                    ctx.Events.Emit(new BuildingDerelict
                    {
                        Port = port, DefinitionIndex = state.DefinitionIndex,
                    });
                }
            }

            if (owed == 0) return;

            if (decayed > 0)
            {
                ctx.Events.Emit(new UpkeepUnpaid
                {
                    Port = port, Owed = owed, Paid = paidCoin, Decayed = decayed,
                });
                return;
            }

            if (paidCount > 0)
            {
                ctx.Events.Emit(new UpkeepPaid
                {
                    Port = port, Coin = paidCoin, Buildings = paidCount,
                });
            }
        }
    }
}
