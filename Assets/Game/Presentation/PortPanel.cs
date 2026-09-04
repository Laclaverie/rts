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
        }

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
