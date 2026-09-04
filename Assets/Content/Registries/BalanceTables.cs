using System;
using System.Collections.Generic;
using RTS.Content.Loading;
using RTS.Content.Validation;

namespace RTS.Content.Registries
{
    /// <summary>
    /// The loaded balance tables, cross-checked against each other.
    /// </summary>
    /// <remarks>
    /// Loading them together is the point. A good with no producer, or a building producing
    /// something that does not exist, is only visible once more than one file is in hand —
    /// and each file on its own looks perfectly valid (ARCHITECTURE §5.3).
    /// </remarks>
    public sealed class BalanceTables
    {
        public const string GoodsFile = "goods.csv";
        public const string BuildingsFile = "buildings.csv";
        public const string CrewRolesFile = "crew_roles.csv";

        private BalanceTables(ConfigRegistry<Good> goods, ConfigRegistry<Building> buildings,
            ConfigRegistry<CrewRole> crewRoles)
        {
            Goods = goods;
            Buildings = buildings;
            CrewRoles = crewRoles;
        }

        public ConfigRegistry<Good> Goods { get; }
        public ConfigRegistry<Building> Buildings { get; }
        public ConfigRegistry<CrewRole> CrewRoles { get; }

        /// <summary>
        /// Loads and validates every table. Collects problems rather than throwing, so one run
        /// reports everything wrong with the content.
        /// </summary>
        public static BalanceTables Load(
            CsvTable goods, CsvTable buildings, CsvTable crewRoles, ValidationReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));

            var pending = new List<PendingReference>();

            ConfigRegistry<Good> goodRegistry = ConfigRegistry<Good>.Load(goods, report,
                row => new Good(
                    row.Id(),
                    row.Int("base_price", min: 0),
                    row.Float("volatility", 0f, 1f),
                    row.Float("heat_per_unit", 0f, 1f),
                    row.Enum<GoodSupply>("supply")),
                "id", "base_price", "volatility", "heat_per_unit", "supply");

            ConfigRegistry<Building> buildingRegistry = ConfigRegistry<Building>.Load(buildings, report,
                row => ReadBuilding(row, pending), "id", "upkeep_coin", "build_timber",
                "build_iron", "capacity", "produces", "output_per_day");

            ConfigRegistry<CrewRole> crewRegistry = ConfigRegistry<CrewRole>.Load(crewRoles, report,
                row => new CrewRole(
                    row.Id(),
                    row.Int("wage_coin", min: 0),
                    row.Float("work_rate", 0f, 10f),
                    row.Float("food_per_day", 0f, 100f),
                    row.Float("rum_per_day", 0f, 100f)),
                "id", "wage_coin", "work_rate", "food_per_day", "rum_per_day");

            ReferenceResolver.Resolve(report, pending, GoodsFile, goodRegistry);

            var tables = new BalanceTables(goodRegistry, buildingRegistry, crewRegistry);
            tables.CrossCheck(report);
            return tables;
        }

        private static Building ReadBuilding(RowReader row, ICollection<PendingReference> pending)
        {
            string produces = row.Text("produces");

            // Empty is legitimate — most buildings produce nothing — so it is only a reference
            // when it is filled in.
            if (!string.IsNullOrEmpty(produces))
                pending.Add(new PendingReference("buildings.csv", row.Line, "produces", produces, GoodsFile));

            return new Building(
                row.Id(),
                row.Int("upkeep_coin", min: 0),
                row.Int("build_timber", min: 0),
                row.Int("build_iron", min: 0),
                row.Int("capacity", min: 0),
                produces,
                row.Float("output_per_day", 0f, 1000f));
        }

        /// <summary>
        /// The rules that need more than one table (§5.3): every local good is produced by
        /// something, and everything is consumed by something.
        /// </summary>
        /// <remarks>
        /// A good nobody produces is an economy that soft-locks the first time it is needed. A
        /// good nobody consumes is dead weight the player accumulates for no reason. Neither is
        /// visible in a single file, and neither produces an error at runtime — the sim just
        /// quietly never works.
        /// <para>
        /// <see cref="GoodSupply.ImportOnly"/> is exempt from both, because §5.3 defines spice
        /// as a pure trade good and puts rum's only source behind the post-MVP Workshop. The
        /// exemption is declared in data rather than assumed by the checker.
        /// </para>
        /// </remarks>
        private void CrossCheck(ValidationReport report)
        {
            var produced = new HashSet<string>(StringComparer.Ordinal);
            var consumed = new HashSet<string>(StringComparer.Ordinal);

            foreach (Building building in Buildings)
            {
                if (building.IsProducer) produced.Add(building.Produces);

                // Construction costs are a form of consumption: they are why timber and iron
                // are wanted at all before trade exists.
                if (building.BuildTimber > 0) consumed.Add("timber");
                if (building.BuildIron > 0) consumed.Add("iron");
            }

            foreach (CrewRole role in CrewRoles)
            {
                if (role.FoodPerDay > 0f) consumed.Add("food");
                if (role.RumPerDay > 0f) consumed.Add("rum");
            }

            foreach (Good good in Goods)
            {
                if (good.Supply == GoodSupply.ImportOnly) continue;

                if (!produced.Contains(good.Id))
                {
                    report.Add(GoodsFile, Goods.LineOf(good.Id),
                        $"'{good.Id}' is Local but no building produces it. Add a producer, or " +
                        "mark it ImportOnly.");
                }

                if (!consumed.Contains(good.Id))
                {
                    report.Add(GoodsFile, Goods.LineOf(good.Id),
                        $"nothing consumes '{good.Id}'. A good nobody wants is dead weight; " +
                        "give it a consumer, or mark it ImportOnly if it exists only to trade.");
                }
            }

            foreach (Building building in Buildings)
            {
                if (building.IsProducer && building.OutputPerDay <= 0f)
                {
                    report.Add(BuildingsFile, Buildings.LineOf(building.Id),
                        $"'{building.Id}' produces '{building.Produces}' but its output_per_day " +
                        "is 0, so it produces nothing. Set an output, or clear `produces`.");
                }

                if (!building.IsProducer && building.OutputPerDay > 0f)
                {
                    report.Add(BuildingsFile, Buildings.LineOf(building.Id),
                        $"'{building.Id}' has an output_per_day but no `produces`, so the number " +
                        "does nothing.");
                }
            }
        }

        public override string ToString() =>
            $"BalanceTables({Goods.Count} goods, {Buildings.Count} buildings, {CrewRoles.Count} roles)";
    }
}
