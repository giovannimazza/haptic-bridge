namespace Loupedeck.HapticPlugin
{
    using System;
    using System.IO;
    using System.IO.Pipes;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;

    public class HapticPlugin : Plugin
    {
        public const String PipeName = "HapticBridge";

        // Event names come from events/DefaultEventSource.yaml; anything else is dropped.
        private static readonly Regex ValidName = new("^[A-Za-z_][A-Za-z0-9_]*$");

        private CancellationTokenSource _cts;

        public override Boolean UsesApplicationApiOnly => true;
        public override Boolean HasNoApplication => true;

        public HapticPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        public override void Load()
        {
            this._cts = new CancellationTokenSource();
            _ = Task.Run(() => this.ServePipe(this._cts.Token));
        }

        public override void Unload() => this._cts?.Cancel();

        // shortcut: one client at a time, no auth on the pipe; add a per-user ACL before shipping.
        private async Task ServePipe(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await pipe.WaitForConnectionAsync(ct);
                    using var reader = new StreamReader(pipe);
                    String line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        line = line.Trim();
                        if (!ValidName.IsMatch(line))
                        {
                            PluginLog.Warning($"Invalid event name '{line}'");
                            continue;
                        }
                        PluginLog.Info($"Raising {line}");
                        this.PluginEvents.RaiseEvent(line);
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { PluginLog.Error(ex, "Pipe error"); await Task.Delay(500); }
            }
        }
    }
}
