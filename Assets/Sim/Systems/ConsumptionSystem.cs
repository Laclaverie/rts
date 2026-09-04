using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Crew eat and drink, once per day (GDD §5.3, §5.4).
    /// </summary>
    /// <remarks>
    /// Food is a need and rum is not. Going without food costs morale; going without rum costs
    /// a smaller amount, and only for roles that expect it. Rum is ImportOnly in Phase 1, so
    /// there is none — which is a real state to observe rather than a gap to paper over.
    /// <para>
    /// Runs first at the day boundary, per §4.2's declared order: what the crew eat today comes
    /// out of yesterday's stock, before today's production is added.
    /// </para>
    /// </remarks>
    public sealed class ConsumptionSystem : ISystem
    {
        public const string SystemId = "Consumption";

        /// <summary>Morale lost by a crew member who went unfed today.</summary>
        public const float HungerMoralePenalty = 0.15f;

        /// <summary>Morale lost by a crew member whose role expects rum and got none.</summary>
        public const float DryMoralePenalty = 0.02f;

        /// <summary>Morale regained by a fed crew member. Slower than it is lost.</summary>
        public const float FedMoraleRecovery = 0.05f;

        public string Id => SystemId;

        public void Run(World world, in Context ctx)
        {
            BalanceTables balance = ctx.Balance;
            if (balance == null) return;

            int foodIndex = IndexOf(balance, "food");
            int rumIndex = IndexOf(balance, "rum");

            ComponentStore<CrewMember> crew = world.Store<CrewMember>();
            if (crew.Count == 0) return;

            float foodWanted = 0f;
            float foodEaten = 0f;
            int hungry = 0;

            for (int i = 0; i < crew.Count; i++)
            {
                EntityId member = crew.Ids[i];
                ref CrewMember state = ref crew.GetRef(member);

                CrewRole role = balance.CrewRoles[state.RoleIndex];

                if (role.FoodPerDay > 0f && foodIndex >= 0)
                {
                    foodWanted += role.FoodPerDay;
                    float eaten = Port.Take(world, foodIndex, role.FoodPerDay);
                    foodEaten += eaten;

                    if (eaten < role.FoodPerDay)
                    {
                        state.Morale = Clamp01(state.Morale - HungerMoralePenalty);
                        hungry++;
                    }
                    else
                    {
                        state.Morale = Clamp01(state.Morale + FedMoraleRecovery);
                    }
                }

                if (role.RumPerDay > 0f && rumIndex >= 0)
                {
                    float drunk = Port.Take(world, rumIndex, role.RumPerDay);
                    if (drunk < role.RumPerDay) state.Morale = Clamp01(state.Morale - DryMoralePenalty);
                }
            }

            if (hungry > 0)
            {
                ctx.Events.Emit(new FoodShortfall
                {
                    Wanted = foodWanted,
                    Eaten = foodEaten,
                    Crew = hungry,
                });
            }
        }

        internal static int IndexOf(BalanceTables balance, string goodId)
        {
            for (int i = 0; i < balance.Goods.Count; i++)
                if (balance.Goods[i].Id == goodId) return i;

            return -1;
        }

        internal static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
