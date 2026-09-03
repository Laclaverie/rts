namespace RTS.Sim.Engine.Pipeline
{
    /// <summary>
    /// The two cadences of the 20-minute day (ARCHITECTURE §4.1, GDD §5.1).
    /// </summary>
    public enum Phase
    {
        /// <summary>Fixed step, real time: movement, steering, combat resolution, mob flow-fields.</summary>
        Tick = 0,

        /// <summary>Once per in-game day: consumption, wages, upkeep, production, market, Heat, Unrest.</summary>
        DayBoundary = 1,
    }
}
