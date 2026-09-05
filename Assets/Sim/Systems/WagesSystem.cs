using System;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Pays the crew, or fails to (GDD §5.2.3).
    /// </summary>
    /// <remarks>
    /// This is the first link in the cascade: reserves exhausted, wages unpaid, morale and
    /// loyalty fall, desertion and unrest follow. It runs before Upkeep because people are paid
    /// before buildings are maintained — and because §4.2 requires Wages to precede Unrest so
    /// that an unpaid wage feeds grievance the same day rather than the next.
    /// <para>
    /// Wages are paid in full or not at all, per crew member, in world order. Paying everyone a
    /// fraction would be a different game: partial pay is a negotiation, and this port does not
    /// negotiate yet.
    /// </para>
    /// </remarks>
    public sealed class WagesSystem : ISystem
    {
        public const string SystemId = "Wages";

        /// <summary>Morale lost by a crew member who was not paid today.</summary>
        public const float UnpaidMoralePenalty = 0.10f;

        /// <summary>Loyalty lost by a crew member who was not paid today.</summary>
        public const float UnpaidLoyaltyPenalty = 0.12f;

        public string Id => SystemId;

        public void Run(World world, in Context ctx)
        {
            BalanceTables balance = ctx.Balance;
            if (balance == null) return;

            ReadOnlySpan<EntityId> ports = Port.All(world);
            for (int i = 0; i < ports.Length; i++) Pay(world, ports[i], balance, ctx);
        }

        /// <summary>
        /// One port's payday. Each pays its own crew out of its own treasury.
        /// </summary>
        /// <remarks>
        /// Scoped per port rather than run once over every crew member in the world, which
        /// would have Ironhold's coin paying Saltmarsh's sailors. That is not a subtle bug: the
        /// whole of §5.2.3's cascade runs on a port being unable to meet its own bills.
        /// </remarks>
        private static void Pay(World world, EntityId port, BalanceTables balance, in Context ctx)
        {
            if (!Port.HasTreasury(world, port)) return;

            ComponentStore<CrewMember> crew = world.Store<CrewMember>();
            if (crew.Count == 0) return;

            int owed = 0;
            int paidCoin = 0;
            int paidCrew = 0;
            int unpaidCrew = 0;

            for (int i = 0; i < crew.Count; i++)
            {
                if (!Port.BelongsTo(world, crew.Ids[i], port)) continue;

                ref CrewMember state = ref crew.GetRef(crew.Ids[i]);
                int wage = balance.CrewRoles[state.RoleIndex].WageCoin;
                owed += wage;

                ref Treasury treasury = ref Port.Treasury(world, port);

                if (treasury.Coin >= wage)
                {
                    treasury.Coin -= wage;
                    paidCoin += wage;
                    paidCrew++;
                    continue;
                }

                treasury.Arrears += wage;
                state.Morale = ConsumptionSystem.Clamp01(state.Morale - UnpaidMoralePenalty);
                state.Loyalty = ConsumptionSystem.Clamp01(state.Loyalty - UnpaidLoyaltyPenalty);
                unpaidCrew++;
            }

            if (owed == 0) return;

            if (unpaidCrew > 0)
            {
                ctx.Events.Emit(new WagesUnpaid
                {
                    Port = port, Owed = owed, Paid = paidCoin, Crew = unpaidCrew,
                });
                return;
            }

            ctx.Events.Emit(new WagesPaid { Port = port, Coin = paidCoin, Crew = paidCrew });
        }
    }
}
