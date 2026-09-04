using System;
using System.Collections.Generic;
using System.Linq;
using RTS.Content.Loading;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.State;
using RTS.Sim.Engine.Events;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Tests
{
    [Category(TestCategories.Unit)]
    public class CommandDispatcherTests
    {
        // Commands are data: sealed records, per ARCHITECTURE §6.
        private sealed record Raise(int By) : ICommand;

        private sealed record Lower(int By) : ICommand;

        private sealed record Unregistered : ICommand;

        private struct Raised { public int To; }

        private struct Tally : IComponentData
        {
            public int Value;

            public void Write(IStateWriter writer) => writer.Write("value", Value);
        }

        /// <summary>Adds to a Tally on a fixed entity, and reports it.</summary>
        private sealed class RaiseHandler : ICommandHandler
        {
            private readonly int _max;
            private readonly bool _throwOnApply;

            public RaiseHandler(int max = int.MaxValue, bool throwOnApply = false)
            {
                _max = max;
                _throwOnApply = throwOnApply;
            }

            public Type CommandType => typeof(Raise);

            public List<string> Calls { get; } = new List<string>();

            public CommandRejection Validate(ICommand command, World world, in Context ctx)
            {
                Calls.Add("validate");
                var raise = (Raise)command;

                return raise.By > _max ? CommandRejection.OutOfRange : CommandRejection.None;
            }

            public void Apply(ICommand command, World world, in Context ctx)
            {
                Calls.Add("apply");
                if (_throwOnApply) throw new InvalidOperationException("handler blew up");

                var raise = (Raise)command;
                EntityId target = world.Entities.Count > 0 ? world.Entities[0] : world.CreateEntity();

                if (!world.Has<Tally>(target)) world.Add(target, new Tally { Value = 0 });
                world.GetRef<Tally>(target).Value += raise.By;

                ctx.Events.Emit(new Raised { To = world.GetRef<Tally>(target).Value });
            }
        }

        private sealed class LowerHandler : ICommandHandler
        {
            public Type CommandType => typeof(Lower);

            public CommandRejection Validate(ICommand command, World world, in Context ctx) =>
                CommandRejection.None;

            public void Apply(ICommand command, World world, in Context ctx)
            {
                EntityId target = world.Entities.Count > 0 ? world.Entities[0] : world.CreateEntity();
                if (!world.Has<Tally>(target)) world.Add(target, new Tally());
                world.GetRef<Tally>(target).Value -= ((Lower)command).By;
            }
        }

        /// <summary>Enqueues another command while being applied, to test re-entrancy.</summary>
        private sealed class ReentrantHandler : ICommandHandler
        {
            private CommandDispatcher _dispatcher = null!;   // set by Bind before use

            public void Bind(CommandDispatcher dispatcher) => _dispatcher = dispatcher;

            public int Applications { get; private set; }

            public Type CommandType => typeof(Raise);

            public CommandRejection Validate(ICommand command, World world, in Context ctx) =>
                CommandRejection.None;

            public void Apply(ICommand command, World world, in Context ctx)
            {
                Applications++;
                if (Applications < 5) _dispatcher.Enqueue(new Raise(1));
            }
        }

        private static Context ContextWith(EventQueue events, int day = 1) =>
            new Context(day, 0f, events);

        private static CommandDispatcher Dispatcher(EventQueue events, params ICommandHandler[] handlers) =>
            new CommandDispatcher(events, handlers);

        // ------------------------------------------------------------------ applying

        [Test]
        public void A_queued_command_does_nothing_until_the_drain()
        {
            var events = new EventQueue();
            var handler = new RaiseHandler();
            CommandDispatcher dispatcher = Dispatcher(events, handler);
            var world = new World();

            dispatcher.Enqueue(new Raise(3));

            Assert.That(handler.Calls, Is.Empty, "commands are applied at a pipeline position, never on submit");
            Assert.That(dispatcher.PendingCount, Is.EqualTo(1));

            dispatcher.Drain(world, ContextWith(events));

            Assert.That(handler.Calls, Is.EqualTo(new[] { "validate", "apply" }));
        }

        [Test]
        public void Commands_apply_in_submission_order()
        {
            var events = new EventQueue();
            CommandDispatcher dispatcher = Dispatcher(events, new RaiseHandler(), new LowerHandler());
            var world = new World();

            dispatcher.Enqueue(new Raise(10));
            dispatcher.Enqueue(new Lower(3));
            dispatcher.Enqueue(new Raise(1));

            dispatcher.Drain(world, ContextWith(events));

            EntityId only = world.Entities[0];
            Assert.That(world.GetRef<Tally>(only).Value, Is.EqualTo(8));
            Assert.That(dispatcher.Log.Entries.Select(e => e.Command.GetType().Name),
                Is.EqualTo(new[] { "Raise", "Lower", "Raise" }));
        }

        [Test]
        public void Drain_reports_what_it_did()
        {
            var events = new EventQueue();
            CommandDispatcher dispatcher = Dispatcher(events, new RaiseHandler(max: 5));
            dispatcher.Enqueue(new Raise(1));
            dispatcher.Enqueue(new Raise(99));

            CommandDispatcher.DrainResult result = dispatcher.Drain(new World(), ContextWith(events));

            Assert.That(result.Applied, Is.EqualTo(1));
            Assert.That(result.Rejected, Is.EqualTo(1));
            Assert.That(result.Total, Is.EqualTo(2));
        }

        [Test]
        public void Draining_an_empty_queue_is_a_no_op()
        {
            var events = new EventQueue();
            CommandDispatcher dispatcher = Dispatcher(events, new RaiseHandler());

            CommandDispatcher.DrainResult result = dispatcher.Drain(new World(), ContextWith(events));

            Assert.That(result.Total, Is.EqualTo(0));
            Assert.That(dispatcher.Log.Count, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ rejection

        [Test]
        public void A_rejected_command_changes_nothing_but_is_still_logged_with_its_reason()
        {
            var events = new EventQueue();
            CommandDispatcher dispatcher = Dispatcher(events, new RaiseHandler(max: 5));
            var world = new World();

            dispatcher.Enqueue(new Raise(99));
            dispatcher.Drain(world, ContextWith(events));

            Assert.That(world.EntityCount, Is.EqualTo(0), "a refused command must not touch the world");
            Assert.That(dispatcher.Log.Count, Is.EqualTo(1), "what the player tried is part of the record");

            CommandLogEntry entry = dispatcher.Log[0];
            Assert.That(entry.Applied, Is.False);
            Assert.That(entry.Rejection, Is.EqualTo(CommandRejection.OutOfRange),
                "a code, not a sentence: this entry is serialised into saves and digested");
            Assert.That(entry.Node, Is.EqualTo(EventId.None), "nothing happened, so there is no DAG node");
        }

        [Test]
        public void Rejection_does_not_stop_the_rest_of_the_batch()
        {
            var events = new EventQueue();
            CommandDispatcher dispatcher = Dispatcher(events, new RaiseHandler(max: 5));
            var world = new World();

            dispatcher.Enqueue(new Raise(99));
            dispatcher.Enqueue(new Raise(2));
            dispatcher.Drain(world, ContextWith(events));

            Assert.That(world.GetRef<Tally>(world.Entities[0]).Value, Is.EqualTo(2));
        }

        [Test]
        public void Applied_lists_only_what_took_effect()
        {
            var events = new EventQueue();
            CommandDispatcher dispatcher = Dispatcher(events, new RaiseHandler(max: 5));
            dispatcher.Enqueue(new Raise(1));
            dispatcher.Enqueue(new Raise(99));
            dispatcher.Enqueue(new Raise(2));
            dispatcher.Drain(new World(), ContextWith(events));

            Assert.That(dispatcher.Log.Count, Is.EqualTo(3));
            Assert.That(dispatcher.Log.Applied().Count(), Is.EqualTo(2));
        }

        // ------------------------------------------------------------------ causality

        [Test]
        public void Events_emitted_by_a_handler_are_attributed_to_the_command()
        {
            var events = new EventQueue();
            CommandDispatcher dispatcher = Dispatcher(events, new RaiseHandler());
            dispatcher.Enqueue(new Raise(4));

            dispatcher.Drain(new World(), ContextWith(events, day: 6));

            CommandLogEntry entry = dispatcher.Log[0];
            Envelope emitted = events.Pending.Single();

            Assert.That(emitted.Cause, Is.EqualTo(entry.AsCause()));
            Assert.That(emitted.Cause.IsRoot, Is.False, "the command caused it, not the phase");
            Assert.That(emitted.Day, Is.EqualTo(6));
        }

        [Test]
        public void Command_nodes_and_event_nodes_never_collide()
        {
            var events = new EventQueue();
            CommandDispatcher dispatcher = Dispatcher(events, new RaiseHandler());
            dispatcher.Enqueue(new Raise(1));
            dispatcher.Enqueue(new Raise(1));
            dispatcher.Drain(new World(), ContextWith(events));

            var ids = dispatcher.Log.Applied().Select(e => e.Node.Value)
                .Concat(events.Pending.Select(e => e.Id.Value))
                .ToArray();

            Assert.That(ids, Is.Unique);
        }

        [Test]
        public void A_throwing_handler_leaves_the_cause_stack_balanced()
        {
            var events = new EventQueue();
            CommandDispatcher dispatcher = Dispatcher(events, new RaiseHandler(throwOnApply: true));
            dispatcher.Enqueue(new Raise(1));

            Assert.Throws<InvalidOperationException>(
                () => dispatcher.Drain(new World(), ContextWith(events)));

            Assert.That(events.ScopeDepth, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ re-entrancy

        [Test]
        public void A_command_enqueued_during_a_drain_waits_for_the_next_one()
        {
            // Re-entrant application would make order depend on call depth and could recurse
            // without bound. §7 drains at defined boundaries; commands follow the same rule.
            var events = new EventQueue();
            var handler = new ReentrantHandler();
            CommandDispatcher dispatcher = Dispatcher(events, handler);
            handler.Bind(dispatcher);
            var world = new World();

            dispatcher.Enqueue(new Raise(1));
            dispatcher.Drain(world, ContextWith(events));

            Assert.That(handler.Applications, Is.EqualTo(1), "one drain applies exactly one batch");
            Assert.That(dispatcher.PendingCount, Is.EqualTo(1), "the follow-up waits for the next drain");

            dispatcher.Drain(world, ContextWith(events));
            Assert.That(handler.Applications, Is.EqualTo(2));
        }

        // ------------------------------------------------------------------ registration

        [Test]
        public void Enqueuing_an_unhandled_command_is_loud_at_submission()
        {
            var events = new EventQueue();
            CommandDispatcher dispatcher = Dispatcher(events, new RaiseHandler());

            var e = Assert.Throws<InvalidOperationException>(() => dispatcher.Enqueue(new Unregistered()));

            Assert.That(e.Message, Does.Contain("No handler for Unregistered"));
            Assert.That(dispatcher.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Two_handlers_for_one_command_type_are_rejected()
        {
            var events = new EventQueue();

            var e = Assert.Throws<InvalidOperationException>(
                () => Dispatcher(events, new RaiseHandler(), new RaiseHandler()));

            Assert.That(e.Message, Does.Contain("Exactly one handler per command"));
        }

        [Test]
        public void A_null_handler_is_rejected()
        {
            var events = new EventQueue();

            var e = Assert.Throws<InvalidOperationException>(() => Dispatcher(events, (ICommandHandler)null!));

            Assert.That(e.Message, Does.Contain("null handler"));
        }

        [Test]
        public void Enqueuing_null_throws()
        {
            var events = new EventQueue();
            CommandDispatcher dispatcher = Dispatcher(events, new RaiseHandler());

            Assert.Throws<ArgumentNullException>(() => dispatcher.Enqueue(null));
        }

        // ------------------------------------------------------------------ determinism

        [Test]
        public void The_same_command_log_replays_to_the_same_world_and_the_same_record()
        {
            // A rehearsal for the Phase 0 gate: a save is a seed plus this log (§6.1).
            (int tally, string[] log) Run()
            {
                var events = new EventQueue();
                CommandDispatcher dispatcher = Dispatcher(events, new RaiseHandler(max: 50), new LowerHandler());
                var world = new World();

                dispatcher.Enqueue(new Raise(10));
                dispatcher.Enqueue(new Raise(99));   // rejected, and still part of the record
                dispatcher.Enqueue(new Lower(4));
                dispatcher.Drain(world, ContextWith(events, day: 2));

                return (world.GetRef<Tally>(world.Entities[0]).Value,
                    dispatcher.Log.Entries.Select(e => e.ToString()).ToArray());
            }

            (int tally, string[] log) first = Run();
            (int tally, string[] log) second = Run();

            Assert.That(second.tally, Is.EqualTo(first.tally));
            Assert.That(second.log, Is.EqualTo(first.log));
        }

        // ------------------------------------------------------------------ pipeline

        [Test]
        public void The_drain_runs_at_the_position_pipeline_csv_declares()
        {
            // "when input takes effect" is an ordering decision, so it lives in data (§4.2).
            var events = new EventQueue();
            var handler = new RaiseHandler();
            CommandDispatcher dispatcher = Dispatcher(events, handler);
            var drain = new CommandDrainSystem(dispatcher);

            CsvTable table = CsvTable.Parse(
                "phase,order,system,enabled\nDayBoundary,10," + CommandDrainSystem.SystemId + ",true\n",
                "pipeline.csv");

            Pipeline pipeline = Pipeline.Build(table, new ISystem[] { drain });
            var world = new World();

            dispatcher.Enqueue(new Raise(7));
            pipeline.Run(Phase.DayBoundary, world, ContextWith(events, day: 3));

            Assert.That(drain.LastResult.Applied, Is.EqualTo(1));
            Assert.That(world.GetRef<Tally>(world.Entities[0]).Value, Is.EqualTo(7));

            // Still attributed to the command, not to the phase scope the pipeline opened.
            Assert.That(events.Pending.Single().Cause, Is.EqualTo(dispatcher.Log[0].AsCause()));
        }

        [Test]
        public void A_disabled_drain_row_stops_input_taking_effect_at_all()
        {
            var events = new EventQueue();
            var handler = new RaiseHandler();
            CommandDispatcher dispatcher = Dispatcher(events, handler);
            var drain = new CommandDrainSystem(dispatcher);

            CsvTable table = CsvTable.Parse(
                "phase,order,system,enabled\nDayBoundary,10," + CommandDrainSystem.SystemId + ",false\n",
                "pipeline.csv");

            Pipeline pipeline = Pipeline.Build(table, new ISystem[] { drain });

            dispatcher.Enqueue(new Raise(7));
            pipeline.Run(Phase.DayBoundary, new World(), ContextWith(events));

            Assert.That(dispatcher.PendingCount, Is.EqualTo(1), "nothing drained it");
            Assert.That(handler.Calls, Is.Empty);
        }
    }
}
