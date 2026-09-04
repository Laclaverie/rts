using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Buildings produce goods, scaled by condition, by the commoners working them, and by the
    /// named crew assigned to improve them.
    /// </summary>
    /// <remarks>
    /// Output is deliberately not a constant. The cascade of §5.2.3 depends on a feedback loop:
    /// a port that cannot feed its people loses them, and a port with fewer people produces
    /// less, which makes feeding the rest harder still.
    /// <para>
    /// <strong>Labour is commoners; crew are a multiplier on it.</strong> A building's base rate
    /// is how much of its <c>staff</c> the population fills, and a building nobody works produces
    /// nothing however many specialists stand in it — an overseer without hands is not a farm.
    /// Named crew assigned to a building raise what those hands achieve, which is what §5.4's
    /// skilled individuals are for and what keeps <see cref="AssignCrew"/> a real decision
    /// rather than a way of manning things.
    /// </para>
    /// <para>
    /// Crew were the labour before commoners existed, and pooled across the port before that.
    /// The pool inverted the cascade — staffing was total effort over total producers clamped to
    /// 1, so a port with surplus crew sat at the cap and losing someone made it <em>richer</em>,
    /// measured at 248 coin ahead over a corpus run. Assignment fixed that; commoners fix the
    /// other half, which was that a port could lose its entire workforce and its grievance with
    /// it (§5.2.2).
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

        /// <summary>
        /// The most a building's own specialists can add, as a fraction of its base output.
        /// </summary>
        /// <remarks>
        /// Capped so that stacking crew onto one farm cannot replace having a second farm.
        /// Without a ceiling the cheapest strategy would be to pile every specialist into a
        /// single building, which is neither interesting nor what §5.5 wants buildings to be.
        /// </remarks>
        public const float MaximumSpecialistBonus = 0.25f;

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

                float staffing = StaffingOf(state, definition);
                if (staffing <= 0f) continue;

                float bonus = SpecialistBonusOf(world, balance, building, definition);
                float output = definition.OutputPerDay * state.Condition * staffing
                               * (1f + bonus) * unrest;
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
        /// 0..1 — how much of what this building wants the population actually fills.
        /// </summary>
        /// <remarks>
        /// Hands above the requirement are wasted: two people cannot work a one-person mine
        /// twice as hard. <c>Labour</c> already refuses to over-fill a building, so the clamp is
        /// belt and braces rather than a live path.
        /// </remarks>
        public static float StaffingOf(BuildingState state, Building definition)
        {
            if (definition.Staff <= 0) return 0f;

            float ratio = (float)state.Workers / definition.Staff;
            return ratio > 1f ? 1f : ratio;
        }

        /// <summary>
        /// What the named crew assigned here add, as a fraction of the building's base output.
        /// </summary>
        /// <remarks>
        /// A specialist improves work that is already happening rather than replacing it, so
        /// this multiplies rather than adds — and a building with nobody working it never gets
        /// here, because a bonus on nothing is nothing.
        /// <para>
        /// Morale still reaches output through this term, which is the §5.2.3 link between
        /// unpaid wages and income. It is weaker than it was when crew were the labour, and
        /// deliberately so: the strong link now runs through the population, where hunger costs
        /// the port workers outright rather than making the ones it has slightly worse.
        /// </para>
        /// </remarks>
        public static float SpecialistBonusOf(World world, BalanceTables balance,
            EntityId building, Building definition)
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

            float bonus = effort / definition.Staff * MaximumSpecialistBonus;
            return bonus > MaximumSpecialistBonus ? MaximumSpecialistBonus : bonus;
        }
    }
}
