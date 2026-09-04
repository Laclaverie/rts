namespace RTS.Content.Registries
{
    /// <summary>
    /// What angers one stratum, and how fast it forgets (GDD §5.2.2).
    /// </summary>
    public sealed class StratumRules : IHasId
    {
        public StratumRules(string id, Stratum stratum, float decayPerDay, float reliefPerDay,
            float foodPerDay, int leaveAfterDays, float hungerWeight, float unpaidWeight,
            float desertionWeight, float idleWeight)
        {
            Id = id;
            Stratum = stratum;
            DecayPerDay = decayPerDay;
            ReliefPerDay = reliefPerDay;
            FoodPerDay = foodPerDay;
            LeaveAfterDays = leaveAfterDays;
            HungerWeight = hungerWeight;
            UnpaidWeight = unpaidWeight;
            DesertionWeight = desertionWeight;
            IdleWeight = idleWeight;
        }

        public string Id { get; }

        public Stratum Stratum { get; }

        /// <summary>
        /// How fast anger fades when nothing new happens. Slower than it rises, or nothing
        /// compounds and the ladder can only ever be climbed by a single bad day.
        /// </summary>
        public float DecayPerDay { get; }

        /// <summary>
        /// How fast anger fades on a day when nothing at all went wrong: nobody hungry, nobody
        /// unpaid, nobody idle, nobody gone.
        /// </summary>
        /// <remarks>
        /// Larger than <see cref="DecayPerDay"/>, and the reason the Phase 2 gate has a second
        /// direction at all. Grievance is capped at 1.00, so a port in a spiral sits at the cap;
        /// with decay alone it needs twenty-five clear days to reach zero, which no amount of
        /// player action can make faster and which the ladder's dwell times outrun. Repression
        /// then becomes the only exit, and §5.2.2's "viable strategy, not a free one" turns into
        /// the only strategy.
        /// <para>
        /// The distinction is between a port that has stopped getting worse and one that is
        /// visibly working. Merely halting the bleeding earns the slow rate; a genuinely clean
        /// day — the port fed, paid and employed — earns this one. That is what makes fixing the
        /// cause a real lever rather than a slower way of waiting.
        /// </para>
        /// </remarks>
        public float ReliefPerDay { get; }

        /// <summary>
        /// Food eaten per head of this stratum's own population, per day. Zero for a stratum
        /// with no population of its own.
        /// </summary>
        /// <remarks>
        /// Only commoners have a population here. Named crew eat as individuals through
        /// <c>crew_roles.csv</c>, because they are entities rather than a count; merchants are
        /// not modelled as a population at all yet, since nothing they care about — tariffs,
        /// blockades, lost convoys (§5.2.2) — exists. A zero means "this stratum has no mouths
        /// of its own to feed", not "these people do not eat".
        /// </remarks>
        public float FoodPerDay { get; }

        /// <summary>
        /// Consecutive hungry days before this stratum's population starts to leave. Zero means
        /// it never leaves this way.
        /// </summary>
        /// <remarks>
        /// Commoners leave an order of magnitude slower than crew desert, and that gap is
        /// deliberate. Crew go within days of a missed payday — they are paid professionals with
        /// somewhere else to be (§5.4). Commoners live here; leaving means abandoning a home, so
        /// it takes sustained starvation. If they left as readily as crew, a collapsing port
        /// would empty before the revolution ladder could climb, which is exactly the failure
        /// the Phase 2 gate found.
        /// </remarks>
        public int LeaveAfterDays { get; }

        /// <summary>Per head of this stratum who went unfed today.</summary>
        public float HungerWeight { get; }

        /// <summary>Per wage that went unpaid today. Only crew draw one.</summary>
        public float UnpaidWeight { get; }

        /// <summary>Per crew member who deserted today. Everyone notices people leaving.</summary>
        public float DesertionWeight { get; }

        /// <summary>
        /// Per head of this stratum with no work, per day. Commoners count their own unemployed;
        /// named crew count crew nobody has assigned to a building.
        /// </summary>
        public float IdleWeight { get; }

        /// <summary>Whether anything at all angers this stratum yet.</summary>
        public bool HasAnyDriver =>
            HungerWeight > 0f || UnpaidWeight > 0f || DesertionWeight > 0f || IdleWeight > 0f;

        public override string ToString() => Id;
    }
}
