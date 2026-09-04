using System;
using System.Collections.Generic;

namespace RTS.Sim.Engine.Diagnostics
{
    /// <summary>
    /// A named stream of log lines, resolved once to an index so that checking whether it is
    /// enabled is an array read rather than a string comparison.
    /// </summary>
    /// <remarks>
    /// Channels are strings rather than an enum so a class can declare its own without
    /// touching a shared file — the point of the exercise being that a reader can filter one
    /// subsystem in or out. Resolving to an index at declaration keeps that free at call time.
    /// <para>
    /// Declare one per class as a <c>static readonly</c> field and reuse it;
    /// <see cref="Log.Channel"/> takes a lock, so resolving in a hot loop would be the one way
    /// to make this expensive.
    /// </para>
    /// </remarks>
    public readonly struct LogChannel : IEquatable<LogChannel>
    {
        internal LogChannel(int index, string name)
        {
            Index = index;
            Name = name;
        }

        internal readonly int Index;

        public readonly string Name;

        public bool IsValid => Name != null;

        public bool Equals(LogChannel other) => Index == other.Index;

        public override bool Equals(object obj) => obj is LogChannel other && Equals(other);

        public override int GetHashCode() => Index;

        public static bool operator ==(LogChannel a, LogChannel b) => a.Index == b.Index;

        public static bool operator !=(LogChannel a, LogChannel b) => a.Index != b.Index;

        public override string ToString() => Name ?? "<unset>";
    }

    /// <summary>
    /// The channels declared so far, and each one's threshold.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Log"/> so the table can be inspected and reconfigured — by
    /// <c>logging.csv</c>, or by a reader tool — without going through the write path.
    /// </remarks>
    internal sealed class LogChannelTable
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, int> _indexByName = new Dictionary<string, int>(StringComparer.Ordinal);

        private string[] _names = new string[16];
        private LogLevel[] _levels = new LogLevel[16];
        private int _count;

        /// <summary>The threshold applied to a channel nobody has configured.</summary>
        public LogLevel DefaultLevel { get; set; } = LogLevel.Info;

        public int Count => _count;

        public LogChannel Resolve(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A channel needs a name.", nameof(name));

            lock (_gate)
            {
                if (_indexByName.TryGetValue(name, out int existing))
                    return new LogChannel(existing, _names[existing]);

                if (_count == _names.Length)
                {
                    Array.Resize(ref _names, _names.Length * 2);
                    Array.Resize(ref _levels, _levels.Length * 2);
                }

                int index = _count++;
                _names[index] = name;
                _levels[index] = DefaultLevel;
                _indexByName.Add(name, index);

                return new LogChannel(index, name);
            }
        }

        /// <summary>
        /// The threshold for a channel. Read without a lock: a torn read is impossible for an
        /// int-sized enum, and the worst case is one line logged against a threshold that
        /// changed a microsecond ago.
        /// </summary>
        public LogLevel LevelOf(in LogChannel channel) =>
            channel.Index >= 0 && channel.Index < _count ? _levels[channel.Index] : DefaultLevel;

        public void SetLevel(in LogChannel channel, LogLevel level)
        {
            lock (_gate)
            {
                if (channel.Index >= 0 && channel.Index < _count) _levels[channel.Index] = level;
            }
        }

        /// <summary>Sets a level for a channel that may not have been declared yet.</summary>
        public void SetLevel(string name, LogLevel level) => SetLevel(Resolve(name), level);

        /// <summary>Every declared channel and its threshold, in declaration order.</summary>
        public IReadOnlyList<KeyValuePair<string, LogLevel>> Snapshot()
        {
            lock (_gate)
            {
                var result = new List<KeyValuePair<string, LogLevel>>(_count);
                for (int i = 0; i < _count; i++)
                    result.Add(new KeyValuePair<string, LogLevel>(_names[i], _levels[i]));

                return result;
            }
        }

        public void ResetLevels(LogLevel level)
        {
            lock (_gate)
            {
                for (int i = 0; i < _count; i++) _levels[i] = level;
            }
        }
    }
}
