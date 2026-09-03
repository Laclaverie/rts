using System;

namespace RTS.Sim.Engine.Entities
{
    /// <summary>
    /// An entity is nothing but an id (ARCHITECTURE C2, §3). No base class, no inheritance,
    /// no data on the entity itself — data lives in <see cref="ComponentStore{T}"/>.
    /// </summary>
    /// <remarks>
    /// ARCHITECTURE §3 writes this as `readonly record struct`. That is C# 10 and Unity 6.3
    /// compiles with -langversion:9.0, so the equality members are spelled out by hand.
    /// </remarks>
    public readonly struct EntityId : IEquatable<EntityId>
    {
        /// <summary>Reserved: no entity. Stores never hold this id.</summary>
        public static readonly EntityId None = default;

        public readonly int Value;

        public EntityId(int value) => Value = value;

        public bool IsNone => Value == 0;

        public bool Equals(EntityId other) => Value == other.Value;

        public override bool Equals(object obj) => obj is EntityId other && Equals(other);

        public override int GetHashCode() => Value;

        public static bool operator ==(EntityId a, EntityId b) => a.Value == b.Value;

        public static bool operator !=(EntityId a, EntityId b) => a.Value != b.Value;

        public override string ToString() => IsNone ? "EntityId.None" : "#" + Value;
    }
}
