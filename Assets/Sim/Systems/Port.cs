using System;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// Helpers every economy system needs: find a port, its treasury, its pile of a given good.
    /// </summary>
    /// <remarks>
    /// Not a system and not state — just the lookups, in one place so a dozen systems do not
    /// each invent their own and drift apart.
    /// <para>
    /// Everything takes the port it applies to. Phase 1 had one port, so "the treasury" was
    /// unambiguous and every one of these read <c>Values[0]</c>; §5.3's trade needs cities that
    /// differ, so the world holds several and the ambiguity is real. The systems did not change
    /// shape when it arrived, which is what the original comment here predicted.
    /// </para>
    /// </remarks>
    public static class Port
    {
        /// <summary>Every port, in creation order. Iteration order is part of determinism.</summary>
        public static ReadOnlySpan<EntityId> All(World world) => world.Store<PortState>().Ids;

        /// <summary>How many ports the world holds.</summary>
        public static int Count(World world) => world.Store<PortState>().Count;

        /// <summary>
        /// The port the player runs.
        /// </summary>
        /// <remarks>
        /// Throws if there is none. Content validation already refuses a world without exactly
        /// one, so reaching here without one is a composition error rather than a game state.
        /// </remarks>
        public static EntityId Player(World world)
        {
            ComponentStore<PortState> ports = world.Store<PortState>();

            for (int i = 0; i < ports.Count; i++)
                if (ports.Values[i].IsPlayer) return ports.Ids[i];

            throw new InvalidOperationException("No port is marked as the player's.");
        }

        /// <summary>Which port owns this, or <see cref="EntityId.None"/>.</summary>
        public static EntityId OwnerOf(World world, EntityId entity) =>
            world.TryGet(entity, out Owner owner) ? owner.Port : EntityId.None;

        /// <summary>Whether this entity belongs to that port.</summary>
        public static bool BelongsTo(World world, EntityId entity, EntityId port) =>
            world.TryGet(entity, out Owner owner) && owner.Port == port;

        /// <summary>
        /// One port's treasury. Throws if it has none: a day boundary running against a port
        /// with no treasury is a composition error, not a game state.
        /// </summary>
        public static ref Treasury Treasury(World world, EntityId port)
        {
            ComponentStore<Treasury> store = world.Store<Treasury>();

            for (int i = 0; i < store.Count; i++)
            {
                if (BelongsTo(world, store.Ids[i], port)) return ref store.GetRef(store.Ids[i]);
            }

            throw new InvalidOperationException(
                $"Port {port.Value} has no Treasury. Nothing can be paid.");
        }

        public static bool HasTreasury(World world, EntityId port)
        {
            ComponentStore<Treasury> store = world.Store<Treasury>();

            for (int i = 0; i < store.Count; i++)
                if (BelongsTo(world, store.Ids[i], port)) return true;

            return false;
        }

        /// <summary>
        /// One port's pile of a good, created empty if it does not exist yet. Returns the entity
        /// so the caller can take a ref without a second lookup.
        /// </summary>
        public static EntityId StockPile(World world, EntityId port, int goodIndex)
        {
            ComponentStore<Stock> stocks = world.Store<Stock>();

            for (int i = 0; i < stocks.Count; i++)
            {
                if (stocks.Values[i].GoodIndex != goodIndex) continue;
                if (!BelongsTo(world, stocks.Ids[i], port)) continue;

                return stocks.Ids[i];
            }

            EntityId created = world.CreateEntity();
            world.Add(created, new Stock { GoodIndex = goodIndex, Units = 0f });
            world.Add(created, new Owner { Port = port });
            return created;
        }

        /// <summary>How much of a good this port holds back from the passing merchant.</summary>
        public static float ReserveOf(World world, EntityId port, int goodIndex)
        {
            ComponentStore<Stock> stocks = world.Store<Stock>();

            for (int i = 0; i < stocks.Count; i++)
            {
                if (stocks.Values[i].GoodIndex != goodIndex) continue;
                if (!BelongsTo(world, stocks.Ids[i], port)) continue;

                return stocks.Values[i].Reserve;
            }

            return 0f;
        }

        /// <summary>Sets what a port holds back. Creates the pile if it does not exist yet.</summary>
        public static void SetReserve(World world, EntityId port, int goodIndex, float units)
        {
            EntityId pile = StockPile(world, port, goodIndex);
            world.Store<Stock>().GetRef(pile).Reserve = units < 0f ? 0f : units;
        }

        /// <summary>What this port has reserved across every good.</summary>
        public static float Reserved(World world, EntityId port)
        {
            ComponentStore<Stock> stocks = world.Store<Stock>();
            float total = 0f;

            for (int i = 0; i < stocks.Count; i++)
                if (BelongsTo(world, stocks.Ids[i], port)) total += stocks.Values[i].Reserve;

            return total;
        }

        /// <summary>
        /// How much this port can store, from the buildings that store things (GDD §5.5).
        /// </summary>
        /// <remarks>
        /// The <c>capacity</c> column has been in <c>buildings.csv</c> since Phase 1 and did
        /// nothing at all until now. A shut warehouse holds nothing: mothballing one is a way of
        /// saving upkeep, and it should cost what it saves.
        /// </remarks>
        public static float Capacity(World world, EntityId port, BalanceTables balance)
        {
            if (balance == null) return 0f;

            ComponentStore<BuildingState> buildings = world.Store<BuildingState>();
            float total = 0f;

            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingState state = buildings.Values[i];
                if (state.Mothballed) continue;
                if (!BelongsTo(world, buildings.Ids[i], port)) continue;
                if (state.DefinitionIndex < 0 || state.DefinitionIndex >= balance.Buildings.Count)
                    continue;

                total += balance.Buildings[state.DefinitionIndex].Capacity;
            }

            return total;
        }

        public static float UnitsOf(World world, EntityId port, int goodIndex)
        {
            ComponentStore<Stock> stocks = world.Store<Stock>();

            for (int i = 0; i < stocks.Count; i++)
            {
                if (stocks.Values[i].GoodIndex != goodIndex) continue;
                if (!BelongsTo(world, stocks.Ids[i], port)) continue;

                return stocks.Values[i].Units;
            }

            return 0f;
        }

        /// <summary>Adds to a pile, creating it if needed. Negative amounts remove.</summary>
        public static void Add(World world, EntityId port, int goodIndex, float units)
        {
            EntityId pile = StockPile(world, port, goodIndex);
            ref Stock stock = ref world.Store<Stock>().GetRef(pile);

            stock.Units += units;

            // A negative pile is not a debt, it is a bug. Callers take what is available and
            // report the shortfall themselves.
            if (stock.Units < 0f) stock.Units = 0f;
        }

        /// <summary>
        /// Takes up to <paramref name="wanted"/> units, returning how much was actually taken.
        /// </summary>
        public static float Take(World world, EntityId port, int goodIndex, float wanted)
        {
            if (wanted <= 0f) return 0f;

            EntityId pile = StockPile(world, port, goodIndex);
            ref Stock stock = ref world.Store<Stock>().GetRef(pile);

            float taken = wanted <= stock.Units ? wanted : stock.Units;
            stock.Units -= taken;
            return taken;
        }
    }
}
