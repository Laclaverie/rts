using System;

namespace RTS.Sim.Engine.Events
{
    /// <summary>
    /// Why something happened: the node that produced it — a command, or a prior event
    /// (ARCHITECTURE §6.2).
    /// </summary>
    /// <remarks>
    /// A single int is enough because commands and events share one node-id space
    /// (<see cref="EventId"/>). Two separate spaces behind one value would collide, and the
    /// resulting DAG would silently link the wrong parent.
    /// <para>
    /// <see cref="Root"/> means "nothing caused this" — a system acting on the phase itself
    /// rather than in response to anything. It is a legitimate answer, not a missing value:
    /// the day boundary arriving is a real reason for consumption to happen.
    /// </para>
    /// </remarks>
    public readonly struct CauseId : IEquatable<CauseId>
    {
        /// <summary>The DAG's root: the tick or day boundary itself, caused by nothing.</summary>
        public static readonly CauseId Root = default;

        public readonly int Value;

        public CauseId(int value) => Value = value;

        public bool IsRoot => Value == 0;

        public bool Equals(CauseId other) => Value == other.Value;

        public override bool Equals(object obj) => obj is CauseId other && Equals(other);

        public override int GetHashCode() => Value;

        public static bool operator ==(CauseId a, CauseId b) => a.Value == b.Value;

        public static bool operator !=(CauseId a, CauseId b) => a.Value != b.Value;

        public override string ToString() => IsRoot ? "CauseId.Root" : "<-" + Value;
    }
}
