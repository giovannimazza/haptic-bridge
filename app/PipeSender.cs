using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;

namespace HapticGamingBridge
{
    // Sends event names to the Logi plugin. Writes happen off the hook thread so the hook never blocks input.
    // A haptic event is only useful right now, so when the plugin is missing or slow events are dropped instead of queued:
    // nothing piles up, and nothing from the past is replayed when the plugin comes back.
    public class PipeSender : IDisposable
    {
        public const string DefaultPipeName = "HapticGamingBridge";

        private const int MaxQueued = 32;
        private const int MaxAgeMs = 750;       // older than this when it reaches the front: stale, drop it
        private const int RetryAfterMs = 500;   // after a failed connect, drop events for this long instead of waiting on each one

        private readonly BlockingCollection<(string Name, long At)> _queue = new(MaxQueued);
        private readonly string _pipeName;
        private readonly Thread _thread;
        private int _dropped;

        public volatile bool Connected;
        public int Dropped => Volatile.Read(ref _dropped);

        public PipeSender(string pipeName = DefaultPipeName)
        {
            _pipeName = pipeName;
            _thread = new Thread(Run) { IsBackground = true };
            _thread.Start();
        }

        // True while a plugin is listening: the plugin always keeps one pipe instance waiting.
        public static bool PluginAvailable(string pipeName = DefaultPipeName)
        {
            try { return Directory.EnumerateFiles(@"\\.\pipe\").Any(f => f.EndsWith(pipeName, StringComparison.OrdinalIgnoreCase)); }
            catch { return false; }
        }

        public void Send(string name)
        {
            try { if (!_queue.TryAdd((name, Stopwatch.GetTimestamp()))) Drop(); }
            catch (InvalidOperationException) { }   // already disposed: the app is closing
        }

        private void Drop() => Interlocked.Increment(ref _dropped);

        private static double AgeMs(long at) => (Stopwatch.GetTimestamp() - at) * 1000.0 / Stopwatch.Frequency;

        private void Run()
        {
            NamedPipeClientStream pipe = null; StreamWriter w = null;
            var retryAt = 0L;
            foreach (var (name, at) in _queue.GetConsumingEnumerable())
            {
                if (AgeMs(at) > MaxAgeMs || (w == null && Environment.TickCount64 < retryAt)) { Drop(); continue; }

                var sent = false;
                for (var attempt = 0; attempt < 2 && !sent; attempt++)
                {
                    var hadConnection = w != null;
                    try
                    {
                        if (w == null)
                        {
                            pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
                            pipe.Connect(200);
                            w = new StreamWriter(pipe) { AutoFlush = true };
                        }
                        w.WriteLine(name);
                        sent = true; Connected = true;
                    }
                    catch
                    {
                        Connected = false;
                        try { w?.Dispose(); pipe?.Dispose(); } catch { }
                        w = null; pipe = null;
                        if (!hadConnection) break;   // the plugin is not there: do not wait a second time
                    }
                }
                if (!sent) { Drop(); retryAt = Environment.TickCount64 + RetryAfterMs; }
            }
            try { w?.Dispose(); pipe?.Dispose(); } catch { }
        }

        public void Dispose() => _queue.CompleteAdding();
    }
}
