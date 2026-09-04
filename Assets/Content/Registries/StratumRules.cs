namespace RTS.Content.Registries
{
    /// <summary>
    /// What angers one stratum, and how fast it forgets (GDD §5.2.2).
    /// </summary>
    public sealed class StratumRules : IHasId
    {
        public StratumRules(string id, Stratum stratum, float decayPerDay, float reliefPerDay,
            float hungerWeight, float unpaidWeight, float desertionWeight, float idleWeight)
        {
            Id = id;
            Stratum = stratum;
            DecayPerDay = decayPerDay;
            ReliefPerDay = reliefPerDay;
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

        /// <summary>Per crew member who went unfed today.</summary>
        public float HungerWeight { get; }

        /// <summary>Per crew member whose wage went unpaid today.</summary>
        public float UnpaidWeight { get; }

        /// <summary>Per crew member who left today.</summary>
        public float DesertionWeight { get; }

        /// <summary>Per crew member with no work, per day.</summary>
        public float IdleWeight { get; }

        /// <summary>Whether anything at all angers this stratum yet.</summary>
        public bool HasAnyDriver =>
            HungerWeight > 0f || UnpaidWeight > 0f || DesertionWeight > 0f || IdleWeight > 0f;

        public override string ToString() => Id;
    }
}
