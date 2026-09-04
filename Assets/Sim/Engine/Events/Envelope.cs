using System;

namespace RTS.Sim.Engine.Events
{
    /// <summary>
    /// One event: its identity, what caused it, when, and the payload
    /// (ARCHITECTURE §6.2).
    /// </summary>
    /// <remarks>
    /// §6.2 writes this as `Envelope&lt;T&gt;`. One queue has to hold events of many payload
    /// types in a single insertion-ordered list — that ordering is the log — so the stored
    /// form is non-generic and the payload is boxed. Events are reports at input and day-
    /// boundary rate, not per-entity work, so the allocation is irrelevant; §7.1's ordering
    /// guarantee is not. Read payloads back with <see cref="TryGet{T}"/>.
    /// </remarks>
    public readonly struct Envelope
    {
        public readonly EventId Id;
        public readonly CauseId Cause;
        public readonly int Day;

        private readonly object _payload;

        public Envelope(EventId id, CauseId cause, int day, object payload)
        {
            Id = id;
            Cause = cause;
            Day = day;
            _payload = payload;
        }

        public Type PayloadType => _payload?.GetType();

        public bool Is<T>() => _payload is T;

        public bool TryGet<T>(out T payload)
        {
            if (_payload is T typed)
            {
                payload = typed;
                return true;
            }

            payload = default;
            return false;
        }

        /// <summary>The payload, or a throw naming both types. For callers that already know.</summary>
        public T Get<T>()
        {
            if (_payload is T typed) return typed;

            throw new InvalidOperationException(
                $"{Id} carries {PayloadType?.Name ?? "nothing"}, not {typeof(T).Name}.");
        }

        public override string ToString() =>
            $"{Id} {Cause} day {Day} {PayloadType?.Name ?? "empty"}";
    }
}
