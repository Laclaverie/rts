using System;
using RTS.Sim.Engine.Events;

namespace RTS.Sim.Tests
{
    public class EventQueueTests
    {
        private struct PortStarved { public int Port; }
        private struct WagesPaid { public int Coins; }

        private static EventQueue Scoped(CauseId cause, int day = 1)
        {
            var queue = new EventQueue();
            queue.BeginCause(cause, day);
            return queue;
        }

        [Test]
        public void Emitting_stamps_the_open_cause_without_being_asked()
        {
            var cause = new CauseId(42);
            EventQueue queue = Scoped(cause, day: 7);

            queue.Emit(new PortStarved { Port = 3 });

            Envelope only = queue.Pending[0];
            Assert.That(only.Cause, Is.EqualTo(cause));
            Assert.That(only.Day, Is.EqualTo(7));
            Assert.That(only.TryGet(out PortStarved payload), Is.True);
            Assert.That(payload.Port, Is.EqualTo(3));
        }

        [Test]
        public void Emitting_outside_a_scope_throws_rather_than_guessing_a_cause()
        {
            var queue = new EventQueue();

            var e = Assert.Throws<InvalidOperationException>(() => queue.Emit(new WagesPaid()));

            Assert.That(e.Message, Does.Contain("outside a cause scope"));
        }

        [Test]
        public void Root_is_a_real_cause_not_a_missing_one()
        {
            // The day boundary arriving is a legitimate reason for something to happen.
            EventQueue queue = Scoped(CauseId.Root);

            Assert.DoesNotThrow(() => queue.Emit(new WagesPaid { Coins = 10 }));
            Assert.That(queue.Pending[0].Cause.IsRoot, Is.True);
        }

        [Test]
        public void Ids_are_unique_and_ascending_in_emission_order()
        {
            EventQueue queue = Scoped(CauseId.Root);

            EventId first = queue.Emit(new WagesPaid());
            EventId second = queue.Emit(new PortStarved());

            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(second.Value, Is.GreaterThan(first.Value));
            Assert.That(first.IsNone, Is.False, "an allocated id must never be None");
        }

        [Test]
        public void Order_is_emission_order_across_payload_types()
        {
            EventQueue queue = Scoped(CauseId.Root);

            queue.Emit(new WagesPaid { Coins = 1 });
            queue.Emit(new PortStarved { Port = 2 });
            queue.Emit(new WagesPaid { Coins = 3 });

            Assert.That(queue.Pending.Count, Is.EqualTo(3));
            Assert.That(queue.Pending[0].Is<WagesPaid>(), Is.True);
            Assert.That(queue.Pending[1].Is<PortStarved>(), Is.True);
            Assert.That(queue.Pending[2].Get<WagesPaid>().Coins, Is.EqualTo(3));
        }

        [Test]
        public void An_event_can_become_the_cause_of_the_next_one()
        {
            // The DAG edge §6.2 exists to record: this happened because that happened.
            EventQueue queue = Scoped(CauseId.Root);
            EventId first = queue.Emit(new WagesPaid());
            queue.EndCause();

            queue.BeginCause(first.AsCause(), day: 1);
            queue.Emit(new PortStarved());

            Assert.That(queue.Pending[1].Cause, Is.EqualTo(first.AsCause()));
            Assert.That(queue.Pending[1].Cause.Value, Is.EqualTo(first.Value));
        }

        [Test]
        public void The_innermost_scope_wins()
        {
            // The dispatcher is drained at a pipeline position, so applying a command happens
            // inside a phase that already has a cause. The event belongs to the command.
            var phase = CauseId.Root;
            var command = new CauseId(77);

            EventQueue queue = Scoped(phase, day: 3);
            queue.BeginCause(command, day: 3);
            queue.Emit(new WagesPaid());
            queue.EndCause();
            queue.Emit(new PortStarved());

            Assert.That(queue.Pending[0].Cause, Is.EqualTo(command));
            Assert.That(queue.Pending[1].Cause, Is.EqualTo(phase));
        }

        [Test]
        public void Ending_an_inner_scope_restores_the_outer_one()
        {
            EventQueue queue = Scoped(new CauseId(1), day: 5);
            queue.BeginCause(new CauseId(2), day: 9);

            Assert.That(queue.ScopeDepth, Is.EqualTo(2));
            Assert.That(queue.CurrentCause, Is.EqualTo(new CauseId(2)));
            Assert.That(queue.CurrentDay, Is.EqualTo(9));

            queue.EndCause();

            Assert.That(queue.ScopeDepth, Is.EqualTo(1));
            Assert.That(queue.CurrentCause, Is.EqualTo(new CauseId(1)));
            Assert.That(queue.CurrentDay, Is.EqualTo(5));
        }

