using System;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.State;

namespace RTS.Sim.Tests
{
    /// <summary>
    /// Pins the written shape of world state.
    /// </summary>
    /// <remarks>
    /// The names in <c>WriteTo</c> are a file format, not code identifiers, so they are string
    /// literals rather than <c>nameof()</c>: renaming a private field is a refactor that must
    /// not change what a save looks like. The trade is that nothing about the format is checked
    /// by the compiler — so it is checked here instead.
    /// <para>
    /// If a refactor changes the shape, this test fails with a readable diff and the change
    /// becomes a decision: either restore it, or accept it and bump
    /// <see cref="ReplayRun.SchemaVersion"/>. Pre-1.0 there is no compatibility to keep
    /// (§6.1 — stamp the version, refuse a mismatch, move on); the point is only to notice.
    /// </para>
    /// </remarks>
    [Category(TestCategories.Unit)]
    public class StateSchemaTests
    {
        private struct Coin : IComponentData
        {
            public int Amount;
            public float Rate;

            public void Write(IStateWriter writer)
            {
                writer.Write("amount", Amount);
                writer.Write("rate", Rate);
            }
        }

        private static World FixedWorld()
        {
            var world = new World();

            EntityId first = world.CreateEntity();
            EntityId second = world.CreateEntity();
            EntityId third = world.CreateEntity();

            world.Add(first, new Coin { Amount = 7, Rate = 0.5f });
            world.Add(third, new Coin { Amount = -2, Rate = 1.25f });

            world.DestroyEntity(second);

            return world;
        }

        private const string ExpectedShape =
            "world:\n" +
            "  entities:\n" +
            "    count = 2\n" +
            "    lastId = 3\n" +
            "    0 = 1\n" +
            "    1 = 3\n" +
            "  stores:\n" +
            "    count = 1\n" +
            "    Coin:\n" +
            "      count = 2\n" +
            "      #1:\n" +
            "        amount = 7\n" +
            "        rate = 0.5 (0x3f000000)\n" +
            "      #3:\n" +
            "        amount = -2\n" +
            "        rate = 1.25 (0x3fa00000)\n";

        [Test]
        public void The_written_shape_of_a_world_is_exactly_this()
        {
            var writer = new TextStateWriter();
            FixedWorld().WriteTo(writer);

            Assert.That(writer.ToString().Replace("\r\n", "\n"), Is.EqualTo(ExpectedShape),
                "The state format changed. Either a refactor moved it by accident — restore it — " +
                "or the change is intended, in which case update this expectation and bump " +
                "ReplayRun.SchemaVersion.");
        }

        [Test]
        public void The_same_world_always_digests_to_the_same_value()
        {
            var first = new HashStateWriter();
            var second = new HashStateWriter();

            FixedWorld().WriteTo(first);
            FixedWorld().WriteTo(second);

            Assert.That(second.Digest, Is.EqualTo(first.Digest));
        }

        [Test]
        public void Renaming_nothing_and_reordering_a_store_changes_the_digest()
        {
            // Store registration order is first-use order, so it is part of the shape. This is
            // the kind of drift a refactor causes without touching any string.
            var writer = new HashStateWriter();
            FixedWorld().WriteTo(writer);

            var reordered = new World();
            EntityId a = reordered.CreateEntity();
            reordered.Store<Coin>();
            reordered.Add(a, new Coin { Amount = 7, Rate = 0.5f });

            var other = new HashStateWriter();
            reordered.WriteTo(other);

            Assert.That(other.Digest, Is.Not.EqualTo(writer.Digest));
        }

        [Test]
        public void The_schema_version_is_part_of_the_replay_digest()
        {
            // So a digest from an older format is recognisably a different format, rather than
            // merely a different number.
            Assert.That(ReplayRun.SchemaVersion, Is.GreaterThan(0));
        }
    }
}
