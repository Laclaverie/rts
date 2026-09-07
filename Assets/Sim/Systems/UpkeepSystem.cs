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

        /// <summary>Condition regained by a maintained building, when the content is silent.</summary>
        /// <remarks>
        /// Superseded by <c>maintenance.csv</c>, and kept only as the fallback for a world whose
        /// content says nothing about repairs. Every fixture written before materials mattered
        /// means this.
        /// </remarks>
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

                // Everything wears, paid for or not. Without it a building at full condition
                // costs no materials and a city with coin wants nothing from anybody — which is
                // the whole reason inter-city trade had no demand to answer. A shut building
                // does not wear, which is most of what shutting one is for.
                state.Condition = ConsumptionSystem.Clamp01(
                    state.Condition - balance.Maintenance.WearPerDay);
                int cost = definition.UpkeepCoin;
                owed += cost;

                ref Treasury treasury = ref Port.Treasury(world, port);

                if (treasury.Coin >= cost)
                {
                    treasury.Coin -= cost;
                    paidCoin += cost;
                    paidCount++;

                    Repair(world, port, ref state, definition, balance, ctx);
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

        /// <summary>
        /// Puts right a day's wear, and takes the materials it costs (GDD §5.5).
        /// </summary>
        /// <remarks>
        /// <strong>Upkeep used to be coin alone.</strong> Two a day and a building repaired
        /// itself out of nothing, so a city with money needed nothing else — which is how a port
        /// came to sit on fourteen hundred coin with its timber stuck at six, wanting no
        /// shipment from anybody. Coin had no sink and materials had no purpose beyond the one
        /// workshop that ate them.
        /// <para>
        /// A repair now draws timber and iron in proportion to what the building cost to build,
        /// from <c>build_timber</c> and <c>build_iron</c> — two more columns that had sat unread
        /// since Phase 1. That is the demand inter-city trade never had: a sawmill matters to
        /// somebody who does not own one.
        /// </para>
        /// <para>
        /// Short of materials, a building repairs by however much it can pay for and no more. It
        /// does <em>not</em> decay faster: being unable to repair is punishment enough, and
        /// stacking decay on top would turn one bad week into a spiral, which §5.2.3 forbids.
        /// </para>
        /// </remarks>
        private static void Repair(World world, EntityId port, ref BuildingState state,
            Building definition, BalanceTables balance, in Context ctx)
        {
            MaintenanceRules rules = balance.Maintenance;

            float wanted = 1f - state.Condition;
            if (wanted <= 0f) return;

            float repair = rules.RepairPerDay < wanted ? rules.RepairPerDay : wanted;
            if (repair <= 0f) return;

            // More to keep up when there is more of it. §5.5 wants upkeep to be something a
            // player is aware of rather than something they fight, and this is the shape that
            // makes sprawl answer for itself: nothing forbids a hundred sheds, they simply cost
            // more timber than a hundred sheds are worth.
            float crowding = rules.Crowding(Standing(world, port));

            float timberWanted = repair * rules.MaterialsPerPoint * definition.BuildTimber * crowding;
            float ironWanted = repair * rules.MaterialsPerPoint * definition.BuildIron * crowding;

            // How much of the repair the sheds can actually pay for. A building made of nothing
            // is free to fix, which is right: a farm is fields and hands.
            float afforded = 1f;
            afforded = Affordable(world, port, balance, "timber", timberWanted, afforded);
            afforded = Affordable(world, port, balance, "iron", ironWanted, afforded);

            if (afforded <= 0f)
            {
                ctx.Events.Emit(new RepairsStalled
                {
                    Port = port, DefinitionIndex = state.DefinitionIndex,
                });
                return;
            }

            Take(world, port, balance, "timber", timberWanted * afforded);
            Take(world, port, balance, "iron", ironWanted * afforded);

            state.Condition = ConsumptionSystem.Clamp01(state.Condition + (repair * afforded));
        }

        /// <summary>How many buildings this port has standing and working.</summary>
        private static int Standing(World world, EntityId port)
        {
            ComponentStore<BuildingState> buildings = world.Store<BuildingState>();
            int count = 0;

            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings.Values[i].Mothballed) continue;
                if (Port.BelongsTo(world, buildings.Ids[i], port)) count++;
            }

            return count;
        }

        private static float Affordable(World world, EntityId port, BalanceTables balance,
            string good, float wanted, float ceiling)
        {
            if (wanted <= 0f) return ceiling;

            int index = ConsumptionSystem.IndexOf(balance, good);
            if (index < 0) return ceiling;

            float held = Port.UnitsOf(world, port, index);
            if (held >= wanted) return ceiling;

            float share = held <= 0f ? 0f : held / wanted;
            return share < ceiling ? share : ceiling;
        }

        private static void Take(World world, EntityId port, BalanceTables balance, string good,
            float units)
        {
            if (units <= 0f) return;

            int index = ConsumptionSystem.IndexOf(balance, good);
            if (index >= 0) Port.Take(world, port, index, units);
        }
    }

    /// <summary>A building could not be repaired for want of materials.</summary>
    /// <remarks>
    /// The first symptom of a route that is not running. The cause is somewhere else - a sawmill
    /// city that stopped shipping, or a raid - so a smaller number in a condition readout would
    /// not explain it.
    /// </remarks>
    public struct RepairsStalled
    {
        public EntityId Port;
        public int DefinitionIndex;
    }
}
