namespace RTS.Content.Registries
{
    /// <summary>
    /// The three groups whose patience runs out separately (GDD §5.2.2).
    /// </summary>
    /// <remarks>
    /// Population is not one number. A port can be feeding its commoners while its crew go
    /// unpaid, and the two produce different trouble — which is what makes unrest a state
    /// machine fed by the economy rather than a meter.
    /// </remarks>
    public enum Stratum
    {
        /// <summary>Want food, work, safety. Angered by hunger, unemployment, repression.</summary>
        Commoners = 0,

        /// <summary>Want pay, rest, respect. Angered by unpaid wages, low morale, losses.</summary>
        NamedCrew = 1,

        /// <summary>Want open routes and low tax. Angered by tariffs, blockades, seizures.</summary>
        Merchants = 2,
    }
}
