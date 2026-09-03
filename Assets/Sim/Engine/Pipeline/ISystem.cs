namespace RTS.Sim.Engine.Pipeline
{
    using RTS.Sim.Engine.Entities;

    /// <summary>
    /// A system is a function over the world. Nothing more (ARCHITECTURE §4).
    /// </summary>
    public interface ISystem
    {
        /// <summary>
        /// Matches the `system` column of pipeline.csv. This string is the contract between
        /// code and the order file, so it is stable: renaming it is a data migration.
        /// </summary>
        string Id { get; }

        void Run(World world, in Context ctx);
    }
}