        [Test]
        public void Ending_a_scope_that_was_never_opened_throws()
        {
            var queue = new EventQueue();

            Assert.Throws<InvalidOperationException>(() => queue.EndCause());
        }

        [Test]
        public void Ending_the_last_scope_leaves_nothing_open()
        {
            EventQueue queue = Scoped(new CauseId(9));
            queue.EndCause();

            Assert.That(queue.CurrentCause, Is.EqualTo(CauseId.Root));
            Assert.That(queue.InScope, Is.False);
            Assert.That(queue.ScopeDepth, Is.EqualTo(0));

            // And emitting is an error again, rather than silently attributing to Root.
            Assert.Throws<InvalidOperationException>(() => queue.Emit(new WagesPaid()));
        }

        [Test]
        public void Draining_hands_over_everything_and_empties_the_queue()
        {
            EventQueue queue = Scoped(CauseId.Root);
            queue.Emit(new WagesPaid());
            queue.Emit(new PortStarved());

            Envelope[] drained = queue.Drain();

            Assert.That(drained.Length, Is.EqualTo(2));
            Assert.That(queue.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Draining_returns_a_copy_so_a_reacting_subscriber_cannot_corrupt_it()
        {
            EventQueue queue = Scoped(CauseId.Root);
            queue.Emit(new WagesPaid());

            Envelope[] drained = queue.Drain();
            queue.Emit(new PortStarved());

            Assert.That(drained.Length, Is.EqualTo(1), "the drained batch must not grow underneath its reader");
            Assert.That(queue.PendingCount, Is.EqualTo(1));
        }

        [Test]
        public void Ids_keep_ascending_across_drains()
        {
            EventQueue queue = Scoped(CauseId.Root);
            EventId before = queue.Emit(new WagesPaid());
            queue.Drain();
            EventId after = queue.Emit(new PortStarved());

            Assert.That(after.Value, Is.GreaterThan(before.Value),
                "draining must not reset identity, or the DAG would grow duplicate node ids");
        }

        [Test]
        public void Allocated_command_ids_share_the_event_id_space()
        {
            // Commands and events are both DAG nodes. Two id spaces behind one CauseId would
            // silently link the wrong parent.
            EventQueue queue = Scoped(CauseId.Root);

            EventId command = queue.AllocateId();
            EventId evt = queue.Emit(new WagesPaid());

            Assert.That(evt.Value, Is.Not.EqualTo(command.Value));
            Assert.That(evt.Value, Is.GreaterThan(command.Value));
        }

        [Test]
        public void TryGet_of_the_wrong_type_is_false_and_Get_throws_naming_both()
        {
            EventQueue queue = Scoped(CauseId.Root);
            queue.Emit(new WagesPaid { Coins = 5 });

            Envelope only = queue.Pending[0];

            Assert.That(only.TryGet(out PortStarved _), Is.False);

            var e = Assert.Throws<InvalidOperationException>(() => only.Get<PortStarved>());
            Assert.That(e.Message, Does.Contain("WagesPaid"));
            Assert.That(e.Message, Does.Contain("PortStarved"));
        }

        [Test]
        public void Two_queues_fed_the_same_sequence_produce_identical_records()
        {
            // Determinism (§7.1): replay must reproduce the causal record exactly.
            Envelope[] Run()
            {
                EventQueue queue = Scoped(new CauseId(11), day: 4);
                queue.Emit(new WagesPaid { Coins = 2 });
                queue.Emit(new PortStarved { Port = 1 });
                return queue.Drain();
            }

            Envelope[] first = Run();
            Envelope[] second = Run();

            Assert.That(first.Length, Is.EqualTo(second.Length));
            for (int i = 0; i < first.Length; i++)
            {
                Assert.That(first[i].Id, Is.EqualTo(second[i].Id));
                Assert.That(first[i].Cause, Is.EqualTo(second[i].Cause));
                Assert.That(first[i].Day, Is.EqualTo(second[i].Day));
                Assert.That(first[i].PayloadType, Is.EqualTo(second[i].PayloadType));
            }
        }
    }
}
