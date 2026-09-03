using System;
using RTS.Sim.Engine.Entities;

namespace RTS.Sim.Tests
{
    public class ComponentStoreTests
    {
        private struct Hp
        {
            public int Value;
        }

        private static EntityId Id(int v) => new EntityId(v);

        [Test]
        public void Set_then_TryGet_round_trips()
        {
            var store = new ComponentStore<Hp>();
            store.Set(Id(1), new Hp { Value = 42 });

            Assert.That(store.TryGet(Id(1), out Hp hp), Is.True);
            Assert.That(hp.Value, Is.EqualTo(42));
            Assert.That(store.Count, Is.EqualTo(1));
        }

        [Test]
        public void TryGet_on_absent_entity_is_false_and_default()
        {
            var store = new ComponentStore<Hp>();

            Assert.That(store.TryGet(Id(1), out Hp hp), Is.False);
            Assert.That(hp.Value, Is.EqualTo(0));
        }

        [Test]
        public void Set_twice_overwrites_in_place_and_keeps_position()
        {
            var store = new ComponentStore<Hp>();
            store.Set(Id(1), new Hp { Value = 1 });
            store.Set(Id(2), new Hp { Value = 2 });
            store.Set(Id(1), new Hp { Value = 99 });

            Assert.That(store.Count, Is.EqualTo(2));
            Assert.That(store.Ids.ToArray(), Is.EqualTo(new[] { Id(1), Id(2) }));
            Assert.That(store.Values[0].Value, Is.EqualTo(99));
        }

        [Test]
        public void Iteration_is_insertion_ordered_not_id_ordered()
        {
            var store = new ComponentStore<Hp>();
            foreach (int v in new[] { 30, 10, 20 })
                store.Set(Id(v), new Hp { Value = v });

            Assert.That(store.Ids.ToArray(), Is.EqualTo(new[] { Id(30), Id(10), Id(20) }));
        }

        [Test]
        public void Remove_from_the_middle_preserves_order_of_the_rest()
        {
            var store = new ComponentStore<Hp>();
            for (int i = 1; i <= 5; i++) store.Set(Id(i), new Hp { Value = i });

            Assert.That(store.Remove(Id(3)), Is.True);

            Assert.That(store.Ids.ToArray(), Is.EqualTo(new[] { Id(1), Id(2), Id(4), Id(5) }));
            Assert.That(store.Values.ToArray().Select(h => h.Value),
                Is.EqualTo(new[] { 1, 2, 4, 5 }));
        }

        [Test]
        public void Remove_reindexes_so_later_lookups_stay_correct()
        {
            var store = new ComponentStore<Hp>();
            for (int i = 1; i <= 4; i++) store.Set(Id(i), new Hp { Value = i * 10 });

            store.Remove(Id(1));

            Assert.That(store.TryGet(Id(4), out Hp hp), Is.True);
            Assert.That(hp.Value, Is.EqualTo(40));
            Assert.That(store.GetRef(Id(2)).Value, Is.EqualTo(20));
        }

        [Test]
        public void Remove_of_absent_entity_is_false()
        {
            var store = new ComponentStore<Hp>();
            store.Set(Id(1), new Hp());

            Assert.That(store.Remove(Id(2)), Is.False);
            Assert.That(store.Count, Is.EqualTo(1));
        }

        [Test]
        public void GetRef_mutates_in_place()
        {
            var store = new ComponentStore<Hp>();
            store.Set(Id(1), new Hp { Value = 5 });

            store.GetRef(Id(1)).Value = 7;

            store.TryGet(Id(1), out Hp hp);
            Assert.That(hp.Value, Is.EqualTo(7));
        }

        [Test]
        public void GetRef_on_absent_entity_throws()
        {
            var store = new ComponentStore<Hp>();
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => store.GetRef(Id(1)));
        }

        [Test]
        public void Growing_past_capacity_keeps_every_component_and_its_order()
        {
            var store = new ComponentStore<Hp>(capacity: 2);
            for (int i = 1; i <= 100; i++) store.Set(Id(i), new Hp { Value = i });

            Assert.That(store.Count, Is.EqualTo(100));
            Assert.That(store.Ids[0], Is.EqualTo(Id(1)));
            Assert.That(store.Ids[99], Is.EqualTo(Id(100)));
            Assert.That(store.TryGet(Id(57), out Hp hp), Is.True);
            Assert.That(hp.Value, Is.EqualTo(57));
        }

        [Test]
        public void None_cannot_own_a_component()
        {
            var store = new ComponentStore<Hp>();
            Assert.Throws<ArgumentException>(() => store.Set(EntityId.None, new Hp()));
        }

        [Test]
        public void Clear_empties_the_store()
        {
            var store = new ComponentStore<Hp>();
            store.Set(Id(1), new Hp());
            store.Clear();

            Assert.That(store.Count, Is.EqualTo(0));
            Assert.That(store.Has(Id(1)), Is.False);
            Assert.That(store.Ids.Length, Is.EqualTo(0));
        }
    }
}
