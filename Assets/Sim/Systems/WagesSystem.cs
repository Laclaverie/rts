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
            if (balance == null || !Port.HasTreasury(world)) return;

            ComponentStore<CrewMember> crew = world.Store<CrewMember>();
            if (crew.Count == 0) return;

            int owed = 0;
            int paidCoin = 0;
            int paidCrew = 0;
            int unpaidCrew = 0;

            for (int i = 0; i < crew.Count; i++)
            {
                ref CrewMember state = ref crew.GetRef(crew.Ids[i]);
                int wage = balance.CrewRoles[state.RoleIndex].WageCoin;
                owed += wage;

                ref Treasury treasury = ref Port.Treasury(world);

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

            if (unpaidCrew > 0)
            {
                ctx.Events.Emit(new WagesUnpaid { Owed = owed, Paid = paidCoin, Crew = unpaidCrew });
                return;
            }

            ctx.Events.Emit(new WagesPaid { Coin = paidCoin, Crew = paidCrew });
        }
    }
}
