using System;
using System.Collections.Generic;
using RTS.Content.Loading;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Events;
using RTS.Sim.Engine.Pipeline;

namespace RTS.Sim.Tests
{
    [Category(TestCategories.Unit)]
    public class PipelineTests
    {
        /// <summary>Records the order in which it ran, which is all these tests need.</summary>
        private sealed class Spy : ISystem
        {
            private readonly List<string> _log;

            public Spy(string id, List<string> log)
            {
                Id = id;
                _log = log;
            }

            public string Id { get; }

            public int Runs { get; private set; }

            public void Run(World world, in Context ctx)
            {
                Runs++;
                _log.Add(Id);
            }
        }

        private static CsvTable Table(string body) =>
            CsvTable.Parse("phase,order,system,enabled\n" + body, "pipeline.csv");

        private static Context AnyContext() =>
            new Context(day: 1, dt: 0f, events: new EventQueue());

        [Test]
        public void Runs_systems_in_declared_order_not_registration_order()
        {
            var log = new List<string>();
            var table = Table("DayBoundary,30,Upkeep,true\nDayBoundary,10,Consumption,true\nDayBoundary,20,Wages,true\n");

            // Registered in a different order on purpose.
            Pipeline pipeline = Pipeline.Build(table, new ISystem[]
            {
                new Spy("Wages", log), new Spy("Upkeep", log), new Spy("Consumption", log),
            });

            pipeline.Run(Phase.DayBoundary, new World(), AnyContext());

            Assert.That(log, Is.EqualTo(new[] { "Consumption", "Wages", "Upkeep" }));
        }

        [Test]
        public void Phases_are_independent()
        {
            var log = new List<string>();
            var table = Table("Tick,10,Movement,true\nDayBoundary,10,Wages,true\n");

            Pipeline pipeline = Pipeline.Build(table, new ISystem[]
            {
                new Spy("Movement", log), new Spy("Wages", log),
            });

            pipeline.Run(Phase.Tick, new World(), AnyContext());

            Assert.That(log, Is.EqualTo(new[] { "Movement" }));
            Assert.That(pipeline.Systems(Phase.DayBoundary).Count, Is.EqualTo(1));
        }

        [Test]
        public void A_disabled_system_is_bound_but_never_runs()
        {
            var log = new List<string>();
            var table = Table("Tick,10,Movement,false\nTick,20,Combat,true\n");

            Pipeline pipeline = Pipeline.Build(table, new ISystem[]
            {
                new Spy("Movement", log), new Spy("Combat", log),
            });

            pipeline.Run(Phase.Tick, new World(), AnyContext());

            Assert.That(log, Is.EqualTo(new[] { "Combat" }));
        }

        [Test]
        public void A_system_declared_but_not_implemented_is_loud()
        {
            var table = Table("Tick,10,Movement,true\nTick,20,Ghost,true\n");

            var e = Assert.Throws<PipelineConfigurationException>(
                () => Pipeline.Build(table, new ISystem[] { new Spy("Movement", new List<string>()) }));

            Assert.That(e.Message, Does.Contain("'Ghost' is declared but no system implements it"));
            Assert.That(e.Message, Does.Contain("pipeline.csv(3)"));
        }

        [Test]
        public void A_system_implemented_but_not_declared_is_loud()
        {
            var log = new List<string>();
            var table = Table("Tick,10,Movement,true\n");

            var e = Assert.Throws<PipelineConfigurationException>(
                () => Pipeline.Build(table, new ISystem[] { new Spy("Movement", log), new Spy("Forgotten", log) }));

            Assert.That(e.Message, Does.Contain("'Forgotten' is implemented but missing from pipeline.csv"));
        }

