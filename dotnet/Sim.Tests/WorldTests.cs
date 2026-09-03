using System;
using RTS.Sim.Engine;

namespace RTS.Sim.Tests
{
    public class WorldTests
    {
        private struct Hp { public int Value; }
        private struct Tag { public int Kind; }

        [Test]
        public void Ids_are_allocated_in_a_deterministic_sequence()
        {
            var a = new World();
            var b = new World();

            for (int i = 0; i < 5; i++)
                Assert.That(a.CreateEntity(), Is.EqualTo(b.CreateEntity()));
        }

        [Test]
        public void First_id_is_not_None()
        {
            var world = new World();
            Assert.That(world.CreateEntity(), Is.Not.EqualTo(EntityId.None));
        }

        [Test]
        public void Destroyed_ids_are_never_reused()
        {
            var world = new World();
            EntityId first = world.CreateEntity();
            world.DestroyEntity(first);

            Assert.That(world.CreateEntity(), Is.Not.EqualTo(first));
        }

        [Test]
        public void Destroy_drops_components_from_every_store()
        {
            var world = new World();
            EntityId e = world.CreateEntity();
            world.Set(e, new Hp { Value = 3 });
            world.Set(e, new Tag { Kind = 1 });

            Assert.That(world.DestroyEntity(e), Is.True);

            Assert.That(world.Has<Hp>(e), Is.False);
            Assert.That(world.Has<Tag>(e), Is.False);
            Assert.That(world.IsAlive(e), Is.False);
            Assert.That(world.EntityCount, Is.EqualTo(0));
        }

        [Test]
        public void Destroying_twice_is_false_the_second_time()
        {
            var world = new World();
            EntityId e = world.CreateEntity();

            Assert.That(world.DestroyEntity(e), Is.True);
            Assert.That(world.DestroyEntity(e), Is.False);
        }

        [Test]
        public void Entities_are_listed_in_creation_order()
        {
            var world = new World();
            EntityId a = world.CreateEntity();
            EntityId b = world.CreateEntity();
            EntityId c = world.CreateEntity();

            world.DestroyEntity(b);

            Assert.That(world.Entities, Is.EqualTo(new[] { a, c }));
        }

        [Test]
        public void Store_returns_the_same_instance_per_type()
        {
            var world = new World();
            ComponentStore<Hp> first = world.Store<Hp>();

            Assert.That(world.Store<Hp>(), Is.SameAs(first));
            Assert.That((object)world.Store<Tag>(), Is.Not.SameAs(world.Store<Hp>()));
        }

        [Test]
        public void Components_cannot_be_attached_to_a_dead_entity()
        {
            var world = new World();
            EntityId e = world.CreateEntity();
            world.DestroyEntity(e);

            Assert.Throws<ArgumentException>(() => world.Set(e, new Hp()));
        }

        [Test]
        public void TryGet_round_trips_through_the_world()
        {
            var world = new World();
            EntityId e = world.CreateEntity();
            world.Set(e, new Hp { Value = 11 });

            Assert.That(world.TryGet(e, out Hp hp), Is.True);
            Assert.That(hp.Value, Is.EqualTo(11));
            Assert.That(world.Remove<Hp>(e), Is.True);
            Assert.That(world.TryGet(e, out Hp _), Is.False);
        }
    }
}
