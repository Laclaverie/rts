using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.State;

namespace RTS.Sim.Components
{
    /// <summary>
    /// Coin on hand. The port's reserves, and therefore the real resource (GDD §5.2.3).
    /// </summary>
    /// <remarks>
    /// Integer coin, not float. Money that accumulates rounding error is money that disagrees
    /// with itself across a replay, and every balance number in the game is quoted in whole
    /// coin anyway.
    /// </remarks>
    public struct Treasury : IComponentData
    {
        public int Coin;

        /// <summary>Coin owed but not paid, accumulated over the run. Never forgiven silently.</summary>
        public int Arrears;

        public void Write(IStateWriter writer)
        {
            writer.Write("coin", Coin);
            writer.Write("arrears", Arrears);
        }
    }
}
