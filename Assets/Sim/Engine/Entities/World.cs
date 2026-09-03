using System;
using System.Collections.Generic;

namespace RTS.Sim.Engine.Entities
{
    /// <summary>
    /// Owns entity identity and the component stores. Entities are ids; behaviour lives in
    /// systems, never on the data (ARCHITECTURE C2, §3).
    /// </summary>
    /// <remarks>
    /// ARCHITECTURE §3 sketches World with named stores — Positions, Morale, Loyalty. Those
    /// are game concepts, and BUILD_ORDER Phase 0 admits none. So the store set is generic
    /// and registered on demand here; Phase 1 introduces the named components on top of it
    /// without changing this type.
    /// </remarks>
    public sealed class World
    {
        // Registration order, so DestroyEntity visits stores deterministically. The
        // dictionary is a lookup index only and is never enumerated (§7.1).
        private readonly List<IComponentStore> _stores = new List<IComponentStore>();
        private readonly Dictionary<Type, IComponentStore> _storesByType =
            new Dictionary<Type, IComponentStore>();

        private int _lastEntityId;

        /// <summary>Entities created and not yet destroyed, in creation order.</summary>
        private readonly List<EntityId> _living = new List<EntityId>();
        private readonly HashSet<EntityId> _livingSet = new HashSet<EntityId>();

        public int EntityCount => _living.Count;

        /// <summary>Living entities in creation order. Safe to iterate for state changes (§7.1).</summary>
        public IReadOnlyList<EntityId> Entities => _living;

        /// <summary>
        /// Allocates the next id. Ids come from a counter rather than a free list, so the
        /// same command sequence always produces the same ids — the basis of replay (§7.1).
        /// Destroyed ids are never reused, which also makes a stale reference detectable
        /// rather than silently pointing at a different entity.
        /// </summary>
        public EntityId CreateEntity()
        {
            if (_lastEntityId == int.MaxValue)
                throw new InvalidOperationException("EntityId space exhausted.");

            var id = new EntityId(++_lastEntityId);
            _living.Add(id);
            _livingSet.Add(id);
            return id;
        }

        public bool IsAlive(EntityId id) => _livingSet.Contains(id);

        /// <summary>
        /// Destroys the entity and drops its components from every store. Returns whether
        /// the entity was alive.
        /// </summary>
        public bool DestroyEntity(EntityId id)
        {
            if (!_livingSet.Remove(id)) return false;

            _living.Remove(id);

            for (int i = 0; i < _stores.Count; i++)
                _stores[i].Remove(id);

            return true;
        }

        /// <summary>
        /// The store for <typeparamref name="T"/>, created on first use. Registration order
        /// therefore follows first use, which is deterministic for a given code path.
        /// </summary>
        public ComponentStore<T> Store<T>() where T : struct
        {
            if (_storesByType.TryGetValue(typeof(T), out IComponentStore existing))
                return (ComponentStore<T>)existing;

            var store = new ComponentStore<T>();
            _storesByType[typeof(T)] = store;
            _stores.Add(store);
            return store;
        }

        public bool TryGet<T>(EntityId id, out T value) where T : struct =>
            Store<T>().TryGet(id, out value);

        public void Set<T>(EntityId id, in T value) where T : struct
        {
            if (!IsAlive(id))
                throw new ArgumentException($"{id} is not alive.", nameof(id));

            Store<T>().Set(id, value);
        }

        public bool Remove<T>(EntityId id) where T : struct => Store<T>().Remove(id);

        public bool Has<T>(EntityId id) where T : struct => Store<T>().Has(id);
    }
}
