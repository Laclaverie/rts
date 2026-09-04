using System;
using System.Collections.Generic;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Events;
using RTS.Sim.Engine.Pipeline;
using RTS.Sim.Engine.Randomness;

namespace RTS.Sim.Engine.State
{
    /// <summary>
    /// Runs a world forward from a seed and a scripted command log, and digests the result.
    /// The replay-determinism gate is two of these compared (BUILD_ORDER Phase 0).
    /// </summary>
    /// <remarks>
    /// This is not test scaffolding that happens to live in Sim: §6.1 makes loading a save
    /// <em>the same operation</em> — a seed plus a command log, replayed. The save loader and
    /// the functional-test harness (§8.2) are this, with a file at one end.
    /// </remarks>
    public sealed class ReplayRun
    {
        private readonly Pipeline.Pipeline _pipeline;
        private readonly CommandDispatcher _dispatcher;

        private ReplayRun(World world, Rng rng, EventQueue events,
            Pipeline.Pipeline pipeline, CommandDispatcher dispatcher)
        {
            World = world;
            Rng = rng;
            Events = events;
            _pipeline = pipeline;
            _dispatcher = dispatcher;
        }

        public World World { get; }
        public Rng Rng { get; }
        public EventQueue Events { get; }
        public CommandLog CommandLog => _dispatcher.Log;

        /// <summary>The in-game day. Starts at 1, as day 0 is "before anything happened".</summary>
        public int Day { get; private set; } = 1;

        /// <summary>
        /// Starts a run. The pipeline is built by a callback because it usually contains a
        /// <see cref="CommandDrainSystem"/>, which needs the dispatcher this run owns — the
        /// alternative is making the caller construct and thread three objects in the right
        /// order every time, which is a rule to remember rather than a shape to follow.
        /// </summary>
        public static ReplayRun Start(
            ulong seed,
            IEnumerable<ICommandHandler> handlers,
            Func<CommandDispatcher, Pipeline.Pipeline> buildPipeline)
        {
            if (buildPipeline == null) throw new ArgumentNullException(nameof(buildPipeline));

            var events = new EventQueue();
            var dispatcher = new CommandDispatcher(events, handlers ?? Array.Empty<ICommandHandler>());

            Pipeline.Pipeline pipeline = buildPipeline(dispatcher)
                ?? throw new InvalidOperationException("buildPipeline returned null.");

            return new ReplayRun(new World(), new Rng(seed), events, pipeline, dispatcher);
        }

        /// <summary>A run whose pipeline needs nothing from the dispatcher.</summary>
        public static ReplayRun Start(ulong seed, Pipeline.Pipeline pipeline,
            IEnumerable<ICommandHandler> handlers = null)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));

            return Start(seed, handlers, _ => pipeline);
        }

        public void Submit(ICommand command) => _dispatcher.Enqueue(command);

        public void Submit(IEnumerable<ICommand> commands)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));

            foreach (ICommand command in commands) _dispatcher.Enqueue(command);
        }

        /// <summary>Runs one Tick phase at the fixed step.</summary>
        public void Tick(float dt) => _pipeline.Run(Phase.Tick, World, Context(dt));

        /// <summary>Runs one DayBoundary phase and advances the day.</summary>
        public void AdvanceDay()
        {
            _pipeline.Run(Phase.DayBoundary, World, Context(0f));
            Day++;
        }

        /// <summary>Runs <paramref name="days"/> whole days, each with a fixed number of ticks.</summary>
        public void Run(int days, int ticksPerDay = 0, float dt = 1f / 60f)
        {
            if (days < 0) throw new ArgumentOutOfRangeException(nameof(days));

            for (int day = 0; day < days; day++)
            {
                for (int tick = 0; tick < ticksPerDay; tick++) Tick(dt);
                AdvanceDay();
            }
        }

        /// <summary>
        /// Everything that must match between two runs of the same seed and command log: the
        /// world, the generator's position, the command log, and the causal record.
        /// </summary>
        /// <remarks>
        /// The last two are included deliberately. A run that reached the same world state by a
        /// different route — different commands rejected, different events emitted, a different
        /// number of draws taken — has diverged, even though the world alone would not say so.
        /// Catching that here is the difference between the gate proving determinism and it
        /// proving only that the end state happened to match.
        /// </remarks>
        public void WriteTo(IStateWriter writer)
        {
            writer.BeginSection("replay");
            writer.Write("day", Day);

            World.WriteTo(writer);

            writer.BeginSection("rng");
            RngState rng = Rng.Capture();
            writer.Write("seed", rng.Seed);
            writer.Write("stream", rng.Stream);
            writer.Write("position", rng.Position);
            writer.Write("draws", rng.Draws);
            writer.EndSection();

            writer.BeginSection("commands");
            writer.Write("count", CommandLog.Count);
            for (int i = 0; i < CommandLog.Count; i++)
            {
                CommandLogEntry entry = CommandLog[i];
                writer.BeginSection(i.ToString());
                writer.Write("node", entry.Node.Value);
                writer.Write("day", entry.Day);
                writer.Write("command", entry.Command?.ToString());
                writer.Write("applied", entry.Applied);
                writer.Write("rejected", entry.RejectedBecause);
                writer.EndSection();
            }

            writer.EndSection();

            writer.BeginSection("events");
            writer.Write("pending", Events.PendingCount);
            for (int i = 0; i < Events.PendingCount; i++)
            {
                Envelope envelope = Events.Pending[i];
                writer.BeginSection(i.ToString());
                writer.Write("id", envelope.Id.Value);
                writer.Write("cause", envelope.Cause.Value);
                writer.Write("day", envelope.Day);
                writer.Write("payload", envelope.PayloadType?.Name);
                writer.EndSection();
            }

            writer.EndSection();

            writer.EndSection();
        }

        /// <summary>The 64-bit digest of <see cref="WriteTo"/>.</summary>
        public string Digest()
        {
            var writer = new HashStateWriter();
            WriteTo(writer);
            return writer.Digest;
        }

        /// <summary>The diffable form of <see cref="WriteTo"/>, for when a digest mismatches.</summary>
        public string Dump()
        {
            var writer = new TextStateWriter();
            WriteTo(writer);
            return writer.ToString();
        }

        private Context Context(float dt) => new Context(Day, dt, Events, Rng);
    }
}
