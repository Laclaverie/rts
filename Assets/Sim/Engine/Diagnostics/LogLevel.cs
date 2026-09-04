namespace RTS.Sim.Engine.Diagnostics
{
    /// <summary>
    /// Severity, ordered so a threshold comparison is a single integer test.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>Firehose. Per-entity, per-tick detail; off unless something is being hunted.</summary>
        Trace = 0,

        /// <summary>How the machinery ran: what loaded, what was skipped, how long it took.</summary>
        Debug = 1,

        /// <summary>Milestones a reader scanning the file would want: startup, save, mode change.</summary>
        Info = 2,

        /// <summary>Wrong but survivable — a default was substituted, a file was missing.</summary>
        Warn = 3,

        /// <summary>Broken. Something the player or the developer has to know about.</summary>
        Error = 4,

        /// <summary>Not a severity: the threshold that silences a channel entirely.</summary>
        Off = 5,
    }
}
