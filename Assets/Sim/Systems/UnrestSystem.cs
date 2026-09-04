using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Events;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Turns the day's events into grievance, per stratum (GDD §5.2.2).
    /// </summary>
    /// <remarks>
    /// It reads what the earlier systems <em>emitted</em> today rather than inspecting the world
    /// for traces of it. Wages already reports how many went unpaid; Consumption already reports
    /// how many went hungry. Re-deriving that from state would mean two sources of the same
    /// truth, and they would eventually disagree.
    /// <para>
    /// This makes the ordering in pipeline.csv load-bearing in a new way: Unrest must run after
    /// the systems whose events it reads and before the queue is drained. §4.2 already required
    /// Wages before Unrest so an unpaid wage feeds grievance the same day; this is that
    /// requirement, made mechanical.
    /// </para>
    /// <para>
    /// A system reading the queue is not a subscriber. §7 forbids <em>subscribers</em> from
    /// mutating the world; a system is where mutation belongs, and the ordering is explicit
    /// rather than implicit.
    /// </para>
    /// </remarks>
    public sealed class UnrestSystem : ISystem
    {
        public const string SystemId = "Unrest";

        public string Id => SystemId;

        public void Run(World world, in Context ctx)
        {
            BalanceTables balance = ctx.Balance;
            if (balance == null || balance.Strata.Count == 0) return;

            ComponentStore<Grievance> grievances = world.Store<Grievance>();
            if (grievances.Count == 0) return;

            DayTally tally = Tally(world, ctx.Events);

            for (int i = 0; i < grievances.Count; i++)
            {
                EntityId entity = grievances.Ids[i];
                ref Grievance grievance = ref grievances.GetRef(entity);

                if (grievance.StratumIndex < 0 || grievance.StratumIndex >= balance.Strata.Count) continue;

                StratumRules rules = balance.Strata[grievance.StratumIndex];

                // What today gave this stratum to resent. Asked per stratum rather than for the
                // port as a whole: named crew do not care that a labourer has no work, and a
                // port-wide test would let one group's complaint deny another its relief. The
                // shipped port has more crew than places to put them, so a port-wide clean day
                // would in fact never happen at all.
                float added =
                    tally.Hungry * rules.HungerWeight +
                    tally.Unpaid * rules.UnpaidWeight +
                    tally.Deserted * rules.DesertionWeight +
                    tally.Idle * rules.IdleWeight;

                // A stratum recently put down by force says nothing about today. The hunger and
                // the unpaid wages are still real and will be resented the moment the window
                // closes — this buys the player time to fix them, not absolution. Silence is not
                // contentment either, so a cowed stratum cools at the slow rate.
                bool cowed = grievance.CowedDays > 0;
                if (cowed)
                {
                    grievance.CowedDays--;
                    added = 0f;
                }

                // Decay towards the baseline rather than towards zero. A port that has been put
                // down by force never returns to calm, which is the cost §5.2.2 says repression
                // carries.
                //
                // A day this stratum had nothing to complain about is worth more than a day that
                // merely was not worse: the port is visibly working, not just no longer bleeding.
                // That gap is what makes fixing the cause a lever the player can pull rather than
                // a slower way of waiting.
                float value = grievance.Value;
                if (value > grievance.Baseline)
                {
                    value -= added <= 0f && !cowed ? rules.ReliefPerDay : rules.DecayPerDay;
                    if (value < grievance.Baseline) value = grievance.Baseline;
                }

                grievance.Value = ConsumptionSystem.Clamp01(value + added);
            }
        }

        /// <summary>What happened today, counted from the events already emitted.</summary>
        internal static DayTally Tally(World world, EventQueue events)
        {
            var tally = new DayTally();

            if (events != null)
            {
                for (int i = 0; i < events.PendingCount; i++)
                {
                    Envelope envelope = events.Pending[i];

                    if (envelope.TryGet(out FoodShortfall hunger)) tally.Hungry += hunger.Crew;
                    else if (envelope.TryGet(out WagesUnpaid unpaid)) tally.Unpaid += unpaid.Crew;
                    else if (envelope.Is<CrewDeserted>()) tally.Deserted++;
                }
            }

            // Idleness is a standing condition rather than an event: nobody emits "still has no
            // work today", and a stratum that resents unemployment resents it every day.
            ComponentStore<Assignment> assignments = world.Store<Assignment>();
            for (int i = 0; i < assignments.Count; i++)
                if (assignments.Values[i].IsIdle) tally.Idle++;

            return tally;
        }

        /// <summary>The day's counts, as the strata weights expect them.</summary>
        internal struct DayTally
        {
            public int Hungry;
            public int Unpaid;
            public int Deserted;
            public int Idle;
        }
    }
}
