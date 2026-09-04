using System;
using RTS.Sim.Components;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Shut a building down, or open it again (GDD §5.2.3).
    /// </summary>
    /// <remarks>
    /// The first exit §5.2.3 lists from the spiral: "Demolish or mothball buildings — permanent
    /// capacity loss; sunk investment gone." A mothballed building costs no upkeep and produces
    /// nothing, so this is how a port that cannot pay its bills stops the bleeding.
    /// <para>
    /// "Deliberate downsizing must be a viable, respected strategy — cutting your port down to
    /// survive a bad season should feel like competent play, not like losing slowly." That is
    /// only true if the command exists, which until now it did not.
    /// </para>
    /// </remarks>
    public sealed class MothballBuilding : ICommand
    {
        public MothballBuilding(EntityId building, bool mothballed)
        {
            Building = building;
            Mothballed = mothballed;
        }

        public EntityId Building { get; }

        /// <summary>True to shut it down, false to open it again.</summary>
        public bool Mothballed { get; }

        public override string ToString() =>
            $"{(Mothballed ? "Mothball" : "Reopen")}({Building})";
    }

    /// <summary>Applies <see cref="MothballBuilding"/>.</summary>
    public sealed class MothballBuildingHandler : ICommandHandler
    {
        public Type CommandType => typeof(MothballBuilding);

        public CommandRejection Validate(ICommand command, World world, in Context ctx)
        {
            var mothball = (MothballBuilding)command;

            if (!world.IsAlive(mothball.Building)) return CommandRejection.TargetGone;
            if (!world.TryGet(mothball.Building, out BuildingState state))
                return CommandRejection.InvalidTarget;

            if (state.Mothballed == mothball.Mothballed) return CommandRejection.AlreadyInState;

            return CommandRejection.None;
        }

        public void Apply(ICommand command, World world, in Context ctx)
        {
            var mothball = (MothballBuilding)command;

            ref BuildingState state = ref world.Store<BuildingState>().GetRef(mothball.Building);
            state.Mothballed = mothball.Mothballed;

            int released = 0;

            if (mothball.Mothballed)
            {
                // Shutting a building frees whoever worked it. Leaving them assigned to a place
                // that produces nothing would be a silent waste — they would still eat and still
                // draw wages while the port thought it had put them somewhere useful.
                ComponentStore<Assignment> assignments = world.Store<Assignment>();

                for (int i = 0; i < assignments.Count; i++)
                {
                    if (assignments.Values[i].Building != mothball.Building) continue;

                    assignments.GetRef(assignments.Ids[i]).Building = EntityId.None;
                    released++;
                }
            }

            ctx.Events.Emit(new BuildingMothballed
            {
                Building = mothball.Building,
                Mothballed = mothball.Mothballed,
                CrewReleased = released,
            });
        }
    }

    /// <summary>A building was shut down or reopened.</summary>
    public struct BuildingMothballed
    {
        public EntityId Building;
        public bool Mothballed;
        public int CrewReleased;
    }
}
