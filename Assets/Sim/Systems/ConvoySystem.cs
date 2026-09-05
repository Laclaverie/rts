using System;
using System.Collections.Generic;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Sails every convoy one day nearer, and lands the ones that arrive (GDD P1, §5.1).
    /// </summary>
    /// <remarks>
    /// Runs first at the day boundary, before anything eats or works. Bread that arrives this
    /// morning should be edible this morning — a convoy that landed and then watched the city
    /// starve because the order was wrong would be the kind of bug nobody finds for a month.
    /// <para>
    /// Convoys are destroyed on arrival rather than kept as history. The command log already
    /// records what was sent and the feed records what landed (§6.1, §6.2); a graveyard of
    /// delivered convoys would be a third copy of the same fact, and one that grows forever.
    /// </para>
    /// </remarks>
    public sealed class ConvoySystem : ISystem
    {
        public const string SystemId = "Convoy";

        public string Id => SystemId;

        public void Run(World world, in Context ctx)
        {
            ComponentStore<Convoy> convoys = world.Store<Convoy>();
            if (convoys.Count == 0) return;

            // Collected before landing any of them: arriving destroys entities, and the store
            // shifts under an index-based loop when it happens.
            var landed = new List<EntityId>();

            for (int i = 0; i < convoys.Count; i++)
            {
                ref Convoy convoy = ref convoys.GetRef(convoys.Ids[i]);

                if (convoy.DaysRemaining > 0) convoy.DaysRemaining--;
                if (convoy.DaysRemaining <= 0) landed.Add(convoys.Ids[i]);
            }

            for (int i = 0; i < landed.Count; i++) Land(world, landed[i], ctx);
        }

        private static void Land(World world, EntityId id, in Context ctx)
        {
            Convoy convoy = world.Store<Convoy>().GetRef(id);

            // The cargo becomes the destination's, which is the whole journey's point.
            if (convoy.Units > 0f && world.IsAlive(convoy.Destination))
                Port.Add(world, convoy.Destination, convoy.GoodIndex, convoy.Units);

            // A sale is paid when it lands. A purchase paid on dispatch and carries nothing.
            if (convoy.CoinOnArrival > 0 && world.IsAlive(convoy.Origin) &&
                Port.HasTreasury(world, convoy.Origin))
            {
                Port.Treasury(world, convoy.Origin).Coin += convoy.CoinOnArrival;
            }

            ctx.Events.Emit(new ConvoyArrived
            {
                Port = convoy.Destination,
                Origin = convoy.Origin,
                GoodIndex = convoy.GoodIndex,
                Units = convoy.Units,
                Coin = convoy.CoinOnArrival,
            });

            world.DestroyEntity(id);
        }

        /// <summary>
        /// Puts a convoy on the water.
        /// </summary>
        /// <remarks>
        /// Owned by whoever bears the risk: the buyer for a purchase they have already paid for,
        /// the seller for goods not yet paid. That is who a raid takes from, and who the feed
        /// should tell.
        /// </remarks>
        public static EntityId Dispatch(World world, BalanceTables balance, EntityId origin,
            EntityId destination, int goodIndex, float units, int coinOnArrival, EntityId owner,
            in Context ctx)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            int days = DaysBetween(world, balance, origin, destination);

            EntityId id = world.CreateEntity();
            world.Add(id, new Convoy
            {
                Origin = origin,
                Destination = destination,
                GoodIndex = goodIndex,
                Units = units,
                CoinOnArrival = coinOnArrival,
                DaysRemaining = days,
                TotalDays = days,
            });
            world.Add(id, new Owner { Port = owner });

            ctx.Events.Emit(new ConvoyDispatched
            {
                Port = owner,
                Destination = destination,
                GoodIndex = goodIndex,
                Units = units,
                Days = days,
            });

            return id;
        }

        /// <summary>
        /// How long the crossing takes, from where the two cities are.
        /// </summary>
        /// <remarks>
        /// Never written down as a day count anywhere. A hand-written number would be a second
        /// source of truth that disagrees with the map the moment one exists, and the distances
        /// are already in <c>ports.csv</c> for exactly this.
        /// </remarks>
        public static int DaysBetween(World world, BalanceTables balance, EntityId from,
            EntityId to)
        {
            PortDefinition a = DefinitionOf(world, balance, from);
            PortDefinition b = DefinitionOf(world, balance, to);

            if (a == null || b == null) return 1;

            return WorldScenario.TravelDays(a, b);
        }

        private static PortDefinition DefinitionOf(World world, BalanceTables balance, EntityId port)
        {
            if (balance == null || !world.TryGet(port, out PortState state)) return null;
            if (state.DefinitionIndex < 0 || state.DefinitionIndex >= balance.Ports.Count) return null;

            return balance.Ports[state.DefinitionIndex];
        }
    }

    /// <summary>A convoy set out. What it carries is no longer in anybody's warehouse.</summary>
    public struct ConvoyDispatched
    {
        /// <summary>Whose convoy it is — the city bearing the risk.</summary>
        public EntityId Port;

        public EntityId Destination;
        public int GoodIndex;
        public float Units;
        public int Days;
    }

    /// <summary>A convoy landed. The cargo is real again.</summary>
    public struct ConvoyArrived
    {
        /// <summary>Where it landed.</summary>
        public EntityId Port;

        public EntityId Origin;
        public int GoodIndex;
        public float Units;
        public int Coin;
    }
}
