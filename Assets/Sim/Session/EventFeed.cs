using System;
using System.Collections.Generic;
using RTS.Content.Registries;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Events;

namespace RTS.Sim.Session
{
    /// <summary>
    /// What happened, most recent last, with what caused it (GDD §5.1; ARCHITECTURE §6.2).
    /// </summary>
    /// <remarks>
    /// The loop of §5.1 is read the world, decide, commit, absorb the fallout. This is the
    /// first step: at twenty minutes a day a player looks away, and what they need on returning
    /// is not the current numbers but the story that produced them.
    /// <para>
    /// <strong>It is a view of the causal DAG, not a log.</strong> §6.2 put the hook in months
    /// before anything consumed it, precisely so that this could exist: every line carries the
    /// node that produced it, so "you shut the warehouse" and "2 crew released" are linked
    /// rather than adjacent. That link is what the Decision Timeline eventually reads, and it
    /// could not have been reconstructed afterwards.
    /// </para>
    /// <para>
    /// Bounded, and old lines are dropped. A feed that keeps everything becomes a log nobody
    /// reads; the command log is the record, and it is already complete and replayable (§6.1).
    /// </para>
    /// </remarks>
    public sealed class EventFeed
    {
        /// <summary>
        /// How many lines are kept. A few hundred is several days of a busy port — enough to
        /// scroll back through what went wrong, short of becoming an archive.
        /// </summary>
        public const int DefaultCapacity = 300;

        private readonly List<FeedEntry> _entries = new List<FeedEntry>();
        private readonly Dictionary<int, int> _indexById = new Dictionary<int, int>();
        private int _commandsSeen;

        public EventFeed(int capacity = DefaultCapacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
        }

        public int Capacity { get; }

        /// <summary>Oldest first, so a renderer can draw it downwards and scroll to the end.</summary>
        public IReadOnlyList<FeedEntry> Entries => _entries;

        public int Count => _entries.Count;

        /// <summary>
        /// Takes the commands issued since last time, then the day's drained events.
        /// </summary>
        /// <remarks>
        /// Commands first, deliberately. They are the cause of the events that follow them, and
        /// a feed that listed the consequence above the decision would read backwards.
        /// </remarks>
        public void Record(CommandLog log, IReadOnlyList<Envelope> drained, BalanceTables balance,
            EntityId port = default)
        {
            RecordCommands(log);
            RecordEvents(drained, balance, port);
        }

        private void RecordCommands(CommandLog log)
        {
            if (log == null) return;

            for (; _commandsSeen < log.Entries.Count; _commandsSeen++)
            {
                CommandLogEntry entry = log.Entries[_commandsSeen];
                string text = FeedNarrator.Describe(in entry, out FeedImportance importance);

                // A command's own node is its cause for everything it goes on to do, and the
                // command itself was caused by the player, which the DAG spells Root.
                Add(new FeedEntry(entry.Node, CauseId.Root, entry.Day, text, importance));
            }
        }

        /// <summary>
        /// Records the day's events, keeping only the ones that happened to this city.
        /// </summary>
        /// <remarks>
        /// One queue carries every city's day. Without the filter the player's feed showed five
        /// paydays every morning and reported a neighbour's famine as their own — five times the
        /// noise and none of it true. Whether another city's troubles are visible at all belongs
        /// with stances and intelligence (§5.6) rather than leaking by accident.
        /// <para>
        /// <see cref="EntityId.None"/> keeps everything, which is what a single-port test wants.
        /// </para>
        /// </remarks>
        private void RecordEvents(IReadOnlyList<Envelope> drained, BalanceTables balance,
            EntityId port)
        {
            if (drained == null) return;

            for (int i = 0; i < drained.Count; i++)
            {
                Envelope envelope = drained[i];

                if (!FeedNarrator.TryDescribe(in envelope, balance,
                        out string text, out FeedImportance importance, out EntityId happenedTo))
                {
                    continue;
                }

                if (!port.IsNone && !happenedTo.IsNone && happenedTo != port) continue;

                Add(new FeedEntry(envelope.Id, envelope.Cause, envelope.Day, text, importance));
            }
        }

        /// <summary>
        /// Finds the line that caused this one, if it is still in the feed.
        /// </summary>
        /// <remarks>
        /// "If still" is not a caveat to apologise for. The feed is bounded, so a consequence
        /// can outlive its cause — which is honest: the port is still paying for a decision the
        /// player has scrolled past.
        /// </remarks>
        public bool TryFindCause(in FeedEntry entry, out FeedEntry cause)
        {
            cause = default;
            if (!entry.HasCause) return false;

            if (!_indexById.TryGetValue(entry.Cause.Value, out int index)) return false;

            cause = _entries[index];
            return true;
        }

        /// <summary>Everything this line went on to cause, directly.</summary>
        public List<FeedEntry> Consequences(in FeedEntry entry)
        {
            var found = new List<FeedEntry>();

            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Cause.Value == entry.Id.Value)
                    found.Add(_entries[i]);

            return found;
        }

        /// <summary>The most recent lines at or above an importance, oldest first.</summary>
        public List<FeedEntry> Recent(int count, FeedImportance atLeast = FeedImportance.Detail)
        {
            var found = new List<FeedEntry>();

            for (int i = _entries.Count - 1; i >= 0 && found.Count < count; i--)
                if (_entries[i].Importance >= atLeast)
                    found.Add(_entries[i]);

            found.Reverse();
            return found;
        }

        public void Clear()
        {
            _entries.Clear();
            _indexById.Clear();
        }

        private void Add(in FeedEntry entry)
        {
            _entries.Add(entry);
            _indexById[entry.Id.Value] = _entries.Count - 1;

            if (_entries.Count <= Capacity) return;

            // Dropping from the front invalidates every index, so the map is rebuilt. It
            // happens once per line past the cap on a list of a few hundred, which is nothing
            // against a day boundary — and the alternative, a ring with wrapped indices, is
            // fiddly in exactly the way a feed does not deserve.
            int excess = _entries.Count - Capacity;
            _entries.RemoveRange(0, excess);

            _indexById.Clear();
            for (int i = 0; i < _entries.Count; i++) _indexById[_entries[i].Id.Value] = i;
        }
    }
}
