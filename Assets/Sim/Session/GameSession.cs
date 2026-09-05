using System;
using System.Collections.Generic;
using RTS.Content.Registries;
using RTS.Sim.Components;
using RTS.Sim.Engine.Commands;
using RTS.Sim.Engine.Entities;
using RTS.Sim.Engine.Pipeline;
using RTS.Sim.Engine.State;
using RTS.Sim.Engine.Time;
using RTS.Sim.Scenarios;
using RTS.Sim.Systems;

namespace RTS.Sim.Session
{
    /// <summary>
    /// A playable game: a world, a clock, and the commands a player can issue.
    /// </summary>
    /// <remarks>
    /// <strong>This is the game. Unity is a renderer.</strong> Nothing here references
    /// UnityEngine, and the assembly it lives in cannot — <c>Sim.asmdef</c> sets
    /// <c>noEngineReferences</c>, so an accidental <c>using UnityEngine</c> is a compile error
    /// rather than a discipline problem (ARCHITECTURE C5, §2).
    /// <para>
    /// The reason is not purity. An engine is a dependency that will one day need upgrading on
    /// somebody else's schedule — a security fix, a dropped platform, a licence change — and the
    /// cost of that upgrade should be proportional to how much of the game lives inside it.
    /// Here it is the scene bootstrap and the panel binder, both of which are small enough to
    /// rewrite in an afternoon. Everything that took real thought is on the other side of this
    /// line, compiled and tested by a plain <c>dotnet test</c> that never launches an editor.
    /// </para>
    /// <para>
    /// A front end has to do four things: advance time, read state, issue commands, and see what
    /// happened. All four are here, so a console, a test, or a different engine can drive the
    /// game without knowing anything Unity-shaped.
    /// </para>
    /// </remarks>
    public sealed class GameSession
    {
        private readonly List<Readout> _readouts = new List<Readout>();
        private readonly List<PlayerAction> _actions = new List<PlayerAction>();
        private readonly ICommandHandler[] _handlers;

        private GameSession(ReplayRun run, Clock clock, BalanceTables balance,
            ICommandHandler[] handlers)
        {
            Run = run;
            Clock = clock;
            Balance = balance;
            _handlers = handlers;
        }

        /// <summary>What has happened, for a player who looked away (§5.1).</summary>
        public EventFeed Feed { get; } = new EventFeed();

        public ReplayRun Run { get; }

        public Clock Clock { get; }

        public BalanceTables Balance { get; }

        public World World => Run.World;

        /// <summary>
        /// The city the player runs. Everything the panel shows and every order it offers is
        /// scoped to this one; the others live by the same rules, unwatched (§5.2.2).
        /// </summary>
        public EntityId PlayerPort => Port.Player(World);

        /// <summary>The in-game day. Starts at 1.</summary>
        public int Day => Run.Day;

        /// <summary>Days that have passed since the front end last asked. For animation.</summary>
        public int DaysLastAdvanced { get; private set; }

        /// <summary>
        /// Starts a session on the shipped content.
        /// </summary>
        /// <param name="pipelineCsv">The text of <c>pipeline.csv</c>, read by the caller.</param>
        /// <remarks>
        /// Text rather than a path, because <c>Sim</c> has no business knowing where files live
        /// — under Unity that is StreamingAssets, in a test it is the build output, and neither
        /// should leak in here (§5.2).
        /// </remarks>
        public static GameSession Start(BalanceTables balance, Clock clock, string pipelineCsv,
            World world = null, ulong seed = 1)
        {
            if (balance == null) throw new ArgumentNullException(nameof(balance));
            if (clock == null) throw new ArgumentNullException(nameof(clock));
            if (pipelineCsv == null) throw new ArgumentNullException(nameof(pipelineCsv));

            // The whole map by default. A caller with its own world — a test exercising one
            // city, a scenario replaying a recorded run — passes it instead.
            world = world ?? WorldScenario.FromContent(balance);

            ICommandHandler[] handlers = PlayerCommands();

            ReplayRun run = ReplayRun.Start(
                seed,
                handlers,
                dispatcher => ScenarioRunner.BuildPipeline(pipelineCsv, dispatcher),
                world,
                balance);

            return new GameSession(run, clock, balance, handlers);
        }

        /// <summary>
        /// Every lever a player has. The same list the scenario runner uses, because a scenario
        /// is a recording of a session and the two must accept the same commands (§6.1).
        /// </summary>
        public static ICommandHandler[] PlayerCommands() => new ICommandHandler[]
        {
            new ShockHandler(),
            new SuppressRiotHandler(),
            new AssignCrewHandler(),
            new MothballBuildingHandler(),
        };

        /// <summary>
        /// Hands the clock real time and runs whatever whole days come back.
        /// </summary>
        /// <remarks>
        /// The only place real time touches the game, and it touches it as an integer. What the
        /// front end does with frames, vsync or a stalled breakpoint stays on the front end's
        /// side of this call — which is why a session played at ×4 and a headless replay of its
        /// command log reach the same digest.
        /// </remarks>
        public int Advance(float realSeconds)
        {
            int days = Clock.Advance(realSeconds);

            for (int i = 0; i < days; i++) RunOneDay();

            DaysLastAdvanced = days;
            return days;
        }

