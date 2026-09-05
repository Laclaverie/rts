using RTS.Sim.Components;
using RTS.Sim.Engine.Entities;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// Builds the one port a unit-test world needs.
    /// </summary>
    /// <remarks>
    /// Most fixtures assemble a world by hand — a treasury, three crew, a farm — because that is
    /// far clearer than a scenario file when the test is about one system. Now that ownership is
    /// explicit, each of those entities has to say which port it belongs to, and a fixture that
    /// forgot would silently test nothing: the systems iterate ports, so an unowned entity is
    /// never reached.
    /// <para>
    /// That failure mode is the reason this is a helper rather than three lines copied into each
    /// fixture. A test that quietly exercises nothing still passes.
    /// </para>
    /// </remarks>
    internal static class TestPort
    {
        /// <summary>Creates the port every other entity in the fixture will belong to.</summary>
        public static EntityId Create(World world)
        {
            EntityId port = world.CreateEntity();
            world.Add(port, new PortState { DefinitionIndex = 0, IsPlayer = true });
            return port;
        }

        /// <summary>Hands an entity to a port, and returns it so calls can be chained.</summary>
        public static EntityId Own(World world, EntityId entity, EntityId port)
        {
            world.Add(entity, new Owner { Port = port });
            return entity;
        }
    }
}
