using RTS.Sim.Engine.Events;

namespace RTS.Sim.Session
{
    /// <summary>
    /// How loudly a line should be shown. Not severity — a riot is bad news and a wage paid is
    /// good, and both are worth interrupting for.
    /// </summary>
    /// <remarks>
    /// The feed's job is to answer "what happened" for a player who looked away, and the enemy
    /// of that is volume. Every day pays wages, feeds people and produces goods; if all of it
    /// arrived at the same weight, the day a riot started would look like every other day.
    /// </remarks>
    public enum FeedImportance
    {
        /// <summary>Routine. The port working as intended.</summary>
        Detail = 0,

        /// <summary>Worth reading. A decision landed, or something changed direction.</summary>
        Notable = 1,

        /// <summary>Worth stopping for. Something is going wrong, or has.</summary>
        Alarming = 2,
    }

    /// <summary>
    /// One line of the event feed: what happened, when, and what caused it.
    /// </summary>
    /// <remarks>
    /// Carries <see cref="Cause"/> rather than a formatted "because…" string, so the feed stays
    /// a view of the causal DAG (§6.2) rather than a pile of sentences. Whoever draws it can
    /// follow the link, or ignore it and print a flat list.
    /// </remarks>
    public readonly struct FeedEntry
    {
        public FeedEntry(EventId id, CauseId cause, int day, string text, FeedImportance importance)
        {
            Id = id;
            Cause = cause;
            Day = day;
            Text = text;
            Importance = importance;
        }

        /// <summary>This line's node in the DAG. Other lines may name it as their cause.</summary>
        public readonly EventId Id;

        /// <summary>
        /// What produced it: a command, an earlier event, or <see cref="CauseId.Root"/> for the
        /// day boundary itself. Root is an answer, not a missing value — the day arriving is a
        /// real reason for people to eat.
        /// </summary>
        public readonly CauseId Cause;

        public readonly int Day;

        public readonly string Text;

        public readonly FeedImportance Importance;

        /// <summary>Whether anything but the day itself is to blame.</summary>
        public bool HasCause => Cause.Value != CauseId.Root.Value;

        public override string ToString() => $"day {Day}: {Text}";
    }
}
