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
        public int Coin;
        public int Crew;
    }

    /// <summary>The first link in the cascade: reserves out, wages unpaid (§5.2.3).</summary>
    public struct WagesUnpaid
    {
        public int Owed;
        public int Paid;
        public int Crew;
    }

    public struct UpkeepPaid
    {
        public int Coin;
        public int Buildings;
    }

    /// <summary>Upkeep unpayable, so buildings begin to decay and capacity falls.</summary>
    public struct UpkeepUnpaid
    {
        public int Owed;
        public int Paid;
        public int Decayed;
    }

    /// <summary>Not enough food for everyone. Morale falls, and it is the morale floor (§5.3).</summary>
    public struct FoodShortfall
    {
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
        public int Coin;
        public int Units;
        public string Good;
    }

    /// <summary>Surplus sold to a passing merchant. The port's only income for now.</summary>
    public struct GoodsSold
    {
        public int Coin;
        public int Units;
    }

    /// <summary>A building fell to zero condition and stopped producing.</summary>
    public struct BuildingDerelict
    {
        public int DefinitionIndex;
    }
}
