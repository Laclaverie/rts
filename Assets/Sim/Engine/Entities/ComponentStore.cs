using System;
using System.Collections.Generic;

namespace RTS.Sim.Engine.Entities
{
    /// <summary>
    /// Non-generic handle so <see cref="World"/> can hold stores of mixed component types
    /// in one ordered list. Only the operations that do not need to know T live here.
    /// </summary>
    public interface IComponentStore
    {
        int Count { get; }
        bool Has(EntityId id);
        bool Remove(EntityId id);
        void Clear();
    }

    /// <summary>
    /// Dense storage for one component type, with insertion-ordered iteration
    /// (ARCHITECTURE §3, §7.1). Components live in packed arrays; the dictionary is a
    /// lookup index only and is never iterated, because dictionary order is not
    /// deterministic and §7.1 forbids state-affecting iteration over it.
    /// </summary>
    /// <remarks>
    /// Removal shifts the tail down to preserve insertion order, which is O(n). That is
    /// deliberate: ARCHITECTURE §3 caps this at dozens of named agents plus a few hundred
    /// mobs, and a swap-remove would trade determinism for a speed-up nothing needs.
    /// </remarks>
    public sealed class ComponentStore<T> : IComponentStore where T : struct
    {
        private const int DefaultCapacity = 16;

        private EntityId[] _ids;
        private T[] _values;
        private int _count;

        // EntityId -> index into the dense arrays. Lookup only; never enumerated.
        private readonly Dictionary<EntityId, int> _indexOf;

        public ComponentStore(int capacity = DefaultCapacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));

            _ids = new EntityId[capacity];
            _values = new T[capacity];
            _indexOf = new Dictionary<EntityId, int>(capacity);
        }

        public int Count => _count;

        /// <summary>Components in insertion order. Index i pairs with <see cref="Ids"/>[i].</summary>
        public ReadOnlySpan<T> Values => new ReadOnlySpan<T>(_values, 0, _count);

        /// <summary>Owners in insertion order. Index i pairs with <see cref="Values"/>[i].</summary>
        public ReadOnlySpan<EntityId> Ids => new ReadOnlySpan<EntityId>(_ids, 0, _count);

        public bool Has(EntityId id) => _indexOf.ContainsKey(id);

        public bool TryGet(EntityId id, out T value)
        {
            if (_indexOf.TryGetValue(id, out int index))
            {
                value = _values[index];
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>
        /// Attaches the component. Throws if the entity already has one.
        /// </summary>
        /// <remarks>
        /// Deliberately strict: attaching twice usually means two systems each believe they
        /// own this component, and that is a bug worth hearing about. Updating an existing
        /// component is <see cref="GetRef"/>, which is a different operation with different
        /// risks. An upsert ("ensure this component equals this value") is the natural shape
        /// for idempotent command handlers, but nothing needs one yet — it can be added
        /// alongside the first handler that does, since loosening this later breaks nothing
        /// while tightening it would.
        /// </remarks>
        public void Add(EntityId id, in T value)
        {
            if (id.IsNone) throw new ArgumentException("EntityId.None cannot own a component.", nameof(id));

            if (_indexOf.ContainsKey(id))
                throw new InvalidOperationException($"{id} already has a {typeof(T).Name}. Use GetRef to update it.");

            if (_count == _ids.Length) Grow();

            _ids[_count] = id;
            _values[_count] = value;
            _indexOf[id] = _count;
            _count++;
        }

        /// <summary>
        /// Mutable access to a stored component, so callers can update a field without
        /// copying the struct out and back. Throws if the entity has no component.
        /// </summary>
        public ref T GetRef(EntityId id)
        {
            if (!_indexOf.TryGetValue(id, out int index))
                throw new KeyNotFoundException($"{id} has no {typeof(T).Name}.");

            return ref _values[index];
        }

        /// <summary>Removes the component if present. Returns whether anything was removed.</summary>
        public bool Remove(EntityId id)
        {
            if (!_indexOf.TryGetValue(id, out int index)) return false;

            // Shift the tail down by one. Preserves insertion order (§7.1); a swap-remove
            // would not.
            int tail = _count - 1;
            for (int i = index; i < tail; i++)
            {
                _ids[i] = _ids[i + 1];
                _values[i] = _values[i + 1];
                _indexOf[_ids[i]] = i;
            }

            _ids[tail] = default;
            _values[tail] = default;
            _count = tail;
            _indexOf.Remove(id);

            return true;
        }

        public void Clear()
        {
            Array.Clear(_ids, 0, _count);
            Array.Clear(_values, 0, _count);
            _count = 0;
            _indexOf.Clear();
        }

        private void Grow()
        {
            int capacity = _ids.Length == 0 ? DefaultCapacity : _ids.Length * 2;
            Array.Resize(ref _ids, capacity);
            Array.Resize(ref _values, capacity);
        }
    }
}
