using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Buildings produce goods, scaled by their condition and by who is there to work them.
    /// </summary>
    /// <remarks>
    /// Output is deliberately <em>not</em> a constant. The cascade of §5.2.3 depends on a
    /// feedback loop — unpaid wages lower morale, lower morale lowers output, lower output
    /// lowers income, which makes wages harder to pay. If production ignored the crew, the
    /// spiral would have no teeth and the Phase 1 gate would be measuring nothing.
    /// <para>
    /// Runs last at the day boundary, per §4.2's order: today's output lands after today's
    /// eating and paying, so it is available tomorrow. That one-day lag is what makes reserves
    /// matter rather than being an accounting detail.
    /// </para>
    /// <para>
    /// <strong>The staffing model is provisional.</strong> Crew are not yet assigned to specific
    /// buildings; the port has a pool of effective labour that is spread across everything that
    /// wants working. Job assignment is a later phase, and when it lands this is the system
    /// that changes.
    /// </para>
    /// </remarks>
    public sealed class ProductionSystem : ISystem
    {
        public const string SystemId = "Production";

        /// <summary>
        /// What a crew member at zero morale still contributes. Not zero: people who are fed up
        /// work badly, they do not evaporate. Desertion is how they leave (§5.4).
        /// </summary>
        public const float MinimumMoraleEffort = 0.5f;

        public string Id => SystemId;

        public void Run(World world, in Context ctx)
        {
            BalanceTables balance = ctx.Balance;
            if (balance == null) return;

            ComponentStore<BuildingState> buildings = world.Store<BuildingState>();
            if (buildings.Count == 0) return;

            int producers = CountActiveProducers(world, balance);
            if (producers == 0) return;

            float staffing = Staffing(world, balance, producers);

            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingState state = buildings.Values[i];
                if (state.Mothballed) continue;

                Building definition = balance.Buildings[state.DefinitionIndex];
                if (!definition.IsProducer) continue;

                int goodIndex = ConsumptionSystem.IndexOf(balance, definition.Produces);
                if (goodIndex < 0) continue;

                float output = definition.OutputPerDay * state.Condition * staffing;
                if (output <= 0f) continue;

                Port.Add(world, goodIndex, output);
            }
        }

        private static int CountActiveProducers(World world, BalanceTables balance)
        {
            ComponentStore<BuildingState> buildings = world.Store<BuildingState>();
            int count = 0;

            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingState state = buildings.Values[i];
                if (state.Mothballed) continue;
                if (balance.Buildings[state.DefinitionIndex].IsProducer) count++;
            }

            return count;
        }

        /// <summary>
        /// 0..1 — how well the port's labour covers what wants working. One building takes one
        /// worker-equivalent; a role's contribution is its work rate scaled by morale.
        /// </summary>
        internal static float Staffing(World world, BalanceTables balance, int producers)
        {
            if (producers <= 0) return 0f;

            ComponentStore<CrewMember> crew = world.Store<CrewMember>();
            float effort = 0f;

            for (int i = 0; i < crew.Count; i++)
            {
                CrewMember member = crew.Values[i];
                CrewRole role = balance.CrewRoles[member.RoleIndex];

                float moraleFactor = MinimumMoraleEffort + (1f - MinimumMoraleEffort) * member.Morale;
                effort += role.WorkRate * moraleFactor;
            }

            float ratio = effort / producers;
            return ratio > 1f ? 1f : ratio;
        }
    }
}
