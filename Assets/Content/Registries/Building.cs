using System.Collections.Generic;

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
            int capacity, string produces, float outputPerDay, int staff,
            IReadOnlyList<KeyValuePair<string, float>> consumes = null)
        {
            Consumes = consumes ?? System.Array.Empty<KeyValuePair<string, float>>();
            Staff = staff;
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

        /// <summary>
        /// Worker-equivalents wanted. A producer at half its staff makes half its output, so
        /// this is what turns a lost crew member into lost income (§5.2.3).
        /// </summary>
        public int Staff { get; }

        /// <summary>
        /// What it eats each day to work, as good and units at full output.
        /// </summary>
        /// <remarks>
        /// A farm consumes nothing and a workshop consumes a great deal, and the difference is
        /// what turns a list of independent cities into an economy. §5.3 says trade only works
        /// because ports differ; a building that needs a good its city cannot make is how that
        /// difference becomes a reason to send a ship somewhere.
        /// <para>
        /// Empty for an extractor. Iron comes out of the ground, and the ground asks for
        /// nothing back.
        /// </para>
        /// </remarks>
        public IReadOnlyList<KeyValuePair<string, float>> Consumes { get; }

        public bool IsProducer => !string.IsNullOrEmpty(Produces);

        /// <summary>Whether it turns goods into other goods rather than making them from nothing.</summary>
        public bool IsTransformer => Consumes.Count > 0;

        public override string ToString() => Id;
    }
}
