using RTS.Content.Loading;

namespace RTS.Content.Registries
{
    /// <summary>
    /// The parsed CSV tables <see cref="BalanceTables.Load"/> reads, named rather than ordered.
    /// </summary>
    /// <remarks>
    /// This exists for one reason: the tables are all the same type. Passed positionally, goods
    /// and buildings could be swapped and the code would compile, load, and produce a port whose
    /// farms cost four coin a day to eat. Nothing downstream would look wrong — the failure would
    /// surface as balance that made no sense, which is the most expensive kind of bug this
    /// project can have, because the numbers are exactly what nobody can check by eye.
    /// <para>
    /// The three unset by default are optional in the same sense they always were: a world with
    /// no strata simply has nothing to be aggrieved, which is a coherent state rather than a
    /// broken one. Tests exercising the economy alone leave them out.
    /// </para>
    /// </remarks>
    public struct BalanceSources
    {
        public CsvTable Goods { get; set; }
        public CsvTable Buildings { get; set; }
        public CsvTable CrewRoles { get; set; }

        /// <summary>Optional. Null loads an empty table from <see cref="BalanceTables.StrataHeader"/>.</summary>
        public CsvTable Strata { get; set; }

        /// <summary>Optional. Null loads an empty table from <see cref="BalanceTables.LadderHeader"/>.</summary>
        public CsvTable Ladder { get; set; }

        /// <summary>Optional. Null loads an empty table from <see cref="BalanceTables.RepressionHeader"/>.</summary>
        public CsvTable Repression { get; set; }
    }
}
