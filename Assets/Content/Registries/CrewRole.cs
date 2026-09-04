namespace RTS.Content.Registries
{
    /// <summary>
    /// What a crew role costs and what it does, per day (GDD §5.4).
    /// </summary>
    /// <remarks>
    /// Morale, loyalty, skill and traits are deliberately absent: those belong to the
    /// individual and live in the world, not in balance data. This table is the part a
    /// designer tunes.
    /// </remarks>
    public sealed class CrewRole : IHasId
    {
        public CrewRole(string id, int wageCoin, float workRate, float foodPerDay, float rumPerDay)
        {
            Id = id;
            WageCoin = wageCoin;
            WorkRate = workRate;
            FoodPerDay = foodPerDay;
            RumPerDay = rumPerDay;
        }

        public string Id { get; }

        /// <summary>Paid at the day boundary. Unpaid wages start the cascade (§5.2.3).</summary>
        public int WageCoin { get; }

        /// <summary>Multiplier on the output of a building this role staffs.</summary>
        public float WorkRate { get; }

        /// <summary>Consumed whether or not the crew member works.</summary>
        public float FoodPerDay { get; }

        /// <summary>A morale good, not a need.</summary>
        public float RumPerDay { get; }

        public override string ToString() => Id;
    }
}
