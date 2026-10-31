using System.Diagnostics;
using System.IO.Pipes;
using HapticGamingBridge;
using Xunit;

namespace HapticGamingBridge.Tests
{
    // The app must behave sensibly both with the Logi plugin present and without it. These tests use a private pipe name,
    // so they never touch the real plugin.
    public class PipeSenderTests
    {
        private static string UniqueName() => "hb-test-" + Guid.NewGuid().ToString("N");

        private static async Task<string> ReadLine(NamedPipeServerStream server, int timeoutMs = 3000)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            using var reader = new StreamReader(server);
            try { return await reader.ReadLineAsync(cts.Token); }
            catch (OperationCanceledException) { return null; }
        }

        // ---- without the plugin ----

        [Fact]
        public void Without_plugin_sending_never_blocks_or_throws()
        {
            using var sender = new PipeSender(UniqueName());
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < 5000; i++) sender.Send("recoil_1");
            Assert.True(sw.ElapsedMilliseconds < 500, $"Send blocked the caller for {sw.ElapsedMilliseconds} ms");
            Assert.False(sender.Connected);
        }

        [Fact]
        public async Task Without_plugin_events_are_dropped_not_piled_up()
        {
            using var sender = new PipeSender(UniqueName());
            for (var i = 0; i < 200; i++) { sender.Send("move"); await Task.Delay(5); }   // about a second of held input
            await Task.Delay(1500);
            Assert.True(sender.Dropped >= 150, $"only {sender.Dropped} of 200 events were dropped; the rest are still queued");
        }

        [Fact]
        public async Task Plugin_starting_late_does_not_replay_old_events()
        {
            var name = UniqueName();
            using var sender = new PipeSender(name);
            for (var i = 0; i < 40; i++) { sender.Send("alert"); await Task.Delay(10); }
            await Task.Delay(2500);                                   // old events are well past their useful life

            using var server = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var connect = server.WaitForConnectionAsync();
            sender.Send("knock");                                     // the first fresh event after the plugin appears
            await connect.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("knock", await ReadLine(server));
        }

        [Fact]
        public void Sending_after_dispose_does_not_throw()
        {
            var sender = new PipeSender(UniqueName());
            sender.Dispose();
            sender.Send("move");
        }

        // ---- with the plugin ----

        [Fact]
        public async Task With_plugin_events_arrive_in_order()
        {
            var name = UniqueName();
            using var server = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var connect = server.WaitForConnectionAsync();
            using var sender = new PipeSender(name);
            sender.Send("recoil_1"); sender.Send("move"); sender.Send("done");
            await connect.WaitAsync(TimeSpan.FromSeconds(3));
            using var reader = new StreamReader(server);
            using var cts = new CancellationTokenSource(3000);
            Assert.Equal("recoil_1", await reader.ReadLineAsync(cts.Token));
            Assert.Equal("move", await reader.ReadLineAsync(cts.Token));
            Assert.Equal("done", await reader.ReadLineAsync(cts.Token));
            Assert.True(sender.Connected);
        }

        [Fact]
        public async Task Sender_reconnects_after_the_plugin_restarts()
        {
            var name = UniqueName();
            using var sender = new PipeSender(name);

            var first = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var c1 = first.WaitForConnectionAsync();
            sender.Send("move");
            await c1.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("move", await ReadLine(first));
            first.Dispose();                                          // the plugin goes away (service restart)

            using var second = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var connected = second.WaitForConnectionAsync();
            for (var i = 0; i < 30 && !connected.IsCompleted; i++)    // the first event may be lost on the dead pipe; later ones must reconnect
            {
                sender.Send("knock");
                await Task.WhenAny(connected, Task.Delay(200));
            }
            Assert.True(connected.IsCompleted, "the sender never reconnected to the restarted plugin");
            sender.Send("done");
            using var reader = new StreamReader(second);
            using var cts = new CancellationTokenSource(3000);
            var seen = new List<string>();
            string line;
            while (!seen.Contains("done") && (line = await reader.ReadLineAsync(cts.Token)) != null) seen.Add(line);
            Assert.Contains("done", seen);
        }

        [Fact]
        public void Plugin_availability_follows_the_pipe()
        {
            var name = UniqueName();
            Assert.False(PipeSender.PluginAvailable(name));
            using var server = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            Assert.True(PipeSender.PluginAvailable(name));
        }
    }
}
