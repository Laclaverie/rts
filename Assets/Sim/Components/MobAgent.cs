using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.State;

namespace RTS.Sim.Components
{
    /// <summary>Which way a body in the square is facing (GDD §5.2.2).</summary>
    public enum MobSide
    {
        /// <summary>Against you. Anonymous bodies, and any named crew who chose this.</summary>
        Rioter = 0,

        /// <summary>Standing between the crowd and the longhouse.</summary>
        Loyalist = 1,
    }

    /// <summary>
    /// One body in a revolt (GDD §5.2.2 rung 5, §6.4).
    /// </summary>
    /// <remarks>
    /// §5.2.2: "a mob is hundreds of anonymous bodies with a handful of named faces inside it."
    /// Both are this component. An anonymous body has no <see cref="Crew"/>; a named face
    /// carries the entity of the crew member who decided to stand there, so the two are the same
    /// crowd rather than two systems that happen to draw near each other.
    /// <para>
    /// <strong>Positions are offsets from the port, not places in the world.</strong> A revolt
    /// happens in one square, and the square is wherever that city is. Storing an offset means
    /// a mob does not have to be told where its city is, and moving a city on the map cannot
    /// leave its rioters standing in the sea.
    /// </para>
    /// <para>
    /// <see cref="PreviousX"/> and <see cref="PreviousY"/> are where the body stood at the start
    /// of the day. The sim moves in whole days, so without them the crowd would teleport once a
    /// day; with them a renderer can interpolate and the revolt reads as something happening
    /// rather than something reported.
    /// </para>
    /// </remarks>
    public struct MobAgent : IComponentData
    {
        public float X;
        public float Y;

        /// <summary>Where it stood when the day began, for a renderer to interpolate from.</summary>
        public float PreviousX;
        public float PreviousY;

        public MobSide Side;

        /// <summary>The named crew member this is, or <see cref="EntityId.None"/> for a body.</summary>
        public EntityId Crew;

        /// <remarks>
        /// The previous position is left out on purpose: it is last day's <see cref="X"/> and
        /// <see cref="Y"/> and nothing else, so writing it would double the size of a crowd in
        /// the digest without being able to disagree with what is already there.
        /// </remarks>
        public void Write(IStateWriter writer)
        {
            writer.Write("x", X);
            writer.Write("y", Y);
            writer.Write("side", (int)Side);
            writer.Write("crew", Crew.Value);
        }
    }
}
