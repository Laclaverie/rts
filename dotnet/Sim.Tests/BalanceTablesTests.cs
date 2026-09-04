using System.Linq;
using RTS.Content.Loading;
using RTS.Content.Registries;
using RTS.Content.Validation;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// The cross-table rules of §5.3 — the ones no single file can show are broken.
    /// </summary>
    [Category(TestCategories.Unit)]
    public class BalanceTablesTests
    {
        private const string GoodsHeader =
            "id,base_price,volatility,heat_per_unit,supply,keep,sell_price\n";


        private const string BuildingsHeader =
            "id,upkeep_coin,build_timber,build_iron,capacity,produces,output_per_day,staff\n";

        private const string CrewHeader = "id,wage_coin,work_rate,food_per_day,rum_per_day\n";

        private static BalanceTables Load(string goods, string buildings, string crew,
            out ValidationReport report)
        {
            report = new ValidationReport();
            return BalanceTables.Load(new BalanceSources
            {
                Goods = CsvTable.Parse(GoodsHeader + goods, "goods.csv"),
                Buildings = CsvTable.Parse(BuildingsHeader + buildings, "buildings.csv"),
                CrewRoles = CsvTable.Parse(CrewHeader + crew, "crew_roles.csv"),
            }, report);
        }

        /// <summary>A minimal consistent set: food is produced by a farm and eaten by a laborer.</summary>
        private static BalanceTables Valid(out ValidationReport report) =>
            Load("food,4,0.25,0.00,Local,0,1\n",
                "farm,1,10,0,0,food,6,1\n",
                "laborer,2,1.00,1.0,0.00\n",
                out report);

        [Test]
        public void A_consistent_set_loads_clean()
        {
            BalanceTables tables = Valid(out ValidationReport report);

            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Problems));
            Assert.That(tables.Goods.Count, Is.EqualTo(1));
            Assert.That(tables.Buildings["farm"].Produces, Is.EqualTo("food"));
            Assert.That(tables.CrewRoles["laborer"].WageCoin, Is.EqualTo(2));
        }

        [Test]
        public void A_local_good_nobody_produces_is_reported()
        {
            // The economy would soft-lock the first time it is needed, and no single file
            // looks wrong.
            Load("food,4,0.25,0.00,Local,0,1\niron,12,0.3,0.05,Local,0,1\n",
                "farm,1,10,0,0,food,6,1\n",
                "laborer,2,1.00,1.0,0.00\n",
                out ValidationReport report);

            Assert.That(report.Problems.Any(p => p.Contains("'iron' is Local but no building produces it")),
                Is.True, string.Join("; ", report.Problems));
        }

        [Test]
        public void A_good_nobody_consumes_is_reported()
        {
            // No building costs timber to build, so nothing wants it: it would pile up in a
            // warehouse forever. Note the farm costs nothing here, unlike the fixture above.
            Load("food,4,0.25,0.00,Local,0,1\ntimber,6,0.2,0.00,Local,0,1\n",
                "farm,1,0,0,0,food,6,1\nsawmill,2,0,0,0,timber,5,1\n",
                "laborer,2,1.00,1.0,0.00\n",
                out ValidationReport report);

            Assert.That(report.Problems.Any(p => p.Contains("nothing consumes 'timber'")),
                Is.True, string.Join("; ", report.Problems));
        }

        [Test]
        public void ImportOnly_is_exempt_from_both_rules()
        {
            // §5.3 makes spice a pure trade good: no producer, no consumer. The exemption is
            // declared in the data rather than assumed by the checker.
            Load("food,4,0.25,0.00,Local,0,1\nspice,40,0.5,0.30,ImportOnly,0,1\n",
                "farm,1,10,0,0,food,6,1\n",
                "laborer,2,1.00,1.0,0.00\n",
                out ValidationReport report);

            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Problems));
        }

        [Test]
        public void Construction_cost_counts_as_consumption()
        {
            // Before trade exists, build costs are the only reason to want timber or iron.
            Load("food,4,0.25,0.00,Local,0,1\ntimber,6,0.2,0.00,Local,0,1\n",
                "farm,1,10,0,0,food,6,1\nsawmill,2,0,0,0,timber,5,1\n" +
                "longhouse,2,20,0,8,,0,0\n",
                "laborer,2,1.00,1.0,0.00\n",
                out ValidationReport report);

            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Problems));
        }

        [Test]
        public void A_building_producing_an_unknown_good_is_reported()
        {
            Load("food,4,0.25,0.00,Local,0,1\n",
                "farm,1,10,0,0,food,6,1\nmystery,1,0,0,0,unobtainium,3,1\n",
                "laborer,2,1.00,1.0,0.00\n",
                out ValidationReport report);

            Assert.That(report.Problems.Any(p => p.Contains("'unobtainium'")),
                Is.True, string.Join("; ", report.Problems));
        }

        [Test]
        public void A_producer_with_no_output_is_reported()
        {
            Load("food,4,0.25,0.00,Local,0,1\n",
                "farm,1,10,0,0,food,0,1\n",
                "laborer,2,1.00,1.0,0.00\n",
                out ValidationReport report);

            Assert.That(report.Problems.Any(p => p.Contains("output_per_day")),
                Is.True, string.Join("; ", report.Problems));
        }

        [Test]
        public void An_output_with_no_producer_column_is_reported()
        {
            // The number would silently do nothing, which is the shape of bug that survives
            // for months.
            Load("food,4,0.25,0.00,Local,0,1\n",
                "farm,1,10,0,0,food,6,1\ntavern,2,15,0,0,,4,0\n",
                "laborer,2,1.00,1.0,0.00\n",
                out ValidationReport report);

            Assert.That(report.Problems.Any(p => p.Contains("no `produces`")),
                Is.True, string.Join("; ", report.Problems));
        }

        [Test]
        public void An_unknown_supply_value_is_reported_with_the_valid_ones()
        {
            Load("food,4,0.25,0.00,Domestic,0,1\n",
                "farm,1,10,0,0,food,6,1\n",
                "laborer,2,1.00,1.0,0.00\n",
                out ValidationReport report);

            Assert.That(report.Problems.Any(p => p.Contains("Expected one of")), Is.True);
            Assert.That(report.Problems.Any(p => p.Contains("ImportOnly")), Is.True);
        }

        [Test]
        public void Out_of_range_numbers_are_reported()
        {
            Load("food,4,1.9,0.00,Local,0,1\n",
                "farm,-1,10,0,0,food,6,1\n",
                "laborer,2,1.00,1.0,0.00\n",
                out ValidationReport report);

            Assert.That(report.Count, Is.GreaterThanOrEqualTo(2), string.Join("; ", report.Problems));
        }

        [Test]
        public void Every_problem_is_reported_in_one_pass()
        {
            // A designer who broke four things should learn that in one run.
            Load("food,4,0.25,0.00,Local,0,1\niron,12,0.3,0.05,Local,0,1\ntimber,6,0.2,0,Local,0,1\n",
                "ghost,1,0,0,0,unobtainium,3,1\n",
                "laborer,2,1.00,1.0,0.00\n",
                out ValidationReport report);

            Assert.That(report.Count, Is.GreaterThanOrEqualTo(4), string.Join("; ", report.Problems));
        }
    }
}
