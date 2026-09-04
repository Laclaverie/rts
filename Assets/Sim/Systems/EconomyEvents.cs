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

    /// <summary>A building fell to zero condition and stopped producing.</summary>
    public struct BuildingDerelict
    {
        public int DefinitionIndex;
    }
}
