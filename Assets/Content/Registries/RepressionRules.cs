namespace RTS.Content.Registries
{
    /// <summary>What one level of repression buys, and what it costs (GDD §5.2.2).</summary>
    public sealed class RepressionRules : IHasId
    {
        public RepressionRules(string id, Harshness harshness, float grievanceRelief,
            float baselineIncrease, float loyaltyCost)
        {
            Id = id;
            Harshness = harshness;
            GrievanceRelief = grievanceRelief;
            BaselineIncrease = baselineIncrease;
            LoyaltyCost = loyaltyCost;
        }

        public string Id { get; }

        public Harshness Harshness { get; }

        /// <summary>Taken off every stratum's grievance at once.</summary>
        public float GrievanceRelief { get; }

        /// <summary>
        /// Added to every stratum's floor, permanently. Grievance decays towards the floor, so
        /// this is the part that never goes away.
        /// </summary>
        public float BaselineIncrease { get; }

        /// <summary>Taken from every crew member. They were not asked.</summary>
        public float LoyaltyCost { get; }

        public override string ToString() => Id;
    }
}
