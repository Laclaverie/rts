using System;
using System.Collections.Generic;

namespace RTS.Sim.Engine.Events
{
    /// <summary>
    /// Emit-only from a system's point of view. Every event is stamped with the cause that
    /// produced it, automatically (ARCHITECTURE §6.2).
    /// </summary>
    /// <remarks>
    /// <see cref="Emit{T}"/> takes no cause and no id, because a system that could pass one
    /// could pass the wrong one, or forget. The cause comes from the scope the caller is
    /// already in — the dispatcher opens one when it applies a command, the drain opens one
    /// when a subscriber reacts. Make the correct thing the only thing (C9).
    /// <para>
    /// Nothing consumes any of this yet; §6.2 is explicit that the hook goes in now anyway,
    /// because causal knowledge exists only at the moment a system acts and retrofitting it
    /// later means editing every system.
    /// </para>
    /// <para>
    /// Not thread-safe, and deliberately so: §7.1 forbids the non-determinism that concurrent
    /// emission would introduce.
    /// </para>
    /// </remarks>
    public sealed class EventQueue
    {
        private readonly List<Envelope> _pending = new List<Envelope>();
        private readonly Stack<Scope> _scopes = new Stack<Scope>();

        private int _lastId;

        /// <summary>The cause currently being attributed. Only meaningful inside a scope.</summary>
        public CauseId CurrentCause => _scopes.Count == 0 ? CauseId.Root : _scopes.Peek().Cause;

        /// <summary>The day stamped onto emitted events. Set when the scope opens.</summary>
        public int CurrentDay => _scopes.Count == 0 ? 0 : _scopes.Peek().Day;

        /// <summary>Whether a cause scope is open. Emitting outside one is an error.</summary>
        public bool InScope => _scopes.Count > 0;

        /// <summary>How deep the attribution stack is. Diagnostics and tests.</summary>
        public int ScopeDepth => _scopes.Count;

        /// <summary>Emitted and not yet drained, in emission order.</summary>
        public IReadOnlyList<Envelope> Pending => _pending;

        public int PendingCount => _pending.Count;

        /// <summary>
        /// Opens the attribution scope. Everything emitted until <see cref="EndCause"/> is
        /// attributed to <paramref name="cause"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="CauseId.Root"/> is a valid cause, meaning the phase itself acted rather
        /// than anything in particular — the day boundary arriving is a real reason for
        /// consumption to happen.
        /// <para>
        /// Scopes nest, because the architecture nests: the command dispatcher is drained at a
        /// pipeline position (§6), so applying a command happens inside a phase that already
        /// has a cause. The innermost scope wins, which attributes an event to the command
        /// that triggered it rather than to the phase that contained it. Every
        /// <see cref="BeginCause"/> needs its <see cref="EndCause"/>.
        /// </para>
        /// </remarks>
        public void BeginCause(CauseId cause, int day)
        {
            _scopes.Push(new Scope(cause, day));
        }

        public void EndCause()
        {
            if (_scopes.Count == 0)
                throw new InvalidOperationException("No cause scope is open.");

            _scopes.Pop();
        }

        /// <summary>
        /// Records that something happened. Returns the new node's id, so a caller that goes
        /// on to cause something else can attribute it — see <see cref="EventId.AsCause"/>.
        /// </summary>
        public EventId Emit<T>(in T payload) where T : struct
        {
            if (_scopes.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Cannot emit {typeof(T).Name} outside a cause scope. The dispatcher or the " +
                    "phase runner opens one; an event with no attributable cause would put a " +
                    "hole in the causal DAG (ARCHITECTURE §6.2).");
            }

            var id = new EventId(++_lastId);
            _pending.Add(new Envelope(id, CurrentCause, CurrentDay, payload));
            return id;
        }

        /// <summary>
        /// Allocates a node id without emitting. The command dispatcher uses this so applied
        /// commands are nodes in the same DAG as events, rather than a second id space that
        /// would collide with this one.
        /// </summary>
        public EventId AllocateId() => new EventId(++_lastId);

        /// <summary>
        /// Hands over everything pending and empties the queue. Called at defined phase
        /// boundaries, never re-entrantly mid-system (§7).
        /// </summary>
        /// <remarks>
        /// Returns a copy: draining while a subscriber emits in reaction is the normal case,
        /// and iterating the live list would mean mutating a collection under enumeration.
        /// </remarks>
        public Envelope[] Drain()
        {
            if (_pending.Count == 0) return Array.Empty<Envelope>();

            Envelope[] drained = _pending.ToArray();
            _pending.Clear();
            return drained;
        }

        /// <summary>
        /// Discards pending events without delivering them. For tests and for tearing a world
        /// down; the causal record is expendable by design (§6.2).
        /// </summary>
        public void Clear() => _pending.Clear();

        private readonly struct Scope
        {
            public Scope(CauseId cause, int day)
            {
                Cause = cause;
                Day = day;
            }

            public readonly CauseId Cause;
            public readonly int Day;
        }
    }
}
