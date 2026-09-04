namespace RTS.Sim.Engine.Diagnostics
{
    /// <summary>
    /// The streams a reader can filter in or out. A closed set, deliberately.
    /// </summary>
    /// <remarks>
    /// Strings would let a class declare a channel without touching this file, which sounds
    /// like flexibility and is mostly a hole: a misspelling in <c>logging.csv</c> would
    /// configure a channel nobody logs to while the real one silently keeps its default. That
    /// is the same silent-omission failure §4.2 refuses for a system missing from
    /// <c>pipeline.csv</c>, and it is why <see cref="Phase"/> is an enum too.
    /// <para>
    /// Adding a channel is an edit here and a recompile. So is adding a system. The set is
    /// meant to stay small enough that a person can read the whole list and choose from it —
    /// a filter list of eighty per-class channels is not a filter, it is a haystack.
    /// </para>
    /// <para>
    /// Values are explicit because they index the level table; keep them contiguous from zero.
    /// </para>
    /// </remarks>
    public enum LogChannel
    {
        /// <summary>Startup and shutdown: what was found, what was loaded, in what order.</summary>
        Boot = 0,

        /// <summary>CSV loading, schema validation, hot reload (§5.3, §5.4).</summary>
        Content = 1,

        /// <summary>System order, phase timings, what ran and what was disabled (§4.2).</summary>
        Pipeline = 2,

        /// <summary>Commands accepted, applied, and refused (§6).</summary>
        Commands = 3,

        /// <summary>Event emission and drain (§7).</summary>
        Events = 4,

        /// <summary>Snapshots, digests, replay (§6.1).</summary>
        State = 5,

        /// <summary>The Unity side: scenes, presentation, input.</summary>
        Game = 6,

        /// <summary>Anything without a home yet. If it earns a channel, give it one.</summary>
        Misc = 7,
    }
}
