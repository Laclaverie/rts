using System;
using System.Collections.Generic;
using System.Linq;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Events;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Engine.Commands
{
    /// <summary>
    /// Queues commands and applies them at a defined pipeline position — never mid-system
    /// (ARCHITECTURE §6).
    /// </summary>
    public sealed class CommandDispatcher
    {
        private readonly Dictionary<Type, ICommandHandler> _handlers;
        private readonly EventQueue _events;

        private readonly List<ICommand> _pending = new List<ICommand>();
        private readonly List<ICommand> _draining = new List<ICommand>();

        private bool _isDraining;

        public CommandDispatcher(EventQueue events, IEnumerable<ICommandHandler> handlers)
        {
            _events = events ?? throw new ArgumentNullException(nameof(events));
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));

            _handlers = IndexHandlers(handlers);
        }

        public CommandLog Log { get; } = new CommandLog();

        public int PendingCount => _pending.Count;

        /// <summary>
        /// Accepts a command. Nothing happens yet: it is applied at the next
        /// <see cref="Drain"/>.
        /// </summary>
        /// <remarks>
        /// Rejects an unhandled command type here rather than at drain, so the stack trace
        /// points at whoever submitted it. A command that no handler claims is input that
        /// silently vanishes — the same failure §4.2 refuses for systems missing from
        /// pipeline.csv.
        /// </remarks>
        public void Enqueue(ICommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            if (!_handlers.ContainsKey(command.GetType()))
            {
                throw new InvalidOperationException(
                    $"No handler for {command.GetType().Name}. Register one, or the command " +
                    "would be accepted and then silently do nothing.");
            }

            _pending.Add(command);
        }

        /// <summary>
        /// Validates and applies every queued command, in the order submitted, each inside its
        /// own cause scope so anything it emits is attributed to it (§6.2). Returns what
        /// happened.
        /// </summary>
        /// <remarks>
        /// Commands enqueued <em>during</em> a drain — by a handler, or later by a subscriber
        /// reacting to an event (§7) — wait for the next drain. Applying them re-entrantly
        /// would make the order depend on call depth and could recurse without bound; §7 says
        /// events are drained at defined boundaries, never re-entrantly, and commands follow
        /// the same rule. The cost is a one-drain delay, which is deterministic and bounded.
        /// </remarks>
        public DrainResult Drain(World world, in Context ctx)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            if (_isDraining)
                throw new InvalidOperationException("Drain is already running. It is not re-entrant.");

            if (_pending.Count == 0) return default;

            // Take the batch, so anything enqueued while applying lands in the next one.
            _draining.Clear();
            _draining.AddRange(_pending);
            _pending.Clear();

            int applied = 0;
            int rejected = 0;

            _isDraining = true;
            try
            {
                for (int i = 0; i < _draining.Count; i++)
                {
                    if (Apply(_draining[i], world, in ctx)) applied++;
                    else rejected++;
                }
            }
            finally
            {
                _isDraining = false;
                _draining.Clear();
            }

            return new DrainResult(applied, rejected);
        }

        private bool Apply(ICommand command, World world, in Context ctx)
        {
            ICommandHandler handler = _handlers[command.GetType()];

            // Validation runs outside the cause scope: it must not mutate, and it must not
            // emit, so there is nothing to attribute yet.
            if (!handler.Validate(command, world, in ctx, out string reason))
            {
                Log.Append(new CommandLogEntry(EventId.None, ctx.Day, command, false, reason ?? "no reason given"));
                return false;
            }

            // The command becomes a node in the same DAG as events, drawn from the same
            // counter, so a CauseId can point at either without ambiguity (§6.2).
            EventId node = _events.AllocateId();

            _events.BeginCause(node.AsCause(), ctx.Day);
            try
            {
                handler.Apply(command, world, in ctx);
            }
            finally
            {
                // A throwing handler must not leave the attribution stack unbalanced.
                _events.EndCause();
            }

            Log.Append(new CommandLogEntry(node, ctx.Day, command, true, null));
            return true;
        }

        private static Dictionary<Type, ICommandHandler> IndexHandlers(IEnumerable<ICommandHandler> handlers)
        {
            var byType = new Dictionary<Type, ICommandHandler>();
            var problems = new List<string>();

            foreach (ICommandHandler handler in handlers)
            {
                if (handler == null)
                {
                    problems.Add("a null handler was registered.");
                    continue;
                }

                Type type = handler.CommandType;

                if (type == null)
                {
                    problems.Add($"{handler.GetType().Name} declares no CommandType.");
                    continue;
                }

                if (!typeof(ICommand).IsAssignableFrom(type))
                {
                    problems.Add($"{handler.GetType().Name} handles {type.Name}, which is not an ICommand.");
                    continue;
                }

                if (byType.TryGetValue(type, out ICommandHandler existing))
                {
                    problems.Add($"{type.Name} is handled by both {existing.GetType().Name} and " +
                                 $"{handler.GetType().Name}. Exactly one handler per command.");
                    continue;
                }

                byType.Add(type, handler);
            }

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Command handler registration is invalid:" + Environment.NewLine +
                    string.Join(Environment.NewLine, problems.Select(p => "  - " + p)));
            }

            return byType;
        }

        /// <summary>What one drain did.</summary>
        public readonly struct DrainResult
        {
            public DrainResult(int applied, int rejected)
            {
                Applied = applied;
                Rejected = rejected;
            }

            public readonly int Applied;
            public readonly int Rejected;

            public int Total => Applied + Rejected;

            public override string ToString() => $"applied {Applied}, rejected {Rejected}";
        }
    }
}
