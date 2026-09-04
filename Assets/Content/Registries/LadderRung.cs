namespace RTS.Content.Registries
{
    /// <summary>
    /// The revolution ladder (GDD §5.2.2). Escalation is a ladder, not a spawn table.
    /// </summary>
    /// <remarks>
    /// Every rung is visible and every rung has an exit. That is what makes revolution the
    /// flagship emergent event rather than a timer wearing a costume: it can be climbed by
    /// mismanagement and descended by fixing what caused it, and a player can see which rung
    /// they are on and what would move them.
    /// </remarks>
    public enum LadderRung
    {
        /// <summary>Nothing is wrong that anybody is talking about.</summary>
        Calm = 0,

        /// <summary>Rumours surface in the Tavern.</summary>
        Grumbling = 1,

        /// <summary>Work is done late, badly, or not at all.</summary>
        Slowdown = 2,

        /// <summary>A named figure emerges with a specific, stated demand.</summary>
        Agitator = 3,

        /// <summary>Localised violence. Property damage; warehouses are a target.</summary>
        Riot = 4,

        /// <summary>A mob. Named crew choose sides individually, by loyalty.</summary>
        Uprising = 5,

        /// <summary>Failure. The port is no longer yours.</summary>
        Deposition = 6,
    }
}
