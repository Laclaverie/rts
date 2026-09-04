using System;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.State;

namespace RTS.Sim.Tests
{
    [Category(TestCategories.Unit)]
    public class ComponentStoreTests
    {
        private struct Hp : IComponentData
        {
            public int Value;

            public void Write(IStateWriter writer) => writer.Write("value", Value);
        }

        private static EntityId Id(int v) => new EntityId(v);

        [Test]
        public void Add_then_TryGet_round_trips()
        {
            var store = new ComponentStore<Hp>();
            store.Add(Id(1), new Hp { Value = 42 });

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
        public void Adding_the_same_component_twice_throws()
        {
            var store = new ComponentStore<Hp>();
            store.Add(Id(1), new Hp { Value = 1 });

            Assert.Throws<InvalidOperationException>(() => store.Add(Id(1), new Hp { Value = 99 }));
        }

        [Test]
        public void A_rejected_second_Add_leaves_the_store_untouched()
        {
            var store = new ComponentStore<Hp>();
            store.Add(Id(1), new Hp { Value = 1 });
            store.Add(Id(2), new Hp { Value = 2 });

            Assert.Throws<InvalidOperationException>(() => store.Add(Id(1), new Hp { Value = 99 }));

            Assert.That(store.Count, Is.EqualTo(2));
            Assert.That(store.Ids.ToArray(), Is.EqualTo(new[] { Id(1), Id(2) }));
            Assert.That(store.Values[0].Value, Is.EqualTo(1));
        }

        [Test]
        public void Remove_then_Add_reattaches_at_the_end()
        {
            var store = new ComponentStore<Hp>();
            for (int i = 1; i <= 3; i++) store.Add(Id(i), new Hp { Value = i });

            store.Remove(Id(1));
            store.Add(Id(1), new Hp { Value = 100 });

            Assert.That(store.Ids.ToArray(), Is.EqualTo(new[] { Id(2), Id(3), Id(1) }));
        }

        [Test]
        public void Iteration_is_insertion_ordered_not_id_ordered()
        {
            var store = new ComponentStore<Hp>();
            foreach (int v in new[] { 30, 10, 20 })
                store.Add(Id(v), new Hp { Value = v });

            Assert.That(store.Ids.ToArray(), Is.EqualTo(new[] { Id(30), Id(10), Id(20) }));
        }

        [Test]
        public void Remove_from_the_middle_preserves_order_of_the_rest()
        {
            var store = new ComponentStore<Hp>();
            for (int i = 1; i <= 5; i++) store.Add(Id(i), new Hp { Value = i });

            Assert.That(store.Remove(Id(3)), Is.True);

            Assert.That(store.Ids.ToArray(), Is.EqualTo(new[] { Id(1), Id(2), Id(4), Id(5) }));
            Assert.That(store.Values.ToArray().Select(h => h.Value),
                Is.EqualTo(new[] { 1, 2, 4, 5 }));
        }

        [Test]
        public void Remove_reindexes_so_later_lookups_stay_correct()
        {
            var store = new ComponentStore<Hp>();
            for (int i = 1; i <= 4; i++) store.Add(Id(i), new Hp { Value = i * 10 });

            store.Remove(Id(1));

            Assert.That(store.TryGet(Id(4), out Hp hp), Is.True);
            Assert.That(hp.Value, Is.EqualTo(40));
            Assert.That(store.GetRef(Id(2)).Value, Is.EqualTo(20));
        }

        [Test]
        public void Remove_of_absent_entity_is_false()
        {
            var store = new ComponentStore<Hp>();
            store.Add(Id(1), new Hp());

            Assert.That(store.Remove(Id(2)), Is.False);
            Assert.That(store.Count, Is.EqualTo(1));
        }

        [Test]
        public void GetRef_mutates_in_place()
        {
            var store = new ComponentStore<Hp>();
            store.Add(Id(1), new Hp { Value = 5 });

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
            for (int i = 1; i <= 100; i++) store.Add(Id(i), new Hp { Value = i });

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
            Assert.Throws<ArgumentException>(() => store.Add(EntityId.None, new Hp()));
        }

        [Test]
        public void Clear_empties_the_store()
        {
            var store = new ComponentStore<Hp>();
            store.Add(Id(1), new Hp());
            store.Clear();

            Assert.That(store.Count, Is.EqualTo(0));
            Assert.That(store.Has(Id(1)), Is.False);
            Assert.That(store.Ids.Length, Is.EqualTo(0));
        }
    }
}