        /// <summary>Advances exactly one day, whatever the clock says. For a step button.</summary>
        public void Step()
        {
            RunOneDay();
            DaysLastAdvanced = 1;
        }

        /// <summary>
        /// One day, with everything it emitted handed to the feed.
        /// </summary>
        /// <remarks>
        /// The drain is what empties the queue, and until now its return value was thrown away
        /// everywhere. Reading it here is the whole reason §6.2 stamped a cause on every event
        /// months before anything consumed one.
        /// </remarks>
        private void RunOneDay()
        {
            Run.AdvanceDay();
            Feed.Record(Run.CommandLog, Run.Events.Drain(), Balance);
        }

        /// <summary>
        /// Queues a player's command. It is validated and applied at the drain, not here.
        /// </summary>
        /// <remarks>
        /// Input never writes world state (§2, §6). It produces a command, the command lands in
        /// the log, and the log plus the seed is the save — so anything a player can do is
        /// replayable by construction.
        /// </remarks>
        public void Submit(ICommand command) => Run.Submit(command);

        /// <summary>
        /// Asks the real handler whether a command would be accepted, without applying it.
        /// </summary>
        /// <remarks>
        /// So a greyed-out button and the command it would issue cannot disagree. Writing the
        /// reasoning a second time in a front end is how a control comes to offer something the
        /// game refuses, or hide something it would have allowed.
        /// <para>
        /// Validation is required not to touch the world (§6), so this is safe to call every
        /// frame. Nothing is emitted and nothing is logged: a question is not a decision.
        /// </para>
        /// </remarks>
        public CommandRejection Validate(ICommand command)
        {
            if (command == null) return CommandRejection.InvalidTarget;

            for (int i = 0; i < _handlers.Length; i++)
            {
                if (_handlers[i].CommandType != command.GetType()) continue;

                var ctx = new Context(Day, 0f, Run.Events, Run.Rng, Balance);
                return _handlers[i].Validate(command, World, in ctx);
            }

            return CommandRejection.Unavailable;
        }

        /// <summary>
        /// Everything the player could do this moment, enabled or not.
        /// </summary>
        /// <remarks>
        /// Disabled actions are listed rather than hidden. A control that appears only when it
        /// would work leaves the player unable to learn that it exists, and §3.2 is betting on a
        /// game that can be understood by thinking about it rather than by discovering it.
        /// <para>
        /// Rebuilt into the same buffer each call, like the readouts.
        /// </para>
        /// </remarks>
        public IReadOnlyList<PlayerAction> Actions()
        {
            _actions.Clear();

            AddRepression();
            AddBuildings();

            return _actions;
        }

        /// <summary>Putting down a riot, at each price §5.2.2 offers.</summary>
        private void AddRepression()
        {
            for (int i = 0; i < Balance.Repression.Count; i++)
            {
                RepressionRules rules = Balance.Repression[i];
                var command = new SuppressRiot(rules.Harshness);

                // The price is on the button. §5.2.2 wants repression to be a decision rather
                // than a reflex, and a decision needs its cost visible at the moment it is made.
                _actions.Add(new PlayerAction(
                    group: "Unrest",
                    label: rules.Harshness.ToString(),
                    detail: $"−{rules.GrievanceRelief:0.00} now, +{rules.BaselineIncrease:0.00} " +
                            $"forever, {rules.CowedDays}d quiet",
                    command: command,
                    rejection: Validate(command)));
            }
        }

        /// <summary>
        /// Shutting and reopening buildings, and posting named crew to them.
        /// </summary>
        /// <remarks>
        /// One row per building rather than a selection model. §5.5's port is small, and a list
        /// a player can read top to bottom is worth more than a tidier interaction that hides
        /// what the port is doing.
        /// </remarks>
        private void AddBuildings()
        {
            ComponentStore<BuildingState> buildings = World.Store<BuildingState>();

            for (int i = 0; i < buildings.Count; i++)
            {
                EntityId id = buildings.Ids[i];
                BuildingState state = buildings.Values[i];
                Building definition = Balance.Buildings[state.DefinitionIndex];
                string detail = Describe(in state, definition, id);

                var mothball = new MothballBuilding(id, !state.Mothballed);
                _actions.Add(new PlayerAction(
                    group: "Buildings",
                    label: (state.Mothballed ? "Reopen " : "Shut ") + definition.Id,
                    detail: detail,
                    command: mothball,
                    rejection: Validate(mothball)));

                if (definition.Staff <= 0) continue;

                var post = new AssignCrew(FirstUnpostedCrew(), id);
                _actions.Add(new PlayerAction(
                    group: "Buildings",
                    label: "post a specialist",
                    detail: detail,
                    command: post,
                    rejection: Validate(post)));

                EntityId posted = FirstCrewAt(id);
                if (posted.IsNone) continue;

                var recall = new AssignCrew(posted, EntityId.None);
                _actions.Add(new PlayerAction(
                    group: "Buildings",
                    label: "recall a specialist",
                    detail: detail,
                    command: recall,
                    rejection: Validate(recall)));
            }
        }

