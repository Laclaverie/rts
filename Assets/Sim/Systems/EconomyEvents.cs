using RTS.Sim.Engine.Entities;

namespace RTS.Sim.Systems
{
    /// <summary>
    /// What the economy systems report. Events say what was decided; they never decide (§7).
    /// </summary>
    /// <remarks>
    /// Deliberately few. An event per transaction would drown the causal DAG and the player
    /// feed in noise; these are the ones that begin a cascade or explain one afterwards
    /// (§5.2.3), which is exactly what "why did this port starve?" needs to answer.
    /// </remarks>
    public struct WagesPaid
    {
        /// <summary>Which city this happened to. One world holds several (§5.3).</summary>
        public EntityId Port;

        public int Coin;
        public int Crew;
    }

    /// <summary>The first link in the cascade: reserves out, wages unpaid (§5.2.3).</summary>
    public struct WagesUnpaid
    {
        /// <summary>Which city this happened to. One world holds several (§5.3).</summary>
        public EntityId Port;

        public int Owed;
        public int Paid;
        public int Crew;
    }

    public struct UpkeepPaid
    {
        /// <summary>Which city this happened to. One world holds several (§5.3).</summary>
        public EntityId Port;

        public int Coin;
        public int Buildings;
    }

    /// <summary>Upkeep unpayable, so buildings begin to decay and capacity falls.</summary>
    public struct UpkeepUnpaid
    {
        /// <summary>Which city this happened to. One world holds several (§5.3).</summary>
        public EntityId Port;

        public int Owed;
        public int Paid;
        public int Decayed;
    }

    /// <summary>Not enough food for everyone. Morale falls, and it is the morale floor (§5.3).</summary>
    public struct FoodShortfall
    {
        /// <summary>Which city this happened to. One world holds several (§5.3).</summary>
        public EntityId Port;

        public float Wanted;
        public float Eaten;
        public int Crew;
    }

    /// <summary>
    /// The town went short of food. Separate from <see cref="FoodShortfall"/>, which counts
    /// crew: the two strata are angered by their own hunger, not by each other's (§5.2.2).
    /// </summary>
    public struct CommonersWentHungry
    {
        /// <summary>Which city this happened to. One world holds several (§5.3).</summary>
        public EntityId Port;

        public int Commoners;
        public float Wanted;
        public float Eaten;

        /// <summary>How long this has been going on. People leave over a streak, not a day.</summary>
        public int ConsecutiveDays;
    }

    /// <summary>
    /// Commoners gave up on the port. The slow ending, as against a riot.
    /// </summary>
    public struct CommonersLeft
    {
        /// <summary>Which city this happened to. One world holds several (§5.3).</summary>
        public EntityId Port;

        public int Left;
        public int Remaining;
        public int HungryDays;
    }

    /// <summary>
    /// Food bought in because the port could not grow enough. The expensive way to survive a
    /// famine, and the reason a treasury is worth keeping.
    /// </summary>
    public struct GoodsBought
    {
        /// <summary>Which city this happened to. One world holds several (§5.3).</summary>
        public EntityId Port;

        public int Coin;
        public int Units;
        public string Good;
    }

    /// <summary>
    /// A building made less than it could have, because it ran short of what it eats.
    /// </summary>
    /// <remarks>
    /// The first symptom a player sees of a route they have not run yet, or one that did not
    /// arrive. Worth a line of its own rather than being inferred from a smaller number in a
    /// stock readout, because the cause is elsewhere and the readout does not say so.
    /// </remarks>
    public struct WorkshopShort
    {
        /// <summary>Which city this happened to. One world holds several (§5.3).</summary>
        public EntityId Port;

        public int DefinitionIndex;
        public float Wanted;
        public float Made;
    }

    /// <summary>Surplus sold to a passing merchant. The port's only income for now.</summary>
    public struct GoodsSold
    {
        /// <summary>Which city this happened to. One world holds several (§5.3).</summary>
        public EntityId Port;

        public int Coin;
        public int Units;
    }

    /// <summary>A building fell to zero condition and stopped producing.</summary>
    public struct BuildingDerelict
    {
        /// <summary>Which city this happened to. One world holds several (§5.3).</summary>
        public EntityId Port;

        public int DefinitionIndex;
    }
}
