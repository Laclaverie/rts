using System;
using System.Collections.Generic;
using System.Linq;
using RTS.Content.Loading;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Events;

namespace RTS.Sim.Engine.Pipeline
{
    /// <summary>
    /// The ordered list of systems per phase, built from pipeline.csv (ARCHITECTURE §4.2).
    /// Order is data: reordering a system, or disabling one to isolate a bug, is a data edit
    /// and a relaunch, never a recompile.
    /// </summary>
    public sealed class Pipeline
    {
        public const string PhaseColumn = "phase";
        public const string OrderColumn = "order";
        public const string SystemColumn = "system";
        public const string EnabledColumn = "enabled";

        private readonly Dictionary<Phase, ISystem[]> _byPhase;

        private Pipeline(Dictionary<Phase, ISystem[]> byPhase)
        {
            _byPhase = byPhase;
        }

        /// <summary>Enabled systems for the phase, in the order the file declares.</summary>
        public IReadOnlyList<ISystem> Systems(Phase phase) => Resolve(phase);

        /// <summary>
        /// Runs the phase's enabled systems in declared order, inside a cause scope rooted at
        /// the phase itself.
        /// </summary>
        /// <remarks>
        /// The scope is what lets a system call <c>ctx.Events.Emit(...)</c> without knowing
        /// anything about causes (§6.2). A system acting because the day turned has
        /// <see cref="CauseId.Root"/> as its cause — that is an answer, not a gap. The command
        /// dispatcher opens a narrower scope inside this one when it applies a command, and
        /// the innermost wins.
        /// </remarks>
        public void Run(Phase phase, World world, in Context ctx)
        {
            ISystem[] systems = Resolve(phase);
            if (systems.Length == 0) return;

            EventQueue events = ctx.Events;
            events?.BeginCause(CauseId.Root, ctx.Day);

            try
            {
                // Indexed rather than foreach: the order of this loop is the whole point of the type.
                for (int i = 0; i < systems.Length; i++)
                    systems[i].Run(world, in ctx);
            }
            finally
            {
                // A system that throws must not leave the stack unbalanced; the next phase
                // would then attribute its events to a cause that already finished.
                events?.EndCause();
            }
        }

        private ISystem[] Resolve(Phase phase) =>
            _byPhase.TryGetValue(phase, out ISystem[] systems) ? systems : Array.Empty<ISystem>();

        /// <summary>
        /// Binds the declared order to the implemented systems, or throws describing every
        /// disagreement at once. A system present in code but absent from the file — or the
        /// reverse — is an error, never a silent skip: a system that quietly never runs is
        /// close to undebuggable (§4.2).
        /// </summary>
        public static Pipeline Build(CsvTable table, IEnumerable<ISystem> systems)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (systems == null) throw new ArgumentNullException(nameof(systems));

            var problems = new List<string>();

            Dictionary<string, ISystem> implemented = IndexImplementations(systems, problems);
            List<Entry> entries = ReadEntries(table, problems);

            ReportUndeclaredIds(entries, implemented, table.SourceName, problems);
            ReportUnlistedSystems(entries, implemented, table.SourceName, problems);

            if (problems.Count > 0) throw new PipelineConfigurationException(problems);

            return new Pipeline(GroupByPhase(entries, implemented));
        }

