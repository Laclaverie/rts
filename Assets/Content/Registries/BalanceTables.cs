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
        public const string StrataFile = "strata.csv";
        public const string LadderFile = "ladder.csv";
        public const string RepressionFile = "repression.csv";

        /// <summary>The strata columns, used to stand in an empty table when none is supplied.</summary>
        public const string StrataHeader =
            "id,decay_per_day,relief_per_day,hunger_weight,unpaid_weight,desertion_weight,idle_weight\n";

        /// <summary>The ladder columns, used to stand in an empty table when none is supplied.</summary>
        public const string LadderHeader =
            "rung,climb_at,fall_below,days_to_climb,output_multiplier,condition_damage\n";

        /// <summary>The repression columns, used to stand in an empty table when none is supplied.</summary>
        public const string RepressionHeader =
            "id,grievance_relief,cowed_days,baseline_increase,loyalty_cost\n";

        private BalanceTables(ConfigRegistry<Good> goods, ConfigRegistry<Building> buildings,
            ConfigRegistry<CrewRole> crewRoles, ConfigRegistry<StratumRules> strata,
            ConfigRegistry<LadderStep> ladder, ConfigRegistry<RepressionRules> repression)
        {
            Ladder = ladder;
            Repression = repression;
            Goods = goods;
            Buildings = buildings;
            CrewRoles = crewRoles;
            Strata = strata;
        }

        public ConfigRegistry<Good> Goods { get; }
        public ConfigRegistry<Building> Buildings { get; }
        public ConfigRegistry<CrewRole> CrewRoles { get; }
        public ConfigRegistry<StratumRules> Strata { get; }
        public ConfigRegistry<LadderStep> Ladder { get; }
        public ConfigRegistry<RepressionRules> Repression { get; }

        /// <summary>
        /// Loads and validates every table. Collects problems rather than throwing, so one run
        /// reports everything wrong with the content.
        /// </summary>
        public static BalanceTables Load(
            CsvTable goods, CsvTable buildings, CsvTable crewRoles, ValidationReport report,
            CsvTable strata = null, CsvTable ladder = null, CsvTable repression = null)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));

            var pending = new List<PendingReference>();

            ConfigRegistry<Good> goodRegistry = ConfigRegistry<Good>.Load(goods, report,
                row => new Good(
                    row.Id(),
                    row.Int("base_price", min: 0),
                    row.Float("volatility", 0f, 1f),
                    row.Float("heat_per_unit", 0f, 1f),
                    row.Enum<GoodSupply>("supply"),
                    row.Float("keep", 0f, 100000f),
                    row.Int("sell_price", min: 0)),
                "id", "base_price", "volatility", "heat_per_unit", "supply", "keep", "sell_price");

            ConfigRegistry<Building> buildingRegistry = ConfigRegistry<Building>.Load(buildings, report,
                row => ReadBuilding(row, pending), "id", "upkeep_coin", "build_timber",
                "build_iron", "capacity", "produces", "output_per_day", "staff");

            ConfigRegistry<CrewRole> crewRegistry = ConfigRegistry<CrewRole>.Load(crewRoles, report,
                row => new CrewRole(
                    row.Id(),
                    row.Int("wage_coin", min: 0),
                    row.Float("work_rate", 0f, 10f),
                    row.Float("food_per_day", 0f, 100f),
                    row.Float("rum_per_day", 0f, 100f)),
                "id", "wage_coin", "work_rate", "food_per_day", "rum_per_day");

            // Optional so that tests exercising the economy alone need not carry a strata table.
            // A world with no strata simply has nothing to be aggrieved, which is a coherent
            // state rather than a broken one.
            ConfigRegistry<StratumRules> strataRegistry =
                ConfigRegistry<StratumRules>.Load(
                    strata ?? CsvTable.Parse(StrataHeader, StrataFile),
                    report, ReadStratum,
                    "id", "decay_per_day", "relief_per_day", "hunger_weight", "unpaid_weight",
                    "desertion_weight", "idle_weight");

            ReferenceResolver.Resolve(report, pending, GoodsFile, goodRegistry);

            ConfigRegistry<LadderStep> ladderRegistry =
                ConfigRegistry<LadderStep>.Load(
                    ladder ?? CsvTable.Parse(LadderHeader, LadderFile),
                    report, ReadLadderStep,
                    "rung", "climb_at", "fall_below", "days_to_climb", "output_multiplier",
                    "condition_damage");

            ConfigRegistry<RepressionRules> repressionRegistry =
                ConfigRegistry<RepressionRules>.Load(
                    repression ?? CsvTable.Parse(RepressionHeader, RepressionFile),
                    report, ReadRepression,
                    "id", "grievance_relief", "cowed_days", "baseline_increase", "loyalty_cost");

            var tables = new BalanceTables(goodRegistry, buildingRegistry, crewRegistry,
                strataRegistry, ladderRegistry, repressionRegistry);
            tables.CrossCheck(report);
            return tables;
        }

        private static RepressionRules ReadRepression(RowReader row)
        {
            string id = row.Id();
            Harshness harshness = row.Enum<Harshness>("id");

            return new RepressionRules(id, harshness,
                row.Float("grievance_relief", 0f, 1f),
                row.Int("cowed_days", 0, 365),
                row.Float("baseline_increase", 0f, 1f),
                row.Float("loyalty_cost", 0f, 1f));
        }

        private static LadderStep ReadLadderStep(RowReader row)
        {
            string id = row.Id("rung");
            LadderRung rung = row.Enum<LadderRung>("rung");

            return new LadderStep(id, rung,
                row.Float("climb_at", 0f, 1f),
                row.Float("fall_below", 0f, 1f),
                row.Int("days_to_climb", 1, 365),
                row.Float("output_multiplier", 0f, 1f),
                row.Float("condition_damage", 0f, 1f));
        }

        private static StratumRules ReadStratum(RowReader row)
        {
            string id = row.Id();
            Stratum stratum = row.Enum<Stratum>("id");

            return new StratumRules(id, stratum,
                row.Float("decay_per_day", 0f, 1f),
                row.Float("relief_per_day", 0f, 1f),
                row.Float("hunger_weight", 0f, 1f),
                row.Float("unpaid_weight", 0f, 1f),
                row.Float("desertion_weight", 0f, 1f),
                row.Float("idle_weight", 0f, 1f));
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
                row.Float("output_per_day", 0f, 1000f),
                row.Int("staff", min: 0, max: 100));
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
            if (Ladder.Count > 0) CheckLadder(report);

            foreach (RepressionRules rules in Repression)
            {
                // Repression that raised grievance on the day it was used would be a trap
                // rather than a decision: the player would be punished for taking the option
                // the game offered them, with no way to see it coming.
                if (rules.BaselineIncrease >= rules.GrievanceRelief)
                {
                    report.Add(RepressionFile, Repression.LineOf(rules.Id),
                        $"'{rules.Id}' relieves {rules.GrievanceRelief} but raises the floor by " +
                        $"{rules.BaselineIncrease}, so using it would make things worse the same day.");
                }
            }

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

            foreach (Good good in Goods)
            {
                // True of the current model, not an economic law. With one static merchant and
                // nobody funding the difference, an above-market price is money from nowhere and
                // the bug would read as generous tuning.
                //
                // A rival power funding a buyer to overpay — absorbing the loss out of taxes to
                // capture a market — is a real thing and a good one; it is parked in GDD
                // Appendix A.0. When a price has an actor behind it, this rule is the first
                // thing that changes.
                if (good.SellPrice > good.BasePrice)
                {
                    report.Add(GoodsFile, Goods.LineOf(good.Id),
                        $"'{good.Id}' sells for {good.SellPrice} but its base price is " +
                        $"{good.BasePrice}. A merchant paying above the market is free money.");
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

                if (building.IsProducer && building.Staff <= 0)
                {
                    report.Add(BuildingsFile, Buildings.LineOf(building.Id),
                        $"'{building.Id}' produces '{building.Produces}' but wants no staff, so " +
                        "it would produce at full rate with nobody working it.");
                }

                if (!building.IsProducer && building.Staff > 0)
                {
                    report.Add(BuildingsFile, Buildings.LineOf(building.Id),
                        $"'{building.Id}' wants {building.Staff} staff but produces nothing, so " +
                        "they would have nothing to do.");
                }

                if (!building.IsProducer && building.OutputPerDay > 0f)
                {
                    report.Add(BuildingsFile, Buildings.LineOf(building.Id),
                        $"'{building.Id}' has an output_per_day but no `produces`, so the number " +
                        "does nothing.");
                }
            }
        }

        /// <summary>
        /// The ladder's own rules: every rung present, thresholds ordered, and every rung with
        /// real hysteresis.
        /// </summary>
        /// <remarks>
        /// A missing rung would be skipped silently and a player would never see it. Thresholds
        /// out of order would make a rung unreachable. And a rung whose climb and fall points
        /// are equal flickers every day on the boundary, which makes "pull it back out"
        /// meaningless — the thing the Phase 2 gate exists to prove.
        /// </remarks>
        private void CheckLadder(ValidationReport report)
        {
            foreach (LadderRung rung in (LadderRung[])Enum.GetValues(typeof(LadderRung)))
            {
                if (!Ladder.Contains(rung.ToString()))
                {
                    report.Add(LadderFile, 1,
                        $"rung '{rung}' is missing. Every rung must be present, or it would be " +
                        "skipped without ever being seen.");
                }
            }

            for (int i = 0; i < Ladder.Count; i++)
            {
                LadderStep step = Ladder[i];
                if (step.Rung == LadderRung.Calm || step.Rung == LadderRung.Deposition) continue;

                if (step.FallBelow >= step.ClimbAt)
                {
                    report.Add(LadderFile, Ladder.LineOf(step.Id),
                        $"'{step.Id}' climbs at {step.ClimbAt} and falls below {step.FallBelow}, " +
                        "so it has no hysteresis and would flicker on the boundary.");
                }

                if (i > 0 && Ladder[i - 1].ClimbAt >= step.ClimbAt && Ladder[i - 1].Rung != LadderRung.Calm)
                {
                    report.Add(LadderFile, Ladder.LineOf(step.Id),
                        $"'{step.Id}' climbs at {step.ClimbAt}, no higher than '{Ladder[i - 1].Id}' " +
                        $"at {Ladder[i - 1].ClimbAt}, so one of them is unreachable.");
                }
            }

            CheckLadderPacing(report);
        }

        /// <summary>
        /// Checks the ladder does not get quicker as it gets worse.
        /// </summary>
        /// <remarks>
        /// <c>days_to_climb</c> is what gives a player time to act, and the time needed grows
        /// with the stakes: losing a day at Grumbling costs a grumble, losing one at Uprising
        /// costs the port. A ladder that accelerated towards its own failure state would take
        /// the most time to escalate where it matters least and the least where it matters most,
        /// which inverts the design.
        /// <para>
        /// Whether the top of the ladder is actually escapable is not decided here — it depends
        /// on the decay rates in strata.csv and on the order systems run in, so it is asserted
        /// against the running simulation in the Phase 2 gate rather than guessed at from one
        /// table.
        /// </para>
        /// </remarks>
        private void CheckLadderPacing(ValidationReport report)
        {
            for (int i = 2; i < Ladder.Count; i++)
            {
                LadderStep step = Ladder[i];
                LadderStep below = Ladder[i - 1];
                if (below.Rung == LadderRung.Calm) continue;

                if (step.DaysToClimb < below.DaysToClimb)
                {
                    report.Add(LadderFile, Ladder.LineOf(step.Id),
                        $"'{step.Id}' climbs after {step.DaysToClimb} days but the milder " +
                        $"'{below.Id}' takes {below.DaysToClimb}. The ladder would speed up as it " +
                        "got worse, leaving least time to act where it matters most.");
                }
            }
        }

        public override string ToString() =>
            $"BalanceTables({Goods.Count} goods, {Buildings.Count} buildings, {CrewRoles.Count} roles)";
    }
}
