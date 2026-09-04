using System;
using System.Collections.Generic;
using RTS.Sim.Engine.Events;

namespace RTS.Sim.Engine.Commands
{
    /// <summary>
    /// One dispatched command and what became of it.
    /// </summary>
    public readonly struct CommandLogEntry
    {
        public CommandLogEntry(EventId node, int day, ICommand command, bool applied, string rejectedBecause)
        {
            Node = node;
            Day = day;
            Command = command;
            Applied = applied;
            RejectedBecause = rejectedBecause;
        }

        /// <summary>This command's node in the causal DAG — the cause of whatever it emitted.</summary>
        public readonly EventId Node;

        public readonly int Day;
        public readonly ICommand Command;
        public readonly bool Applied;

        /// <summary>Why validation refused it, or null when it was applied.</summary>
        public readonly string RejectedBecause;

        public CauseId AsCause() => Node.AsCause();

        public override string ToString() =>
            Applied
                ? $"{Node} day {Day} {Command}"
                : $"{Node} day {Day} {Command} REJECTED: {RejectedBecause}";
    }

    /// <summary>
    /// Append-only record of every command dispatched, in dispatch order. A save is a seed
    /// plus this (ARCHITECTURE §6.1); loading means replaying it.
    /// </summary>
    /// <remarks>
    /// Rejected commands are logged too. They are inputs like any other, and replay re-runs
    /// validation against a reproduced world and reaches the same verdict — dropping them
    /// would make the log a record of outcomes rather than of what actually happened, and a
    /// bug report replaying "what the player did" would omit everything the player tried and
    /// was refused.
    /// </remarks>
    public sealed class CommandLog
    {
        private readonly List<CommandLogEntry> _entries = new List<CommandLogEntry>();

        public int Count => _entries.Count;

        public IReadOnlyList<CommandLogEntry> Entries => _entries;

        public CommandLogEntry this[int index] => _entries[index];

        internal void Append(in CommandLogEntry entry) => _entries.Add(entry);

        /// <summary>Applied entries only. The rest were refused and changed nothing.</summary>
        public IEnumerable<CommandLogEntry> Applied()
        {
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Applied)
                    yield return _entries[i];
        }

        /// <summary>
        /// Discards the record. Snapshots are a cache and the log is the truth (§6.1), so this
        /// is for tests and for starting a fresh session — never a routine operation.
        /// </summary>
        public void Clear() => _entries.Clear();

        public override string ToString() => $"CommandLog({_entries.Count})";
    }
}