        private static Dictionary<string, ISystem> IndexImplementations(
            IEnumerable<ISystem> systems, List<string> problems)
        {
            var implemented = new Dictionary<string, ISystem>(StringComparer.Ordinal);

            foreach (ISystem system in systems)
            {
                if (system == null)
                {
                    problems.Add("a null system was registered.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(system.Id))
                {
                    problems.Add(system.GetType().Name + " has an empty Id.");
                    continue;
                }

                if (implemented.ContainsKey(system.Id))
                {
                    problems.Add("two registered systems share the Id '" + system.Id + "'.");
                    continue;
                }

                implemented.Add(system.Id, system);
            }

            return implemented;
        }

        private static List<Entry> ReadEntries(CsvTable table, List<string> problems)
        {
            var entries = new List<Entry>();
            var declaredAt = new Dictionary<string, int>(StringComparer.Ordinal);
            var slots = new Dictionary<PhaseOrder, string>();

            foreach (CsvRow row in table.Rows)
            {
                string id;
                Phase phase;
                int order;
                bool enabled;

                try
                {
                    id = row[SystemColumn];
                    order = row.GetInt(OrderColumn);
                    enabled = row.GetBool(EnabledColumn);

                    string phaseText = row[PhaseColumn];
                    if (!TryParsePhaseName(phaseText, out phase))
                    {
                        problems.Add(Where(table, row) + "unknown phase '" + phaseText + "'. Expected one of " +
                                     string.Join(", ", Enum.GetNames(typeof(Phase))) + ".");
                        continue;
                    }
                }
                catch (CsvFormatException e)
                {
                    problems.Add(e.Message);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(id))
                {
                    problems.Add(Where(table, row) + "empty system id.");
                    continue;
                }

                if (declaredAt.TryGetValue(id, out int firstLine))
                {
                    problems.Add(Where(table, row) + "'" + id + "' is already declared on line " + firstLine + ".");
                    continue;
                }

                // Equal (phase, order) leaves the run order ambiguous, and ambiguous order is
                // non-determinism waiting to happen (§7.1). Rejected rather than tie-broken.
                var slot = new PhaseOrder(phase, order);
                if (slots.TryGetValue(slot, out string other))
                {
                    problems.Add(Where(table, row) + "'" + id + "' and '" + other + "' both claim " +
                                 phase + " order " + order + ". Orders must be unique within a phase.");
                    continue;
                }

                declaredAt.Add(id, row.Line);
                slots.Add(slot, id);
                entries.Add(new Entry(phase, order, id, enabled, row.Line));
            }

            return entries;
        }

        private static readonly Dictionary<string, Phase> PhasesByName = BuildPhaseNames();

        private static Dictionary<string, Phase> BuildPhaseNames()
        {
            var names = new Dictionary<string, Phase>(StringComparer.Ordinal);
            foreach (Phase phase in (Phase[])Enum.GetValues(typeof(Phase)))
                names.Add(phase.ToString(), phase);

            return names;
        }

        /// <summary>
        /// Exact-name lookup rather than Enum.TryParse, which is far too permissive for a
        /// hand-edited config file: it accepts a numeric string ("1" becomes DayBoundary,
        /// so reordering the enum would silently repoint every such row) and it accepts a
        /// comma-separated list even for a non-flags enum ("Tick,DayBoundary" quietly
        /// resolves to one of them). Both would place systems in a phase nobody chose.
        /// </summary>
        private static bool TryParsePhaseName(string text, out Phase phase) =>
            PhasesByName.TryGetValue(text ?? string.Empty, out phase);

        private static string Where(CsvTable table, CsvRow row) =>
            table.SourceName + "(" + row.Line + "): ";

        private static void ReportUndeclaredIds(
            List<Entry> entries, Dictionary<string, ISystem> implemented,
            string source, List<string> problems)
        {
            foreach (Entry entry in entries)
            {
                if (!implemented.ContainsKey(entry.Id))
                {
                    problems.Add(source + "(" + entry.Line + "): '" + entry.Id +
                                 "' is declared but no system implements it.");
                }
            }
        }

        private static void ReportUnlistedSystems(
            List<Entry> entries, Dictionary<string, ISystem> implemented,
            string source, List<string> problems)
        {
            var declared = new HashSet<string>(entries.Select(e => e.Id), StringComparer.Ordinal);

            // Ordinal sort so the message reads identically run to run.
            IEnumerable<string> missing = implemented.Keys
                .Where(id => !declared.Contains(id))
                .OrderBy(id => id, StringComparer.Ordinal);

            foreach (string id in missing)
            {
                problems.Add("'" + id + "' is implemented but missing from " + source +
                             ". Add a row, or set enabled=false to turn it off deliberately.");
            }
        }

        private static Dictionary<Phase, ISystem[]> GroupByPhase(
            List<Entry> entries, Dictionary<string, ISystem> implemented)
        {
            var byPhase = new Dictionary<Phase, ISystem[]>();

            foreach (Phase phase in (Phase[])Enum.GetValues(typeof(Phase)))
            {
                Phase captured = phase;
                byPhase[phase] = entries
                    .Where(e => e.Phase == captured && e.Enabled)
                    .OrderBy(e => e.Order)
                    .Select(e => implemented[e.Id])
                    .ToArray();
            }

            return byPhase;
        }

        private readonly struct PhaseOrder : IEquatable<PhaseOrder>
        {
            private readonly Phase _phase;
            private readonly int _order;

            public PhaseOrder(Phase phase, int order)
            {
                _phase = phase;
                _order = order;
            }

            public bool Equals(PhaseOrder other) => _phase == other._phase && _order == other._order;

            public override bool Equals(object obj) => obj is PhaseOrder other && Equals(other);

            public override int GetHashCode() => ((int)_phase * 397) ^ _order;
        }

        private readonly struct Entry
        {
            public Entry(Phase phase, int order, string id, bool enabled, int line)
            {
                Phase = phase;
                Order = order;
                Id = id;
                Enabled = enabled;
                Line = line;
            }

            public readonly Phase Phase;
            public readonly int Order;
            public readonly string Id;
            public readonly bool Enabled;
            public readonly int Line;
        }
    }
}
