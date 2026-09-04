using System;
using System.Collections.Generic;
using RTS.Content.Validation;

namespace RTS.Content.Registries
{
    /// <summary>
    /// The second pass: checks that every id one table points at exists in another
    /// (ARCHITECTURE §5.3, "every referenced ID resolves").
    /// </summary>
    /// <remarks>
    /// Two passes are necessary, not tidiness. <c>buildings.csv</c> may reference a good that
    /// <c>goods.csv</c> defines on a later line or in a file not yet read, so references are
    /// collected while rows are parsed and resolved once every table is loaded.
    /// <para>
    /// It lives here rather than on <see cref="ValidationReport"/> so that
    /// <c>Content.Validation</c> stays free of any dependency on registries — the arrow points
    /// one way.
    /// </para>
    /// </remarks>
    public static class ReferenceResolver
    {
        /// <summary>
        /// Resolves every pending reference whose <see cref="PendingReference.TargetTable"/>
        /// matches <paramref name="targetTable"/>, recording each miss.
        /// </summary>
        /// <returns>How many references were checked.</returns>
        public static int Resolve<T>(
            ValidationReport report, IEnumerable<PendingReference> pending,
            string targetTable, ConfigRegistry<T> registry) where T : IHasId
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (pending == null) throw new ArgumentNullException(nameof(pending));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            int checkedCount = 0;

            foreach (PendingReference reference in pending)
            {
                if (!string.Equals(reference.TargetTable, targetTable, StringComparison.Ordinal))
                    continue;

                checkedCount++;

                if (registry.Contains(reference.Value)) continue;

                report.Add(reference.Source, reference.Line,
                    $"column '{reference.Column}' references '{reference.Value}', " +
                    $"which does not exist in {registry.SourceName}.");
            }

            return checkedCount;
        }

        /// <summary>
        /// Catches references aimed at a table nobody resolved. A reference silently checked
        /// against nothing is worse than no check: it reads as validated.
        /// </summary>
        public static void ReportUnresolvedTables(
            ValidationReport report, IEnumerable<PendingReference> pending,
            IEnumerable<string> resolvedTables)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (pending == null) throw new ArgumentNullException(nameof(pending));

            var resolved = new HashSet<string>(resolvedTables ?? Array.Empty<string>(), StringComparer.Ordinal);
            var alreadyReported = new HashSet<string>(StringComparer.Ordinal);

            foreach (PendingReference reference in pending)
            {
                if (resolved.Contains(reference.TargetTable)) continue;
                if (!alreadyReported.Add(reference.TargetTable)) continue;

                report.Add(reference.Source, reference.Line,
                    $"column '{reference.Column}' points at table '{reference.TargetTable}', " +
                    "which was never resolved. The reference has not been checked.");
            }
        }
    }
}
