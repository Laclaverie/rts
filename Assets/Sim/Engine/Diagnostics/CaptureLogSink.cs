using System.Collections.Generic;

namespace RTS.Sim.Engine.Diagnostics
{
    /// <summary>
    /// Keeps records in memory. For tests, and for a future in-editor viewer.
    /// </summary>
    /// <remarks>
    /// Bounded on purpose: an unbounded capture sink left registered during a long session is
    /// a memory leak that looks like a logging feature. The oldest records are dropped.
    /// </remarks>
    public sealed class CaptureLogSink : ILogSink
    {
        private readonly object _gate = new object();
        private readonly Queue<LogRecord> _records = new Queue<LogRecord>();

        public CaptureLogSink(int capacity = 1024)
        {
            Capacity = capacity > 0 ? capacity : 1;
        }

        public int Capacity { get; }

        /// <summary>How many records have been dropped to stay within capacity.</summary>
        public int Dropped { get; private set; }

        public int Count
        {
            get { lock (_gate) return _records.Count; }
        }

        public void Write(in LogRecord record)
        {
            lock (_gate)
            {
                if (_records.Count == Capacity)
                {
                    _records.Dequeue();
                    Dropped++;
                }

                _records.Enqueue(record);
            }
        }

        public void Flush()
        {
            // Nothing is buffered; the records are the buffer.
        }

        public IReadOnlyList<LogRecord> Snapshot()
        {
            lock (_gate) return new List<LogRecord>(_records);
        }

        public void Clear()
        {
            lock (_gate)
            {
                _records.Clear();
                Dropped = 0;
            }
        }
    }
}
