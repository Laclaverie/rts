using System;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Put a crew member to work somewhere, or take them off work (ARCHITECTURE §6).
    /// </summary>
    /// <remarks>
    /// §6 sketches this as <c>AssignCrew(EntityId Crew, JobId Job)</c>. There is no job system;
    /// a job is a building, so the second argument is the building they work. Idle is a real
    /// assignment rather than the absence of one, so <see cref="EntityId.None"/> is a legal
    /// target and means "take them off work".
    /// </remarks>
    public sealed class AssignCrew : ICommand
    {
        public AssignCrew(EntityId crew, EntityId building)
        {
            Crew = crew;
            Building = building;
        }

        public EntityId Crew { get; }

        /// <summary>Where they work. <see cref="EntityId.None"/> takes them off work.</summary>
        public EntityId Building { get; }

        public override string ToString() => $"AssignCrew({Crew} -> {Building})";
    }

    /// <summary>Validates and applies <see cref="AssignCrew"/>.</summary>
    public sealed class AssignCrewHandler : ICommandHandler
    {
        public Type CommandType => typeof(AssignCrew);

        public CommandRejection Validate(ICommand command, World world, in Context ctx)
        {
            var assign = (AssignCrew)command;
            BalanceTables balance = ctx.Balance;

            if (balance == null) return CommandRejection.Unavailable;

            if (!world.IsAlive(assign.Crew)) return CommandRejection.TargetGone;
            if (!world.Has<CrewMember>(assign.Crew)) return CommandRejection.InvalidTarget;

            bool alreadyThere = world.TryGet(assign.Crew, out Assignment current) &&
                                current.Building == assign.Building;

            if (alreadyThere) return CommandRejection.AlreadyInState;

            // Taking someone off work is always allowed. Somebody has to be able to stop.
            if (assign.Building.IsNone) return CommandRejection.None;

            if (!world.IsAlive(assign.Building)) return CommandRejection.TargetGone;
            if (!world.TryGet(assign.Building, out BuildingState state)) return CommandRejection.InvalidTarget;

            Building definition = balance.Buildings[state.DefinitionIndex];

            // Whether a building has work is data, not a hardcoded list of producers. When the
            // port buildings of §5.5 gain staffing needs, this rule follows them without being
            // touched.
            if (definition.Staff <= 0) return CommandRejection.NotPermitted;

            // Deliberately no check on how many are already there. Over-staffing is the player's
            // call: the extra effort is wasted, but keeping someone in reserve at a building is
            // a position, and it costs wages either way.
            return CommandRejection.None;
        }

        public void Apply(ICommand command, World world, in Context ctx)
        {
            var assign = (AssignCrew)command;

            // Set rather than Add: this is the same command whether they were idle or working
            // somewhere else, and the caller should not have to know which.
            world.Set(assign.Crew, new Assignment { Building = assign.Building });

            ctx.Events.Emit(new CrewAssigned { Crew = assign.Crew, Building = assign.Building });
        }
    }

    /// <summary>Someone changed jobs.</summary>
    public struct CrewAssigned
    {
        public EntityId Crew;
        public EntityId Building;
    }
}
