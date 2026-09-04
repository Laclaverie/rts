using System;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>Kinds of bad day (GDD §5.2.3).</summary>
    public enum ShockKind
    {
        None = 0,

        /// <summary>Stored food lost: spoilage, a bad harvest, weevils.</summary>
        HarvestFailure = 1,

        /// <summary>Buildings damaged. Condition falls across the port.</summary>
        Storm = 2,

        /// <summary>Coin taken. Reserves gone without warning.</summary>
        Theft = 3,

        /// <summary>Crew leave. Labour, and the output that depended on it, gone.</summary>
        Desertion = 4,
    }

    /// <summary>
    /// A bad event, injected as a command.
    /// </summary>
    /// <remarks>
    /// A command rather than a test helper on purpose. It goes through the dispatcher, lands in
    /// the command log, and is attributed in the causal DAG, so a cascade scenario is literally
    /// a seed plus a command log — the same thing a save is (§6.1) and the same thing a
    /// functional test is (§8.2). A shock applied by reaching into the world would be none of
    /// those, and the scenario could not be replayed or shipped in a bug report.
    /// </remarks>
    public sealed class Shock : ICommand
    {
        public Shock(ShockKind kind, float magnitude)
        {
            Kind = kind;
            Magnitude = magnitude;
        }

        public ShockKind Kind { get; }

        /// <summary>
        /// Meaning depends on the kind: units of food, condition lost per building, coin taken,
        /// or crew lost.
        /// </summary>
        public float Magnitude { get; }

        public override string ToString() => $"Shock({Kind})";
    }

    /// <summary>Applies a <see cref="Shock"/>.</summary>
    public sealed class ShockHandler : ICommandHandler
    {
        public Type CommandType => typeof(Shock);

        public CommandRejection Validate(ICommand command, World world, in Context ctx)
        {
            var shock = (Shock)command;

            if (shock.Kind == ShockKind.None) return CommandRejection.NotPermitted;
            if (shock.Magnitude <= 0f) return CommandRejection.OutOfRange;
            if (ctx.Balance == null) return CommandRejection.Unavailable;

            return CommandRejection.None;
        }

        public void Apply(ICommand command, World world, in Context ctx)
        {
            var shock = (Shock)command;

            switch (shock.Kind)
            {
                case ShockKind.HarvestFailure:
                    ApplyHarvestFailure(world, ctx.Balance, shock.Magnitude);
                    break;

                case ShockKind.Storm:
                    ApplyStorm(world, shock.Magnitude);
                    break;

                case ShockKind.Theft:
                    ApplyTheft(world, shock.Magnitude);
                    break;

                case ShockKind.Desertion:
                    ApplyDesertion(world, shock.Magnitude);
                    break;
            }

            ctx.Events.Emit(new ShockStruck { Kind = shock.Kind, Magnitude = shock.Magnitude });
        }

        private static void ApplyHarvestFailure(World world, BalanceTables balance, float units)
        {
            int food = ConsumptionSystem.IndexOf(balance, "food");
            if (food >= 0) Port.Take(world, food, units);
        }

        private static void ApplyStorm(World world, float conditionLost)
        {
            ComponentStore<BuildingState> buildings = world.Store<BuildingState>();

            for (int i = 0; i < buildings.Count; i++)
            {
                ref BuildingState state = ref buildings.GetRef(buildings.Ids[i]);
                if (state.Mothballed) continue;

                state.Condition = ConsumptionSystem.Clamp01(state.Condition - conditionLost);
            }
        }

        private static void ApplyTheft(World world, float coin)
        {
            if (!Port.HasTreasury(world)) return;

            ref Treasury treasury = ref Port.Treasury(world);
            int taken = (int)coin;

            treasury.Coin -= taken <= treasury.Coin ? taken : treasury.Coin;
        }

        private static void ApplyDesertion(World world, float count)
        {
            ComponentStore<CrewMember> crew = world.Store<CrewMember>();
            int leaving = (int)count;

            // From the end, so the ids that remain are the ones that were there first — a
            // deterministic choice, and the same one every replay makes.
            for (int i = 0; i < leaving && crew.Count > 0; i++)
                world.DestroyEntity(crew.Ids[crew.Count - 1]);
        }
    }

    /// <summary>Something bad happened. What it was, and how hard.</summary>
    public struct ShockStruck
    {
        public ShockKind Kind;
        public float Magnitude;
    }
}
