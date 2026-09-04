using System;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Engine.Commands
{
    /// <summary>
    /// Drains queued commands at a declared position in the pipeline (ARCHITECTURE §6:
    /// "queued, drained at a defined pipeline position, never applied mid-system").
    /// </summary>
    /// <remarks>
    /// Being an ordinary <see cref="ISystem"/> is the point: <em>when</em> input takes effect
    /// relative to production, wages and unrest is an ordering decision, and §4.2 says
    /// ordering decisions live in pipeline.csv where a designer can see and change them —
    /// not buried in a game loop.
    /// </remarks>
    public sealed class CommandDrainSystem : ISystem
    {
        /// <summary>The id to put in pipeline.csv.</summary>
        public const string SystemId = "CommandDrain";

        private readonly CommandDispatcher _dispatcher;

        public CommandDrainSystem(CommandDispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public string Id => SystemId;

        /// <summary>Commands applied by the most recent run. Diagnostics only.</summary>
        public CommandDispatcher.DrainResult LastResult { get; private set; }

        public void Run(World world, in Context ctx)
        {
            LastResult = _dispatcher.Drain(world, in ctx);
        }
    }
}
