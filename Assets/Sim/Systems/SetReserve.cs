using System;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Decide how much of a good to hold back from the passing merchant (GDD §5.5, §5.3).
    /// </summary>
    /// <remarks>
    /// <strong>The decision a warehouse exists for.</strong> Until now what a port kept was a
    /// number in <c>goods.csv</c> and the same for everybody: twenty food, ten iron, and nothing
    /// at all of rum or spice. That last part had a consequence nobody chose — the two goods the
    /// content says are worth crossing the sea for were sold the day they appeared and could
    /// never be held, shipped, or seen by anyone (§5.2.1's Heat included).
    /// <para>
    /// Holding stock back is income given up now for something later: grain against a bad
    /// harvest, iron for the workshop, rum for a route. It is also visible, so a fuller warehouse
    /// draws more attention. That is the same trade §5.2 makes everywhere — the counter to one
    /// pressure feeds another.
    /// </para>
    /// <para>
    /// What bounds it is <see cref="Port.Capacity"/>, from the <c>capacity</c> column that has
    /// been sitting in <c>buildings.csv</c> since Phase 1 doing nothing. A port with no warehouse
    /// can hold almost nothing and must sell as it produces.
    /// </para>
    /// </remarks>
    public sealed class SetReserve : ICommand
    {
        public SetReserve(int goodIndex, float units)
        {
            GoodIndex = goodIndex;
            Units = units;
        }

        public int GoodIndex { get; }

        /// <summary>How much to hold back. Zero sells everything the merchant will take.</summary>
        public float Units { get; }

        public override string ToString() => $"SetReserve({GoodIndex}, {Units:0.#})";
    }

    /// <summary>Changes what the player's port holds back.</summary>
    public sealed class SetReserveHandler : ICommandHandler
    {
        public Type CommandType => typeof(SetReserve);

        public CommandRejection Validate(ICommand command, World world, in Context ctx)
        {
            var set = (SetReserve)command;
            BalanceTables balance = ctx.Balance;

            if (balance == null) return CommandRejection.Unavailable;
            if (set.Units < 0f) return CommandRejection.InvalidTarget;
            if (set.GoodIndex < 0 || set.GoodIndex >= balance.Goods.Count)
                return CommandRejection.InvalidTarget;

            EntityId port = Port.Player(world);
            if (!world.Has<PortState>(port)) return CommandRejection.Unavailable;

            float current = Port.ReserveOf(world, port, set.GoodIndex);
            if (Math.Abs(current - set.Units) < 0.001f) return CommandRejection.AlreadyInState;

            // Everything reserved has to fit in the sheds. Checked across all goods rather than
            // per pile, because one warehouse holds whatever you put in it and filling it with
            // grain is a decision not to fill it with iron.
            float wanted = Port.Reserved(world, port) - current + set.Units;
            if (wanted > Port.Capacity(world, port, balance)) return CommandRejection.NotYet;

            return CommandRejection.None;
        }

        public void Apply(ICommand command, World world, in Context ctx)
        {
            var set = (SetReserve)command;
            EntityId port = Port.Player(world);

            float before = Port.ReserveOf(world, port, set.GoodIndex);
            Port.SetReserve(world, port, set.GoodIndex, set.Units);

            ctx.Events.Emit(new ReserveChanged
            {
                Port = port,
                GoodIndex = set.GoodIndex,
                From = before,
                To = set.Units,
            });
        }
    }

    /// <summary>The port changed its mind about what to keep.</summary>
    public struct ReserveChanged
    {
        public EntityId Port;
        public int GoodIndex;
        public float From;
        public float To;
    }

    /// <summary>Goods that will not fit are sold, whatever the port would rather do.</summary>
    public struct StorageOverflowed
    {
        public EntityId Port;
        public int GoodIndex;
        public float Units;
        public int Coin;
    }
}
