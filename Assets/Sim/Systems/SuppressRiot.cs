using System;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Put down a riot by force (GDD §5.2.2, §6).
    /// </summary>
    /// <remarks>
    /// §6 sketches this as <c>SuppressRiot(EntityId District, Harshness Harshness)</c>. There
    /// are no districts yet, so it takes only the harshness and applies to the whole port.
    /// </remarks>
    public sealed class SuppressRiot : ICommand
    {
        public SuppressRiot(Harshness harshness) => Harshness = harshness;

        public Harshness Harshness { get; }

        public override string ToString() => $"SuppressRiot({Harshness})";
    }

    /// <summary>
    /// Applies repression: quiet now, a worse floor forever, and loyalty from everyone.
    /// </summary>
    /// <remarks>
    /// The three costs are not balanced against each other by accident. §5.2.2 wants repression
    /// to be "a viable strategy, not a free one" — the right answer when the alternative is
    /// deposition tomorrow, and the wrong one when the port could have been fed instead. That
    /// judgement is the player's, and it is only a judgement if both halves are real.
    /// </remarks>
    public sealed class SuppressRiotHandler : ICommandHandler
    {
        /// <summary>The rung at which there is something to suppress.</summary>
        public const LadderRung MinimumRung = LadderRung.Riot;

        public Type CommandType => typeof(SuppressRiot);

        public CommandRejection Validate(ICommand command, World world, in Context ctx)
        {
            var suppress = (SuppressRiot)command;
            BalanceTables balance = ctx.Balance;

            if (balance == null || balance.Repression.Count == 0) return CommandRejection.Unavailable;
            if (!balance.Repression.Contains(suppress.Harshness.ToString())) return CommandRejection.Unavailable;

            ComponentStore<RevolutionLadder> ladders = world.Store<RevolutionLadder>();
            if (ladders.Count == 0) return CommandRejection.InvalidTarget;

            LadderRung rung = ladders.Values[0].Rung;

            // There has to be a riot to put down. Suppressing a grumble is a different act with
            // different costs, and calling it this would let a player buy the permanent penalty
            // for nothing.
            if (rung < MinimumRung) return CommandRejection.NotYet;

            // Deposition is over. There is nobody left to give the order to.
            if (rung == LadderRung.Deposition) return CommandRejection.TargetGone;

            return CommandRejection.None;
        }

        public void Apply(ICommand command, World world, in Context ctx)
        {
            var suppress = (SuppressRiot)command;
            RepressionRules rules = ctx.Balance.Repression[suppress.Harshness.ToString()];

            ComponentStore<Grievance> grievances = world.Store<Grievance>();
            for (int i = 0; i < grievances.Count; i++)
            {
                ref Grievance grievance = ref grievances.GetRef(grievances.Ids[i]);

                // The floor rises first, so relief can never take grievance below the new
                // baseline. A port put down by force does not get to be calmer than one that
                // was never put down at all.
                grievance.Baseline = ConsumptionSystem.Clamp01(grievance.Baseline + rules.BaselineIncrease);

                float relieved = grievance.Value - rules.GrievanceRelief;
                grievance.Value = ConsumptionSystem.Clamp01(
                    relieved < grievance.Baseline ? grievance.Baseline : relieved);

                // The window, not the subtraction, is what force buys. Relief alone is undone
                // by the next day's hunger, because grievance is capped and a rioting port is
                // already at the cap. Longest window wins if a port is crushed twice: the
                // second crackdown does not make people bolder.
                if (rules.CowedDays > grievance.CowedDays) grievance.CowedDays = rules.CowedDays;
            }

            ComponentStore<CrewMember> crew = world.Store<CrewMember>();
            for (int i = 0; i < crew.Count; i++)
            {
                ref CrewMember member = ref crew.GetRef(crew.Ids[i]);
                member.Loyalty = ConsumptionSystem.Clamp01(member.Loyalty - rules.LoyaltyCost);
            }

            ctx.Events.Emit(new RiotSuppressed
            {
                Harshness = suppress.Harshness,
                Relief = rules.GrievanceRelief,
                BaselineAdded = rules.BaselineIncrease,
                Crew = crew.Count,
            });
        }
    }

    /// <summary>A riot was put down. Everything about this is worth remembering.</summary>
    public struct RiotSuppressed
    {
        public Harshness Harshness;
        public float Relief;
        public float BaselineAdded;
        public int Crew;
    }
}
