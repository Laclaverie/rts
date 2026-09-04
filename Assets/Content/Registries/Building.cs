namespace RTS.Content.Registries
{
    /// <summary>
    /// One constructable building (GDD §5.5, and the extraction sources of §5.3).
    /// </summary>
    /// <remarks>
    /// Upkeep is charged every day boundary whether or not the building earns anything. That
    /// asymmetry — fixed costs, variable income — is the failure model §5.2.3 is built on, so
    /// it is a required column rather than an optional one.
    /// </remarks>
    public sealed class Building : IHasId
    {
        public Building(string id, int upkeepCoin, int buildTimber, int buildIron,
            int capacity, string produces, float outputPerDay)
        {
            Id = id;
            UpkeepCoin = upkeepCoin;
            BuildTimber = buildTimber;
            BuildIron = buildIron;
            Capacity = capacity;
            Produces = produces;
            OutputPerDay = outputPerDay;
        }

        public string Id { get; }

        /// <summary>Charged at every day boundary, useful or not.</summary>
        public int UpkeepCoin { get; }

        public int BuildTimber { get; }

        public int BuildIron { get; }

        /// <summary>Population, storage or routes depending on the building; 0 where unused.</summary>
        public int Capacity { get; }

        /// <summary>Good id, or empty for a building that produces nothing.</summary>
        public string Produces { get; }

        /// <summary>Units of <see cref="Produces"/> per day at full staffing.</summary>
        public float OutputPerDay { get; }

        public bool IsProducer => !string.IsNullOrEmpty(Produces);

        public override string ToString() => Id;
    }
}
