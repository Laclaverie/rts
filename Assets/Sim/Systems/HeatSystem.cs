using System;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// What the port's wealth is drawing (GDD §5.2.1).
    /// </summary>
    /// <remarks>
    /// Read off world state, not scripted and not a wave: what a port has in its warehouses and
    /// what it has on the water. §5.2.1's list of what is visible — fat convoys, full warehouses,
    /// a busy dock — is exactly this sum.
    /// <para>
    /// Which goods draw attention is content, and it has been sitting in <c>goods.csv</c> since
    /// Phase 1 waiting for this: <c>heat_per_unit</c> is zero for bread and timber, small for
    /// iron, and largest for spice. Grain nobody would cross the sea for; a hold of spice they
    /// would. That is why the counter §5.2.1 offers is "keep a lower profile and grow slower"
    /// rather than "hold less" — <em>what</em> you trade is the lever, not how much.
    /// </para>
    /// <para>
    /// Runs after the market, so what the passing merchant carried off this morning is not still
    /// drawing attention this evening.
    /// </para>
    /// </remarks>
    public sealed class HeatSystem : ISystem
    {
        public const string SystemId = "Heat";

        public string Id => SystemId;

        public void Run(World world, in Context ctx)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            BalanceTables balance = ctx.Balance;
            if (balance == null) return;

            HeatRules rules = balance.Heat;

            ReadOnlySpan<EntityId> ports = Port.All(world);
            for (int i = 0; i < ports.Length; i++) Draw(world, ports[i], balance, rules, ctx);
        }

        private static void Draw(World world, EntityId port, BalanceTables balance,
            HeatRules rules, in Context ctx)
        {
            EntityId entity = HeatOf(world, port);
            if (entity.IsNone) return;

            float drawn = 0f;

            for (int good = 0; good < balance.Goods.Count; good++)
            {
                float perUnit = balance.Goods[good].HeatPerUnit;
                if (perUnit <= 0f) continue;

                drawn += Port.UnitsOf(world, port, good) * perUnit * rules.HeldWeight;
            }

            // What is on the water counts for more, and counts for whoever owns it rather than
            // whoever it is sailing towards. A convoy is the owner's wealth, in the open.
            ComponentStore<Convoy> convoys = world.Store<Convoy>();
            for (int i = 0; i < convoys.Count; i++)
            {
                if (!Port.BelongsTo(world, convoys.Ids[i], port)) continue;

                Convoy convoy = convoys.Values[i];
                if (convoy.GoodIndex < 0 || convoy.GoodIndex >= balance.Goods.Count) continue;

                drawn += convoy.Units * balance.Goods[convoy.GoodIndex].HeatPerUnit *
                         rules.AtSeaWeight;
            }

            ref Heat heat = ref world.Store<Heat>().GetRef(entity);
            heat.Drawn = drawn;
            heat.DaysSinceRaid++;

            float before = heat.Value;

            // Towards what is on show, never jumping straight to it. Attention is a reputation
            // rather than an inventory: emptying the warehouse this morning does not mean nobody
            // remembers what was in it.
            float target = drawn > 1f ? 1f : drawn;
            heat.Value += (target - heat.Value) * rules.DecayPerDay;

            if (heat.Value < 0f) heat.Value = 0f;
            if (heat.Value > 1f) heat.Value = 1f;

            // Only when the number a player would read actually moved. Emitted every day for
            // every port it was three hundred lines of nothing in a sixty-day event listing,
            // drowning the events somebody ran the harness to look at — and Heat creeps by
            // thousandths on a quiet day, so most of those said nothing at all.
            if (Percent(before) != Percent(heat.Value))
                ctx.Events.Emit(new HeatChanged { Port = port, Value = heat.Value, Drawn = drawn });
        }

        /// <summary>Heat as the player reads it, so an event fires when the readout would change.</summary>
        private static int Percent(float value) => (int)((value * 100f) + 0.5f);

        /// <summary>The entity carrying a city's heat, or None.</summary>
        public static EntityId HeatOf(World world, EntityId port)
        {
            ComponentStore<Heat> heats = world.Store<Heat>();

            for (int i = 0; i < heats.Count; i++)
                if (Port.BelongsTo(world, heats.Ids[i], port)) return heats.Ids[i];

            return EntityId.None;
        }

        /// <summary>How much attention a city is drawing, 0..1.</summary>
        public static float Of(World world, EntityId port)
        {
            EntityId entity = HeatOf(world, port);

            return entity.IsNone ? 0f : world.Store<Heat>().GetRef(entity).Value;
        }
    }

    /// <summary>Attention moved. Emitted every day, narrated only when it matters.</summary>
    public struct HeatChanged
    {
        public EntityId Port;
        public float Value;
        public float Drawn;
    }
}
