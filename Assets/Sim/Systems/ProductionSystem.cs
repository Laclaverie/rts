using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Buildings produce goods, scaled by their condition and by the crew assigned to them.
    /// </summary>
    /// <remarks>
    /// Output is deliberately not a constant. The cascade of §5.2.3 depends on a feedback loop —
    /// unpaid wages lower morale, lower morale lowers output, lower output lowers income, which
    /// makes wages harder to pay.
    /// <para>
    /// Crew are assigned to specific buildings rather than pooled across the port. The pool was
    /// tried and it inverted the cascade: staffing was total effort over total producers clamped
    /// to 1, so a port with surplus crew sat at the cap, and losing someone made it *richer* —
    /// the corpus measured a two-crew desertion ending 248 coin ahead. With assignment, a lost
    /// worker is lost output at the building they worked, which is what §5.2.3 requires and what
    /// §5.4's named individuals imply.
    /// </para>
    /// <para>
    /// Runs after eating and paying, per §4.2's order: today's output lands after today's costs,
    /// so it is available tomorrow. That one-day lag is what makes reserves matter.
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

            float unrest = UnrestMultiplier(world, balance);

            for (int i = 0; i < buildings.Count; i++)
            {
                EntityId building = buildings.Ids[i];
                BuildingState state = buildings.Values[i];
                if (state.Mothballed) continue;

                Building definition = balance.Buildings[state.DefinitionIndex];
                if (!definition.IsProducer) continue;

                int goodIndex = ConsumptionSystem.IndexOf(balance, definition.Produces);
                if (goodIndex < 0) continue;

                float staffing = StaffingOf(world, balance, building, definition);
                float output = definition.OutputPerDay * state.Condition * staffing * unrest;
                if (output <= 0f) continue;

                Port.Add(world, goodIndex, output);
            }
        }

        /// <summary>
        /// What the ladder is doing to output today. Slowdown is a rung, not a metaphor: at
        /// rung 2 work really is done late and badly, and by Uprising almost nothing gets done.
        /// </summary>
        /// <remarks>
        /// Production runs before Unrest and the ladder in the day (§4.2), so this reads
        /// yesterday's rung. That lag is wanted: a port does not stop working the instant
        /// somebody becomes angry, and the player sees the rung before feeling it.
        /// </remarks>
        public static float UnrestMultiplier(World world, BalanceTables balance)
        {
            if (balance.Ladder.Count == 0) return 1f;

            ComponentStore<RevolutionLadder> ladders = world.Store<RevolutionLadder>();
            if (ladders.Count == 0) return 1f;

            int rung = (int)ladders.Values[0].Rung;
            return rung >= 0 && rung < balance.Ladder.Count ? balance.Ladder[rung].OutputMultiplier : 1f;
        }

        /// <summary>
        /// 0..1 — how well this building's assigned crew cover what it wants. Effort above the
        /// requirement is wasted: two people cannot work a one-person mine twice as hard.
        /// </summary>
        internal static float StaffingOf(World world, BalanceTables balance, EntityId building,
            Building definition)
        {
            if (definition.Staff <= 0) return 0f;

            ComponentStore<Assignment> assignments = world.Store<Assignment>();
            ComponentStore<CrewMember> crew = world.Store<CrewMember>();
            float effort = 0f;

            for (int i = 0; i < assignments.Count; i++)
            {
                if (assignments.Values[i].Building != building) continue;

                EntityId worker = assignments.Ids[i];
                if (!crew.TryGet(worker, out CrewMember member)) continue;

                CrewRole role = balance.CrewRoles[member.RoleIndex];
                float moraleFactor = MinimumMoraleEffort + (1f - MinimumMoraleEffort) * member.Morale;
                effort += role.WorkRate * moraleFactor;
            }

            float ratio = effort / definition.Staff;
            return ratio > 1f ? 1f : ratio;
        }
    }
}
