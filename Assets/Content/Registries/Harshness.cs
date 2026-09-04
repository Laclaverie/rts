namespace RTS.Content.Registries
{
    /// <summary>How hard a riot is put down (GDD §5.2.2, §6).</summary>
    /// <remarks>
    /// Every step buys more quiet today and costs more permanently. There is no option here
    /// that is simply better than the others, which is the point: repression is "a viable
    /// strategy, not a free one".
    /// </remarks>
    public enum Harshness
    {
        /// <summary>Show of force, few heads broken. Buys least, costs least.</summary>
        Restrained = 0,

        /// <summary>Real violence, and everyone knows someone it happened to.</summary>
        Firm = 1,

        /// <summary>Made an example. The port remembers it for good.</summary>
        Brutal = 2,
    }
}
