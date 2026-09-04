namespace RTS.Content.Registries
{
    /// <summary>
    /// What angers one stratum, and how fast it forgets (GDD §5.2.2).
    /// </summary>
    public sealed class StratumRules : IHasId
    {
        public StratumRules(string id, Stratum stratum, float decayPerDay, float hungerWeight,
            float unpaidWeight, float desertionWeight, float idleWeight)
        {
            Id = id;
            Stratum = stratum;
            DecayPerDay = decayPerDay;
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
