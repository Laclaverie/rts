using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS.Sim.Engine.Pipeline
{
    /// <summary>
    /// Thrown when pipeline.csv and the implemented systems disagree. Carries every problem
    /// found, not just the first, so one relaunch reports the whole mismatch
    /// (ARCHITECTURE §4.2).
    /// </summary>
    public sealed class PipelineConfigurationException : Exception
    {
        public PipelineConfigurationException(IReadOnlyList<string> problems)
            : base(Format(problems))
        {
            Problems = problems ?? Array.Empty<string>();
        }

        public IReadOnlyList<string> Problems { get; }

        private static string Format(IReadOnlyList<string> problems)
        {
            if (problems == null || problems.Count == 0)
                return "Pipeline configuration is invalid.";

            return "Pipeline configuration is invalid:" + Environment.NewLine +
                   string.Join(Environment.NewLine, problems.Select(p => "  - " + p));
        }
    }
}
