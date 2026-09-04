using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Everybody eats and drinks, once per day (GDD §5.3, §5.4).
    /// </summary>
    /// <remarks>
    /// Food is a need and rum is not. Going without food costs morale; going without rum costs
    /// a smaller amount, and only for roles that expect it. Rum is ImportOnly in Phase 1, so
    /// there is none — which is a real state to observe rather than a gap to paper over.
    /// <para>
    /// <strong>Crew eat first, then the commoners.</strong> Not a moral claim: crew are named
    /// individuals whose morale and desertion the rest of the simulation reads, so feeding them
    /// in a fixed order keeps the run deterministic (§7.1), and somebody has to be first. It
    /// does mean a port that is short of food starves its town before its professionals, which
    /// is a consequence worth seeing rather than one to hide — commoner hunger is the grievance
    /// that drives §5.2.2's ladder.
    /// </para>
    /// <para>
    /// Runs first at the day boundary, per §4.2's declared order: what the port eats today comes
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

            FeedCrew(world, balance, foodIndex, rumIndex, ctx);
            FeedCommoners(world, balance, foodIndex, ctx);
        }

        private static void FeedCrew(World world, BalanceTables balance, int foodIndex,
            int rumIndex, in Context ctx)
        {
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

        /// <summary>
        /// Feeds the town, and counts how many went without.
        /// </summary>
        /// <remarks>
        /// Commoners have no morale of their own — they are a count, not entities — so hunger
        /// reaches the simulation two ways instead: as grievance the same day (§5.2.2), and as
        /// a streak that eventually costs the port its population. Losing people is what makes
        /// famine a ratchet rather than a bad mood, because the workers who leave were producing
        /// the food.
        /// </remarks>
        private static void FeedCommoners(World world, BalanceTables balance, int foodIndex,
            in Context ctx)
        {
            if (foodIndex < 0) return;

            ComponentStore<Population> populations = world.Store<Population>();
            if (populations.Count == 0) return;

            StratumRules rules = RulesFor(balance, Stratum.Commoners);
            if (rules == null || rules.FoodPerDay <= 0f) return;

            ref Population population = ref populations.GetRef(populations.Ids[0]);
            if (population.Commoners <= 0) return;

            int hungry = 0;
            float wanted = 0f;
            float eaten = 0f;

            for (int i = 0; i < population.Commoners; i++)
            {
                wanted += rules.FoodPerDay;
                float got = Port.Take(world, foodIndex, rules.FoodPerDay);
                eaten += got;
                if (got < rules.FoodPerDay) hungry++;
            }

            if (hungry > 0)
            {
                population.HungryDays++;
                ctx.Events.Emit(new CommonersWentHungry
                {
                    Commoners = hungry,
                    Wanted = wanted,
                    Eaten = eaten,
                    ConsecutiveDays = population.HungryDays,
                });
            }
            else
            {
                // One good day does not undo a famine, but it does stop the exodus. The streak
                // is what drives people out, so feeding the town buys back the time it bought.
                population.HungryDays = 0;
            }

            LeaveIfStarving(ref population, rules, ctx);
        }

        /// <summary>
        /// Sustained starvation drives commoners out, an order of magnitude slower than crew
        /// desert.
        /// </summary>
        /// <remarks>
        /// The gap is the point. Crew leave within days of a missed payday — they are paid
        /// professionals with somewhere else to be — and when they were the only population, a
        /// mismanaged port emptied before the revolution ladder could climb. With nobody left to
        /// be angry the flagship system reported Calm on a ruin, and Deposition was unreachable
        /// from play (§5.2.2). Commoners live here; leaving means abandoning a home.
        /// <para>
        /// One a day rather than a proportion, so the decline is legible: a player watching the
        /// number can see how long they have.
        /// </para>
        /// </remarks>
        private static void LeaveIfStarving(ref Population population, StratumRules rules,
            in Context ctx)
        {
            if (rules.LeaveAfterDays <= 0) return;
            if (population.HungryDays < rules.LeaveAfterDays) return;
            if (population.Commoners <= 0) return;

            population.Commoners--;

            ctx.Events.Emit(new CommonersLeft
            {
                Left = 1,
                Remaining = population.Commoners,
                HungryDays = population.HungryDays,
            });
        }

        private static StratumRules RulesFor(BalanceTables balance, Stratum stratum)
        {
            for (int i = 0; i < balance.Strata.Count; i++)
                if (balance.Strata[i].Stratum == stratum) return balance.Strata[i];

            return null;
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
