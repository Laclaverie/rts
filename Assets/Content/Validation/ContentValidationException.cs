using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS.Content.Validation
{
    /// <summary>
    /// Thrown when the shipped content does not validate. Carries every problem found, not
    /// just the first (ARCHITECTURE §5.3).
    /// </summary>
    public sealed class ContentValidationException : Exception
    {
        public ContentValidationException(IReadOnlyList<string> problems)
            : base(Format(problems))
        {
            Problems = problems ?? Array.Empty<string>();
        }

        public IReadOnlyList<string> Problems { get; }

        private static string Format(IReadOnlyList<string> problems)
        {
            if (problems == null || problems.Count == 0) return "Content is invalid.";

            return $"Content is invalid ({problems.Count} problem(s)):" + Environment.NewLine +
                   string.Join(Environment.NewLine, problems.Select(p => "  - " + p));
        }
    }
}
