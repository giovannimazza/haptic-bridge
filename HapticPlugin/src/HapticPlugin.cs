namespace Loupedeck.HapticPlugin
{
    using System;
    using System.IO;
    using System.IO.Pipes;
    using System.Security.AccessControl;
    using System.Security.Principal;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;

    public class HapticPlugin : Plugin
    {
        public const String PipeName = "HapticBridge";

        private const Int32 MaxLineLength = 64;   // longest declared event name is far shorter
        private const Int32 MaxClients = 4;       // a silent client must not be able to lock everyone else out

        // Event names come from events/DefaultEventSource.yaml; anything that is not a plain identifier is dropped.
        private static readonly Regex ValidName = new("^[A-Za-z_][A-Za-z0-9_]{0,63}$");

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

        private async Task ServePipe(CancellationToken ct)
        {
            // Only the current user may open the pipe; the default ACL also grants read access to Everyone and anonymous users.
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var pipe = NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.In, MaxClients, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous, 0, 0, security);
                    try { await pipe.WaitForConnectionAsync(ct); }
                    catch { pipe.Dispose(); throw; }
                    _ = Task.Run(() => this.HandleClient(pipe, ct), ct);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "Pipe error");
                    try { await Task.Delay(500, ct); } catch (OperationCanceledException) { return; }
                }
            }
        }

        // Reads newline-terminated names without ever buffering more than MaxLineLength characters per line.
        private async Task HandleClient(NamedPipeServerStream pipe, CancellationToken ct)
        {
            using (pipe)
            {
                try
                {
                    using var reader = new StreamReader(pipe);
                    var buffer = new Char[256];
                    var line = new StringBuilder();
                    var skipping = false;
                    Int32 read;
                    while ((read = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
                    {
                        for (var i = 0; i < read; i++)
                        {
                            var ch = buffer[i];
                            if (ch == '\n')
                            {
                                if (!skipping) this.HandleLine(line.ToString());
                                line.Clear();
                                skipping = false;
                            }
                            else if (!skipping)
                            {
                                if (line.Length >= MaxLineLength)
                                {
                                    skipping = true;
                                    line.Clear();
                                    PluginLog.Warning("Line too long, dropped");
                                }
                                else
                                {
                                    line.Append(ch);
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { PluginLog.Error(ex, "Client error"); }
            }
        }

        private void HandleLine(String raw)
        {
            var name = raw.Trim();
            if (!ValidName.IsMatch(name))
            {
                PluginLog.Warning("Invalid event name");   // the text itself is not logged: it is untrusted
                return;
            }

            try
            {
                PluginLog.Info($"Raising {name}");
                this.PluginEvents.RaiseEvent(name);
            }
            catch (Exception ex) { PluginLog.Error(ex, "RaiseEvent failed"); }
        }
    }
}