        [Test]
        public void Every_problem_is_reported_at_once_not_just_the_first()
        {
            var log = new List<string>();
            var table = Table("Tick,10,Ghost,true\nNoSuchPhase,20,Movement,true\n");

            var e = Assert.Throws<PipelineConfigurationException>(
                () => Pipeline.Build(table, new ISystem[] { new Spy("Movement", log), new Spy("Orphan", log) }));

            Assert.That(e.Problems.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(e.Message, Does.Contain("Ghost"));
            Assert.That(e.Message, Does.Contain("unknown phase 'NoSuchPhase'"));
            Assert.That(e.Message, Does.Contain("Orphan"));
        }

        [Test]
        public void Two_systems_claiming_one_slot_is_rejected_as_ambiguous_order()
        {
            var log = new List<string>();
            var table = Table("Tick,10,Movement,true\nTick,10,Combat,true\n");

            var e = Assert.Throws<PipelineConfigurationException>(
                () => Pipeline.Build(table, new ISystem[] { new Spy("Movement", log), new Spy("Combat", log) }));

            Assert.That(e.Message, Does.Contain("both claim Tick order 10"));
        }

        [Test]
        public void The_same_order_in_different_phases_is_fine()
        {
            var log = new List<string>();
            var table = Table("Tick,10,Movement,true\nDayBoundary,10,Wages,true\n");

            Assert.DoesNotThrow(() => Pipeline.Build(table, new ISystem[]
            {
                new Spy("Movement", log), new Spy("Wages", log),
            }));
        }

        [Test]
        public void A_system_declared_twice_is_rejected()
        {
            var log = new List<string>();
            var table = Table("Tick,10,Movement,true\nTick,20,Movement,true\n");

            var e = Assert.Throws<PipelineConfigurationException>(
                () => Pipeline.Build(table, new ISystem[] { new Spy("Movement", log) }));

            Assert.That(e.Message, Does.Contain("already declared on line 2"));
        }

        [Test]
        public void Two_registered_systems_sharing_an_id_are_rejected()
        {
            var log = new List<string>();
            var table = Table("Tick,10,Movement,true\n");

            var e = Assert.Throws<PipelineConfigurationException>(
                () => Pipeline.Build(table, new ISystem[] { new Spy("Movement", log), new Spy("Movement", log) }));

            Assert.That(e.Message, Does.Contain("share the Id 'Movement'"));
        }

        [Test]
        public void An_empty_pipeline_with_no_systems_is_valid()
        {
            Pipeline pipeline = Pipeline.Build(Table(string.Empty), new ISystem[0]);

            Assert.That(pipeline.Systems(Phase.Tick), Is.Empty);
            Assert.DoesNotThrow(() => pipeline.Run(Phase.Tick, new World(), AnyContext()));
        }

        [Test]
        public void Phase_names_are_case_sensitive()
        {
            var log = new List<string>();
            var table = Table("tick,10,Movement,true\n");

            var e = Assert.Throws<PipelineConfigurationException>(
                () => Pipeline.Build(table, new ISystem[] { new Spy("Movement", log) }));

            Assert.That(e.Message, Does.Contain("unknown phase 'tick'"));
        }

        [TestCase("Tick,DayBoundary", TestName = "a comma-separated list")]
        [TestCase("1", TestName = "the numeric value of a phase")]
        [TestCase("0", TestName = "zero")]
        [TestCase("TICK", TestName = "the wrong case")]
        [TestCase("Tick DayBoundary", TestName = "two names run together")]
        public void Phase_must_be_an_exact_name(string phaseText)
        {
            var log = new List<string>();
            var table = Table("\"" + phaseText + "\",10,Movement,true\n");

            var e = Assert.Throws<PipelineConfigurationException>(
                () => Pipeline.Build(table, new ISystem[] { new Spy("Movement", log) }));

            Assert.That(e.Message, Does.Contain("unknown phase"));
        }

        [Test]
        public void Padding_around_a_phase_name_is_tolerated()
        {
            // A stray space in a hand-edited balance file should not be a launch failure.
            // Fields are trimmed, so this is the one liberty taken with the phase column.
            var log = new List<string>();
            var table = Table("  Tick  ,10,Movement,true\n");

            Assert.DoesNotThrow(() => Pipeline.Build(table, new ISystem[] { new Spy("Movement", log) }));
        }

        /// <summary>A system that reports something, which is what systems do (§7).</summary>
        private sealed class Emitter : ISystem
        {
            private readonly bool _throws;

            public Emitter(string id, bool throws = false)
            {
                Id = id;
                _throws = throws;
            }

            public string Id { get; }

            public void Run(World world, in Context ctx)
            {
                ctx.Events.Emit(new Reported { By = 1 });
                if (_throws) throw new InvalidOperationException("system blew up");
            }
        }

        private struct Reported { public int By; }

        [Test]
        public void A_system_can_emit_without_knowing_anything_about_causes()
        {
            var events = new EventQueue();
            Pipeline pipeline = Pipeline.Build(Table("DayBoundary,10,Wages,true\n"),
                new ISystem[] { new Emitter("Wages") });

            pipeline.Run(Phase.DayBoundary, new World(), new Context(day: 4, dt: 0f, events: events));

            Assert.That(events.PendingCount, Is.EqualTo(1));
            Assert.That(events.Pending[0].Cause.IsRoot, Is.True, "the phase itself is the cause");
            Assert.That(events.Pending[0].Day, Is.EqualTo(4));
        }

        [Test]
        public void The_cause_scope_is_closed_even_when_a_system_throws()
        {
            // An unbalanced stack would attribute the next phase's events to a finished cause.
            var events = new EventQueue();
            Pipeline pipeline = Pipeline.Build(Table("Tick,10,Boom,true\n"),
                new ISystem[] { new Emitter("Boom", throws: true) });

            Assert.Throws<InvalidOperationException>(
                () => pipeline.Run(Phase.Tick, new World(), new Context(1, 0f, events)));

            Assert.That(events.ScopeDepth, Is.EqualTo(0));
            Assert.That(events.InScope, Is.False);
        }

        [Test]
        public void Running_a_phase_twice_runs_each_system_twice()
        {
            var log = new List<string>();
            var spy = new Spy("Movement", log);
            Pipeline pipeline = Pipeline.Build(Table("Tick,10,Movement,true\n"), new ISystem[] { spy });

            var world = new World();
            pipeline.Run(Phase.Tick, world, AnyContext());
            pipeline.Run(Phase.Tick, world, AnyContext());

            Assert.That(spy.Runs, Is.EqualTo(2));
        }
    }
}
