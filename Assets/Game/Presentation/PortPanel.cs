using System.Collections.Generic;
using RTS.Sim.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace RTS.Game.Presentation
{
    /// <summary>
    /// Draws a <see cref="GameSession"/>. Deliberately ugly (BUILD_ORDER Phase 3).
    /// </summary>
    /// <remarks>
    /// A binder and nothing else. It decides where things sit on screen; what they say and what
    /// the buttons do belong to <see cref="GameSession"/>, which knows nothing about Unity and
    /// is tested without one. That split is deliberate: an engine is a dependency somebody else
    /// upgrades on their schedule, and the cost of that upgrade should be the size of this file
    /// rather than the size of the game.
    /// <para>
    /// Built in C# rather than UXML because the layout is scaffolding that will be replaced
    /// wholesale, and C# diffs and reviews as code. No stylesheet, no prefab, no inspector
    /// wiring — the whole panel is here.
    /// </para>
    /// </remarks>
    public sealed class PortPanel
    {
        private readonly GameSession _session;
        private readonly Label _day = new Label();
        private readonly VisualElement _readouts = new VisualElement();
        private readonly VisualElement _feed = new VisualElement();
        private readonly ScrollView _feedScroll = new ScrollView();
        private readonly List<Button> _speedButtons = new List<Button>();
        private Button _pause;

        public PortPanel(GameSession session) => _session = session;

        public VisualElement Root { get; private set; }

        public VisualElement Build()
        {
            Root = Column();
            Root.style.position = Position.Absolute;
            Root.style.left = 8;
            Root.style.top = 8;
            Root.style.minWidth = 240;
            Root.style.paddingLeft = 8;
            Root.style.paddingRight = 8;
            Root.style.paddingTop = 6;
            Root.style.paddingBottom = 6;
            Root.style.backgroundColor = new Color(0f, 0f, 0f, 0.65f);

            Root.Add(Controls());
            Root.Add(_readouts);
            Root.Add(Feed());

            Refresh();
            return Root;
        }

        private VisualElement Controls()
        {
            VisualElement bar = Row();
            bar.style.marginBottom = 6;

            _pause = new Button(() => { _session.Clock.TogglePause(); Refresh(); });
            Style(_pause);
            bar.Add(_pause);

            var step = new Button(() => { _session.Step(); Refresh(); }) { text = ">" };
            step.tooltip = "Advance one day";
            Style(step);
            bar.Add(step);

            foreach (int speed in _session.Clock.Speeds)
            {
                int chosen = speed;
                var button = new Button(() => { _session.Clock.Speed = chosen; Refresh(); })
                {
                    text = "x" + chosen,
                };
                Style(button);
                _speedButtons.Add(button);
                bar.Add(button);
            }

            _day.style.marginLeft = 8;
            _day.style.color = Color.white;
            _day.style.unityTextAlign = TextAnchor.MiddleLeft;
            bar.Add(_day);

            return bar;
        }

        /// <summary>
        /// Pulls the current numbers across. Called on a change and once a day, not per frame:
        /// nothing the player is reading moves in between.
        /// </summary>
        public void Refresh()
        {
            _pause.text = _session.Clock.Paused ? "Play" : "Pause";
            _day.text = "day " + _session.Day;

            for (int i = 0; i < _speedButtons.Count; i++)
            {
                bool active = _session.Clock.Speeds[i] == _session.Clock.Speed;
                _speedButtons[i].style.color = active ? Color.white : new Color(0.6f, 0.6f, 0.6f);
            }

            IReadOnlyList<Readout> readouts = _session.Readouts();

            // Rebuilt rather than diffed. The list is short, it changes once a day, and a
            // scaffold that is clever about reuse is a scaffold nobody dares delete.
            _readouts.Clear();
            foreach (Readout readout in readouts) _readouts.Add(RowFor(readout));

            RefreshFeed();
        }

        /// <summary>The feed's own section: what happened, most recent last.</summary>
        private VisualElement Feed()
        {
            var section = new VisualElement();
            section.style.marginTop = 8;
            section.style.borderTopWidth = 1;
            section.style.borderTopColor = new Color(1f, 1f, 1f, 0.2f);
            section.style.paddingTop = 4;

            var heading = new Label("what happened");
            heading.style.color = new Color(0.6f, 0.6f, 0.6f);
            heading.style.marginBottom = 2;
            section.Add(heading);

            _feedScroll.style.maxHeight = 220;
            _feedScroll.Add(_feed);
            section.Add(_feedScroll);

            return section;
        }

        private void RefreshFeed()
        {
            _feed.Clear();

            foreach (FeedEntry entry in _session.Feed.Recent(FeedLines))
                _feed.Add(LineFor(entry));

            // Pinned to the newest line. A player returning to the game wants what just
            // happened, not where they had scrolled to twenty days ago.
            _feedScroll.schedule.Execute(() => _feedScroll.scrollOffset =
                new Vector2(0f, _feed.layout.height));
        }

        /// <summary>
        /// One line, indented under whatever caused it.
        /// </summary>
        /// <remarks>
        /// The indent is the causal DAG showing through (§6.2): a consequence sits under its
        /// cause rather than merely after it, so "you shut a building" and "2 crew released"
        /// read as one thought. Lines whose cause has scrolled out of the feed are drawn flat,
        /// which is honest — the port is still paying for a decision the player cannot see.
        /// </remarks>
        private VisualElement LineFor(FeedEntry entry)
        {
            var label = new Label(Prefix(entry) + entry.Text);
            label.style.color = ColourOf(entry.Importance);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.paddingLeft = _session.Feed.TryFindCause(in entry, out _) ? 12 : 0;
            label.style.fontSize = 11;
            return label;
        }

        private string Prefix(FeedEntry entry) =>
            _session.Feed.TryFindCause(in entry, out _) ? "↳ " : string.Empty;

        private static Color ColourOf(FeedImportance importance)
        {
            switch (importance)
            {
                case FeedImportance.Alarming: return new Color(1f, 0.55f, 0.45f);
                case FeedImportance.Notable: return Color.white;
                default: return new Color(0.62f, 0.62f, 0.62f);
            }
        }

        /// <summary>
        /// How many lines are drawn. The feed keeps far more; this is what fits on screen
        /// without the panel becoming the game.
        /// </summary>
        public const int FeedLines = 40;

        private static VisualElement RowFor(Readout readout)
        {
            VisualElement row = Row();

            var label = new Label(readout.Label) { style = { color = new Color(0.75f, 0.75f, 0.75f) } };
            label.style.minWidth = 110;

            var value = new Label(readout.Value) { style = { color = Color.white } };
            value.style.unityFontStyleAndWeight = FontStyle.Bold;

            row.Add(label);
            row.Add(value);
            return row;
        }

        private static VisualElement Row()
        {
            var element = new VisualElement();
            element.style.flexDirection = FlexDirection.Row;
            return element;
        }

        private static VisualElement Column()
        {
            var element = new VisualElement();
            element.style.flexDirection = FlexDirection.Column;
            return element;
        }

        private static void Style(Button button)
        {
            button.style.marginRight = 2;
            button.style.minWidth = 44;
        }
    }
}
