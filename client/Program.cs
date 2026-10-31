using System.IO.Pipes;

// usage: client <event> [count] [intervalMs]  -- fires <event> count times to measure background/rate behaviour
var name = args.Length > 0 ? args[0] : "recoil_1";
var count = args.Length > 1 ? int.Parse(args[1]) : 1;
var interval = args.Length > 2 ? int.Parse(args[2]) : 100;

using var pipe = new NamedPipeClientStream(".", "HapticGamingBridge", PipeDirection.Out);
pipe.Connect(3000);
using var w = new StreamWriter(pipe) { AutoFlush = true };
for (var i = 0; i < count; i++)
{
    w.WriteLine(name);
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} sent {name} #{i + 1}");
    Thread.Sleep(interval);
}
