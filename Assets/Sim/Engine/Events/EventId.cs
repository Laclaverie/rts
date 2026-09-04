using System;

namespace RTS.Sim.Engine.Events
{
    /// <summary>
    /// Identifies one node in the causal DAG (ARCHITECTURE §6.2). Events and, later, applied
    /// commands are both nodes and draw from one shared numbering space, which is what lets a
    /// <see cref="CauseId"/> point at either with a single value.
    /// </summary>
    /// <remarks>
    /// §6.2 writes this as a `record struct`. Unity 6.3 compiles with -langversion:9.0, so the
    /// equality members are spelled out by hand — see the language note in §3.
    /// </remarks>
    public readonly struct EventId : IEquatable<EventId>
    {
        /// <summary>Reserved: no node. Never allocated.</summary>
        public static readonly EventId None = default;

        public readonly int Value;

        public EventId(int value) => Value = value;

        public bool IsNone => Value == 0;

        /// <summary>This node, seen as the cause of something that follows it.</summary>
        public CauseId AsCause() => new CauseId(Value);

        public bool Equals(EventId other) => Value == other.Value;

        public override bool Equals(object obj) => obj is EventId other && Equals(other);

        public override int GetHashCode() => Value;

        public static bool operator ==(EventId a, EventId b) => a.Value == b.Value;

        public static bool operator !=(EventId a, EventId b) => a.Value != b.Value;

        public override string ToString() => IsNone ? "EventId.None" : "e" + Value;
    }
}
