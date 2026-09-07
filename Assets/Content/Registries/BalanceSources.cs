using RTS.Content.Loading;

using System;
using System.Collections.Generic;
using System.Reflection;

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

        /// <summary>
        /// The cities. Optional: a test exercising one port's economy needs no world around it.
        /// </summary>
        public CsvTable Ports { get; set; }

        /// <summary>
        /// The revolt crowd's settings. Optional: a world without one uses
        /// <see cref="MobRules.Default"/>, which is what every fixture written before rung 5
        /// had a crowd meant.
        /// </summary>
        public CsvTable Mob { get; set; }

        /// <summary>
        /// What wealth draws and what guarding it costs. Optional: a world without it uses
        /// <see cref="HeatRules.Default"/>.
        /// </summary>
        public CsvTable Heat { get; set; }

        /// <summary>
        /// How the other cities trade. Optional: a world without it uses
        /// <see cref="TradeAiRules.Default"/>.
        /// </summary>
        public CsvTable TradeAi { get; set; }

        /// <summary>
        /// What repairs cost. Optional: a world without it uses
        /// <see cref="MaintenanceRules.Default"/>.
        /// </summary>
        public CsvTable Maintenance { get; set; }

        /// <summary>
        /// Every shipped table, read through whatever knows where the files are.
        /// </summary>
        /// <remarks>
        /// <strong>One place that knows the list.</strong> Both composition roots used to build
        /// this by hand, and both drifted: the harness and the Unity boot each stopped at
        /// <c>ports.csv</c> and silently used built-in defaults for mob, heat, trade and
        /// maintenance. The harness was therefore tuning against numbers no file contained —
        /// editing <c>maintenance.csv</c> and re-running the corpus measured nothing at all,
        /// twice — and the game being played was not the game the content described.
        /// <para>
        /// Nothing about that failed loudly, because a missing table is legitimately optional:
        /// a test that only needs goods and buildings passes null for the rest. The defaults are
        /// right for a fixture and wrong for a shipped game, and only the caller knows which it
        /// is. So the guard cannot live inside <see cref="BalanceTables.Load"/> — it is this
        /// method, plus a test that fails by name when a new table is not added to it.
        /// </para>
        /// </remarks>
        /// <param name="read">Turns a file name into a table. Unity and the harness differ here.</param>
        public static BalanceSources From(Func<string, CsvTable> read)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));

            return new BalanceSources
            {
                Goods = read(BalanceTables.GoodsFile),
                Buildings = read(BalanceTables.BuildingsFile),
                CrewRoles = read(BalanceTables.CrewRolesFile),
                Strata = read(BalanceTables.StrataFile),
                Ladder = read(BalanceTables.LadderFile),
                Repression = read(BalanceTables.RepressionFile),
                Ports = read(BalanceTables.PortsFile),
                Mob = read(BalanceTables.MobFile),
                Heat = read(BalanceTables.HeatFile),
                TradeAi = read(BalanceTables.TradeAiFile),
                Maintenance = read(BalanceTables.MaintenanceFile),
            };
        }

        /// <summary>Which tables this set is missing, by property name.</summary>
        /// <remarks>
        /// By reflection on purpose. A hand-written list would be one more thing to forget to
        /// update, which is the exact mistake being guarded against.
        /// </remarks>
        public IEnumerable<string> Missing()
        {
            foreach (PropertyInfo property in typeof(BalanceSources).GetProperties())
            {
                if (property.PropertyType != typeof(CsvTable)) continue;
                if (property.GetValue(this) == null) yield return property.Name;
            }
        }
    }
}