        private string Describe(in BuildingState state, Building definition, EntityId id)
        {
            if (state.Mothballed) return "shut";

            string condition = (state.Condition * 100f).ToString("0") + "%";

            return definition.Staff > 0
                ? $"{state.Workers}/{definition.Staff} worked, {CrewAt(id)} overseeing, {condition}"
                : condition;
        }

        /// <summary>A crew member nobody has posted, or None. First in creation order.</summary>
        private EntityId FirstUnpostedCrew()
        {
            ComponentStore<CrewMember> crew = World.Store<CrewMember>();

            for (int i = 0; i < crew.Count; i++)
            {
                EntityId id = crew.Ids[i];
                if (!World.TryGet(id, out Assignment assignment) || assignment.IsIdle) return id;
            }

            return EntityId.None;
        }

        private EntityId FirstCrewAt(EntityId building)
        {
            ComponentStore<Assignment> assignments = World.Store<Assignment>();

            for (int i = 0; i < assignments.Count; i++)
                if (assignments.Values[i].Building == building) return assignments.Ids[i];

            return EntityId.None;
        }

        private int CrewAt(EntityId building)
        {
            ComponentStore<Assignment> assignments = World.Store<Assignment>();
            int found = 0;

            for (int i = 0; i < assignments.Count; i++)
                if (assignments.Values[i].Building == building) found++;

            return found;
        }

        /// <summary>The day's numbers, computed once for whoever is drawing them.</summary>
        public PortReport Report() => PortReport.Of(World, Balance, Day);

        /// <summary>
        /// The state readouts BUILD_ORDER asks Phase 3 for, as labelled strings.
        /// </summary>
        /// <remarks>
        /// Formatted here rather than in the renderer so that what the player is told is a
        /// property of the game and not of the engine drawing it. A console harness and a Unity
        /// panel show the same words in the same order, and changing them is a one-file change
        /// with a headless test behind it.
        /// <para>
        /// The list is rebuilt into the same buffer each call: this runs every frame, and a
        /// front end should not have to think about the garbage a readout makes.
        /// </para>
        /// </remarks>
        public IReadOnlyList<Readout> Readouts()
        {
            _readouts.Clear();

            PortReport report = Report();

            _readouts.Add(new Readout("Day", report.Day.ToString()));
            _readouts.Add(new Readout("Coin", report.Coin.ToString()));

            if (report.Arrears > 0)
                _readouts.Add(new Readout("Unpaid", report.Arrears.ToString()));

            _readouts.Add(new Readout("Upkeep", UpkeepPerDay().ToString() + "/day"));
            _readouts.Add(new Readout("Crew", report.Crew.ToString()));
            _readouts.Add(new Readout("Town", Commoners().ToString()));
            _readouts.Add(new Readout("Unemployed", LabourSystem.UnemployedIn(World, PlayerPort).ToString()));
            _readouts.Add(new Readout("Morale", Percent(report.AverageMorale)));
            _readouts.Add(new Readout("Condition", Percent(report.AverageCondition)));

            for (int i = 0; i < Balance.Goods.Count && i < report.Stock.Count; i++)
            {
                float units = report.Stock[i];
                if (units <= 0f && Balance.Goods[i].Supply == GoodSupply.ImportOnly) continue;

                _readouts.Add(new Readout(Balance.Goods[i].Id, units.ToString("0.0")));
            }

            _readouts.Add(new Readout("Unrest", report.Rung.ToString()));

            for (int i = 0; i < Balance.Strata.Count && i < report.Grievance.Count; i++)
                _readouts.Add(new Readout(Balance.Strata[i].Id, Percent(report.Grievance[i])));

            return _readouts;
        }

        /// <summary>What the port owes in upkeep every day, shut buildings excepted.</summary>
        public int UpkeepPerDay()
        {
            ComponentStore<BuildingState> buildings = World.Store<BuildingState>();
            int total = 0;

            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingState state = buildings.Values[i];
                if (state.Mothballed) continue;

                total += Balance.Buildings[state.DefinitionIndex].UpkeepCoin;
            }

            return total;
        }

        public int Commoners() => LabourSystem.CommonersIn(World, PlayerPort);

        private static string Percent(float value) => (value * 100f).ToString("0") + "%";
    }

    /// <summary>One labelled number for a front end to draw.</summary>
    public readonly struct Readout
    {
        public Readout(string label, string value)
        {
            Label = label;
            Value = value;
        }

        public readonly string Label;
        public readonly string Value;

        public override string ToString() => Label + " " + Value;
    }
}
