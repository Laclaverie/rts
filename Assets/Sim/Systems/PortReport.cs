using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// One day's numbers, and the table they print into.
    /// </summary>
    /// <remarks>
    /// A report, never a decision: it reads the world and returns text. Nothing here may mutate
    /// anything, because the console being open must not change the simulation any more than
    /// logging does.
    /// <para>
    /// It lives in <c>Sim</c> so the console you tune against and any later UI read the same
    /// numbers from the same place, rather than each computing "average morale" slightly
    /// differently.
    /// </para>
    /// </remarks>
    public readonly struct PortReport
    {
        public PortReport(int day, int coin, int arrears, float averageMorale, float averageLoyalty,
            float averageCondition, int crew, int buildings, IReadOnlyList<float> stock,
            LadderRung rung, IReadOnlyList<float> grievance)
        {
            Rung = rung;
            Grievance = grievance;
            Day = day;
            Coin = coin;
            Arrears = arrears;
            AverageMorale = averageMorale;
            AverageLoyalty = averageLoyalty;
            AverageCondition = averageCondition;
            Crew = crew;
            Buildings = buildings;
            Stock = stock;
        }

        public readonly int Day;
        public readonly int Coin;
        public readonly int Arrears;
        public readonly float AverageMorale;
        public readonly float AverageLoyalty;
        public readonly float AverageCondition;
        public readonly int Crew;
        public readonly int Buildings;

        /// <summary>Units per good, indexed as the goods registry is.</summary>
        public readonly IReadOnlyList<float> Stock;

        /// <summary>Where the port sits on the revolution ladder (§5.2.2).</summary>
        public readonly LadderRung Rung;

        /// <summary>Grievance per stratum, indexed as the strata registry is.</summary>
        public readonly IReadOnlyList<float> Grievance;

        public static PortReport Of(World world, BalanceTables balance, int day)
        {
            ComponentStore<Treasury> treasuries = world.Store<Treasury>();
            int coin = treasuries.Count > 0 ? treasuries.Values[0].Coin : 0;
            int arrears = treasuries.Count > 0 ? treasuries.Values[0].Arrears : 0;

            ComponentStore<CrewMember> crew = world.Store<CrewMember>();
            float morale = 0f;
            float loyalty = 0f;
            for (int i = 0; i < crew.Count; i++)
            {
                morale += crew.Values[i].Morale;
                loyalty += crew.Values[i].Loyalty;
            }

            ComponentStore<BuildingState> buildings = world.Store<BuildingState>();
            float condition = 0f;
            for (int i = 0; i < buildings.Count; i++) condition += buildings.Values[i].Condition;

            var stock = new float[balance.Goods.Count];
            for (int i = 0; i < stock.Length; i++) stock[i] = Port.UnitsOf(world, i);

            ComponentStore<RevolutionLadder> ladders = world.Store<RevolutionLadder>();
            LadderRung rung = ladders.Count > 0 ? ladders.Values[0].Rung : LadderRung.Calm;

            var grievance = new float[balance.Strata.Count];
            ComponentStore<Grievance> grievances = world.Store<Grievance>();
            for (int i = 0; i < grievances.Count; i++)
            {
                Grievance entry = grievances.Values[i];
                if (entry.StratumIndex >= 0 && entry.StratumIndex < grievance.Length)
                    grievance[entry.StratumIndex] = entry.Value;
            }

            return new PortReport(
                day, coin, arrears,
                crew.Count > 0 ? morale / crew.Count : 0f,
                crew.Count > 0 ? loyalty / crew.Count : 0f,
                buildings.Count > 0 ? condition / buildings.Count : 0f,
                crew.Count, buildings.Count, stock, rung, grievance);
        }

        /// <summary>The header matching <see cref="ToRow"/>. Fixed width, so columns line up.</summary>
        public static string Header(BalanceTables balance)
        {
            var text = new StringBuilder();
            text.Append(" day |   coin |  arr | morale | loyal |  cond ");

            for (int i = 0; i < balance.Goods.Count; i++)
                text.Append('|').Append(Pad(balance.Goods[i].Id, 8));

            for (int i = 0; i < balance.Strata.Count; i++)
                text.Append('|').Append(Pad(balance.Strata[i].Id, 6));

            text.Append("| rung");
            return text.ToString();
        }

        public static string Separator(BalanceTables balance) =>
            new string('-', Header(balance).Length);

        public string ToRow()
        {
            var text = new StringBuilder();

            text.Append(Day.ToString(CultureInfo.InvariantCulture).PadLeft(4)).Append(" |")
                .Append(Coin.ToString(CultureInfo.InvariantCulture).PadLeft(7)).Append(" |")
                .Append(Arrears.ToString(CultureInfo.InvariantCulture).PadLeft(5)).Append(" |")
                .Append(AverageMorale.ToString("0.00", CultureInfo.InvariantCulture).PadLeft(7)).Append(" |")
                .Append(AverageLoyalty.ToString("0.00", CultureInfo.InvariantCulture).PadLeft(6)).Append(" |")
                .Append(AverageCondition.ToString("0.00", CultureInfo.InvariantCulture).PadLeft(6)).Append(' ');

            for (int i = 0; i < Stock.Count; i++)
            {
                text.Append('|')
                    .Append(Stock[i].ToString("0.0", CultureInfo.InvariantCulture).PadLeft(7))
                    .Append(' ');
            }

            for (int i = 0; i < Grievance.Count; i++)
            {
                text.Append('|')
                    .Append(Grievance[i].ToString("0.00", CultureInfo.InvariantCulture).PadLeft(5))
                    .Append(' ');
            }

            text.Append("| ").Append(Rung);
            return text.ToString();
        }

        private static string Pad(string value, int width) =>
            value.Length >= width ? value.Substring(0, width) : value.PadLeft(width - 1) + " ";
    }
}
