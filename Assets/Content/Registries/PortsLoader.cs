using System;
using System.Collections.Generic;
using System.Globalization;
using RTS.Content.Loading;
using RTS.Content.Validation;

namespace RTS.Content.Registries
{
    /// <summary>
    /// Reads <c>ports.csv</c> into <see cref="PortDefinition"/>s.
    /// </summary>
    /// <remarks>
    /// The list columns — crew, buildings, stock — are semicolon-separated because a port is
    /// naturally a row and splitting it across several files would mean reading four places to
    /// answer "what is Ironhold". The cost is a parser here rather than a schema, which is the
    /// right trade for a table a designer edits by hand.
    /// </remarks>
    public static class PortsLoader
    {
        public const string IdColumn = "id";
        public const string NameColumn = "name";
        public const string XColumn = "x";
        public const string YColumn = "y";
        public const string PlayerColumn = "player";
        public const string CoinColumn = "coin";
        public const string CommonersColumn = "commoners";
        public const string CrewColumn = "crew";
        public const string BuildingsColumn = "buildings";
        public const string StockColumn = "stock";

        public static readonly string[] Columns =
        {
            IdColumn, NameColumn, XColumn, YColumn, PlayerColumn, CoinColumn,
            CommonersColumn, CrewColumn, BuildingsColumn, StockColumn,
        };

        /// <summary>The columns, as a header row, for standing in an empty table.</summary>
        public const string Header = "id,name,x,y,player,coin,commoners,crew,buildings,stock\n";

        public static PortDefinition Read(RowReader row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));

            return new PortDefinition(
                id: row.Id(),
                name: row.Text(NameColumn, required: true),
                x: row.Float(XColumn, -100000f, 100000f),
                y: row.Float(YColumn, -100000f, 100000f),
                isPlayer: row.Bool(PlayerColumn),
                startingCoin: row.Int(CoinColumn, min: 0),
                commoners: row.Int(CommonersColumn, 0, 100000),
                crew: Counted(row, CrewColumn),
                buildings: Names(row, BuildingsColumn),
                stock: Amounts(row, StockColumn));
        }

        /// <summary>
        /// Checks the things a single row cannot know.
        /// </summary>
        /// <remarks>
        /// Cross-file references — that every building and crew role a port names exists — are
        /// resolved by <see cref="BalanceTables"/>, which is the only place holding all the
        /// tables at once (§5.3). What is left here is about the set of ports as a whole.
        /// </remarks>
        public static void CrossCheck(ConfigRegistry<PortDefinition> ports, ValidationReport report)
        {
            if (ports == null || ports.Count == 0) return;

            int players = 0;
            for (int i = 0; i < ports.Count; i++)
                if (ports[i].IsPlayer) players++;

            if (players != 1)
            {
                report.Add(BalanceTables.PortsFile, 1,
                    players == 0
                        ? "no port is marked player. Somebody has to be the one being played."
                        : $"{players} ports are marked player. Exactly one may be.");
            }

            // Two cities in the same place would have a route of zero length between them,
            // which is not a route. It is also almost certainly a copied row.
            for (int i = 0; i < ports.Count; i++)
            {
                for (int j = i + 1; j < ports.Count; j++)
                {
                    if (ports[i].DistanceTo(ports[j]) > 0.001f) continue;

                    report.Add(BalanceTables.PortsFile, ports.LineOf(ports[j].Id),
                        $"'{ports[j].Id}' sits on top of '{ports[i].Id}'. A route between them " +
                        "would have no length.");
                }
            }
        }

        private static IReadOnlyList<string> Names(RowReader row, string column)
        {
            string text = row.Text(column);
            var found = new List<string>();

            if (string.IsNullOrWhiteSpace(text)) return found;

            foreach (string part in text.Split(';'))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0) found.Add(trimmed);
            }

            return found;
        }

        private static IReadOnlyList<KeyValuePair<string, int>> Counted(RowReader row, string column)
        {
            var found = new List<KeyValuePair<string, int>>();

            foreach (string entry in Names(row, column))
            {
                string[] halves = entry.Split(':');
                if (halves.Length != 2 ||
                    !int.TryParse(halves[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out int count) ||
                    count < 0)
                {
                    row.Problem($"'{entry}' in {column} is not 'id:count'.");
                    continue;
                }

                found.Add(new KeyValuePair<string, int>(halves[0].Trim(), count));
            }

            return found;
        }

        private static IReadOnlyList<KeyValuePair<string, float>> Amounts(RowReader row, string column)
        {
            var found = new List<KeyValuePair<string, float>>();

            foreach (string entry in Names(row, column))
            {
                string[] halves = entry.Split(':');
                if (halves.Length != 2 ||
                    !float.TryParse(halves[1], NumberStyles.Float, CultureInfo.InvariantCulture,
                        out float units) ||
                    units < 0f)
                {
                    row.Problem($"'{entry}' in {column} is not 'id:units'.");
                    continue;
                }

                found.Add(new KeyValuePair<string, float>(halves[0].Trim(), units));
            }

            return found;
        }
    }
}
