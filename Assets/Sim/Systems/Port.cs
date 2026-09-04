using System;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Helpers every economy system needs: find the treasury, find a pile of a given good.
    /// </summary>
    /// <remarks>
    /// Not a system and not state — just the lookups, in one place so four systems do not each
    /// invent their own and drift apart. Phase 1 has one port, so "the treasury" is unambiguous;
    /// when there are several, these take a port id and the systems do not change shape.
    /// </remarks>
    public static class Port
    {
        /// <summary>
        /// The one treasury. Throws if there is none: a day boundary running against a world
        /// with no treasury is a composition error, not a game state.
        /// </summary>
        public static ref Treasury Treasury(World world)
        {
            ComponentStore<Treasury> store = world.Store<Treasury>();

            if (store.Count == 0)
                throw new InvalidOperationException("The world has no Treasury. Nothing can be paid.");

            return ref store.GetRef(store.Ids[0]);
        }

        public static bool HasTreasury(World world) => world.Store<Treasury>().Count > 0;

        /// <summary>
        /// The pile of one good, creating it empty if it does not exist yet. Returns the entity
        /// so the caller can take a ref without a second lookup.
        /// </summary>
        public static EntityId StockPile(World world, int goodIndex)
        {
            ComponentStore<Stock> stocks = world.Store<Stock>();

            for (int i = 0; i < stocks.Count; i++)
            {
                if (stocks.Values[i].GoodIndex == goodIndex) return stocks.Ids[i];
            }

            EntityId created = world.CreateEntity();
            world.Add(created, new Stock { GoodIndex = goodIndex, Units = 0f });
            return created;
        }

        public static float UnitsOf(World world, int goodIndex)
        {
            ComponentStore<Stock> stocks = world.Store<Stock>();

            for (int i = 0; i < stocks.Count; i++)
            {
                if (stocks.Values[i].GoodIndex == goodIndex) return stocks.Values[i].Units;
            }

            return 0f;
        }

        /// <summary>Adds to a pile, creating it if needed. Negative amounts remove.</summary>
        public static void Add(World world, int goodIndex, float units)
        {
            EntityId pile = StockPile(world, goodIndex);
            ref Stock stock = ref world.Store<Stock>().GetRef(pile);

            stock.Units += units;

            // A negative pile is not a debt, it is a bug. Callers take what is available and
            // report the shortfall themselves.
            if (stock.Units < 0f) stock.Units = 0f;
        }

        /// <summary>
        /// Takes up to <paramref name="wanted"/> units, returning how much was actually taken.
        /// </summary>
        public static float Take(World world, int goodIndex, float wanted)
        {
            if (wanted <= 0f) return 0f;

            EntityId pile = StockPile(world, goodIndex);
            ref Stock stock = ref world.Store<Stock>().GetRef(pile);

            float taken = wanted <= stock.Units ? wanted : stock.Units;
            stock.Units -= taken;
            return taken;
        }
    }
}
