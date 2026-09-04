namespace RTS.Content.Registries
{
    /// <summary>What one level of repression buys, and what it costs (GDD §5.2.2).</summary>
    public sealed class RepressionRules : IHasId
    {
        public RepressionRules(string id, Harshness harshness, float grievanceRelief,
            int cowedDays, float baselineIncrease, float loyaltyCost)
        {
            Id = id;
            Harshness = harshness;
            GrievanceRelief = grievanceRelief;
            CowedDays = cowedDays;
            BaselineIncrease = baselineIncrease;
            LoyaltyCost = loyaltyCost;
        }

        public string Id { get; }

        public Harshness Harshness { get; }

        /// <summary>Taken off every stratum's grievance at once.</summary>
        public float GrievanceRelief { get; }

        /// <summary>
        /// Days after the crackdown in which the day's pressures do not add grievance.
        /// </summary>
        /// <remarks>
        /// Without this the relief is undone by the next day's hunger and unpaid wages, because
        /// grievance is capped at 1.00 and a port in a spiral is already there — the crackdown
        /// costs a permanent floor and buys a single day. The window is what makes force worth
        /// its price, and what it buys is time to fix the thing that caused the riot.
        /// </remarks>
        public int CowedDays { get; }

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
