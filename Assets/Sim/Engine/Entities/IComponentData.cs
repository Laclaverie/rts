using RTS.Sim.Engine.State;

namespace RTS.Sim.Engine.Entities
{
    /// <summary>
    /// A component must be able to write itself out. Required, not optional.
    /// </summary>
    /// <remarks>
    /// The replay-determinism gate compares end states, and §6.1's snapshots serialise them,
    /// so a component the writer cannot see is a hole in both. Making this optional would mean
    /// a component that forgot to implement it is silently excluded from the comparison — the
    /// gate would stay green while the sim diverged, which is worse than having no gate.
    /// <para>
    /// The cost is real: every component spells out its fields. It is the same work snapshots
    /// need anyway, done once, and being explicit rather than reflective means field order and
    /// float formatting are decisions rather than accidents.
    /// </para>
    /// <para>
    /// Being a constraint on a struct, the call is made without boxing.
    /// </para>
    /// </remarks>
    public interface IComponentData
    {
        void Write(IStateWriter writer);
    }
}
