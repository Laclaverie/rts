using System;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Engine.Commands
{
    /// <summary>
    /// Validates a command, then applies it. Handlers are the only things that mutate the
    /// world (ARCHITECTURE §6), and they are small (C9).
    /// </summary>
    public interface ICommandHandler
    {
        /// <summary>The exact command type this handles. One handler per type.</summary>
        Type CommandType { get; }

        /// <summary>
        /// Whether the command is legal against the current world. Must not mutate anything.
        /// </summary>
        /// <remarks>
        /// Rejection is normal, not exceptional: a player asking for something illegal is a
        /// Tuesday. It must also be <em>deterministic</em>, because replay re-runs validation
        /// against a reproduced world and has to reach the same verdict (§6.1).
        /// </remarks>
        bool Validate(ICommand command, World world, in Context ctx, out string reason);

        /// <summary>
        /// Applies the command. Called only after <see cref="Validate"/> returned true, and
        /// inside a cause scope, so anything emitted here is attributed to this command (§6.2).
        /// </summary>
        void Apply(ICommand command, World world, in Context ctx);
    }
}
