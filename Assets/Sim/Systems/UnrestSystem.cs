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

                // What today gave this stratum to resent, counted in its own people. A stratum
                // is angered by its own hunger and its own unemployment, not by another's:
                // §5.2.2 says three groups each with its own grievance, and reading one shared
                // set of counts is what made them three weightings of the same thing.
                StratumDay day = tally.For(rules.Stratum);

                float added =
                    day.Hungry * rules.HungerWeight +
                    day.Unpaid * rules.UnpaidWeight +
                    day.Deserted * rules.DesertionWeight +
                    day.Idle * rules.IdleWeight;

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
        public static DayTally Tally(World world, EventQueue events)
        {
            var tally = new DayTally();

            if (events != null)
            {
                for (int i = 0; i < events.PendingCount; i++)
                {
                    Envelope envelope = events.Pending[i];

                    if (envelope.TryGet(out FoodShortfall hunger)) tally.CrewHungry += hunger.Crew;
                    else if (envelope.TryGet(out CommonersWentHungry town)) tally.CommonersHungry += town.Commoners;
                    else if (envelope.TryGet(out WagesUnpaid unpaid)) tally.Unpaid += unpaid.Crew;
                    else if (envelope.Is<CrewDeserted>()) tally.Deserted++;
                }
            }

            // Idleness is a standing condition rather than an event: nobody emits "still has no
            // work today", and a stratum that resents unemployment resents it every day.
            ComponentStore<Assignment> assignments = world.Store<Assignment>();
            for (int i = 0; i < assignments.Count; i++)
                if (assignments.Values[i].IsIdle) tally.CrewIdle++;

            tally.CommonersIdle = LabourSystem.UnemployedIn(world);

            return tally;
        }

        /// <summary>The day's counts, kept apart by who they happened to.</summary>
        /// <remarks>
        /// One shared set of counts is what made the three strata into three weightings of the
        /// same crew events, and it is why the Phase 2 gate found that an emptied port reads as
        /// Calm: lose the crew and every stratum lost its grievance at once. Counting each
        /// group's own people is the fix.
        /// </remarks>
        public struct DayTally
        {
            /// <summary>Crew who went unfed today.</summary>
            public int CrewHungry;

            /// <summary>Commoners who went unfed today.</summary>
            public int CommonersHungry;

            /// <summary>Wages that went unpaid. Only crew draw one.</summary>
            public int Unpaid;

            /// <summary>Crew who deserted. Everyone notices people leaving.</summary>
            public int Deserted;

            /// <summary>Crew nobody has assigned to a building.</summary>
            public int CrewIdle;

            /// <summary>Commoners the port has no work for.</summary>
            public int CommonersIdle;

            /// <summary>What today looked like to one stratum in particular.</summary>
            public StratumDay For(Stratum stratum)
            {
                switch (stratum)
                {
                    case Stratum.Commoners:
                        return new StratumDay(CommonersHungry, unpaid: 0, Deserted, CommonersIdle);

                    case Stratum.NamedCrew:
                        return new StratumDay(CrewHungry, Unpaid, Deserted, CrewIdle);

                    // Merchants care about tariffs, blockades and lost convoys (§5.2.2). None of
                    // those exist, so nothing that happens today is any of their business. The
                    // row is present so the stratum keeps its index when they do.
                    default:
                        return default;
                }
            }
        }

        /// <summary>One stratum's share of the day.</summary>
        public readonly struct StratumDay
        {
            public StratumDay(int hungry, int unpaid, int deserted, int idle)
            {
                Hungry = hungry;
                Unpaid = unpaid;
                Deserted = deserted;
                Idle = idle;
            }

            public readonly int Hungry;
            public readonly int Unpaid;
            public readonly int Deserted;
            public readonly int Idle;
        }
    }
}
