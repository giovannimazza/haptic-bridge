using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace HapticGamingBridge
{
    // Sends event names to the Logi plugin; writes happen off the hook thread so the hook never blocks input.
    public class PipeSender : IDisposable
    {
        private readonly BlockingCollection<string> _queue = new();
        private readonly Thread _thread;
        public volatile bool Connected;

        public PipeSender() { _thread = new Thread(Run) { IsBackground = true }; _thread.Start(); }

        public void Send(string name) => _queue.Add(name);

        private void Run()
        {
            NamedPipeClientStream pipe = null; StreamWriter w = null;
            foreach (var name in _queue.GetConsumingEnumerable())
            {
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        if (w == null)
                        {
                            pipe = new NamedPipeClientStream(".", "HapticGamingBridge", PipeDirection.Out);
                            pipe.Connect(200);
                            w = new StreamWriter(pipe) { AutoFlush = true };
                        }
                        w.WriteLine(name); Connected = true; break;
                    }
                    catch { Connected = false; try { w?.Dispose(); pipe?.Dispose(); } catch { } w = null; pipe = null; }
                }
            }
        }

        public void Dispose() => _queue.CompleteAdding();
    }
}
