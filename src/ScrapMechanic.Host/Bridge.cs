using System.Text.Json;
using ScrapMechanic.Host.Core;

namespace ScrapMechanic.Host;

public static class Bridge
{
    public static async Task<int> RunAsync(Func<string, Task<Reply>> send, TextReader input, TextWriter output, TimeSpan? pollInterval = null)
    {
        void Print(Reply reply) => output.WriteLine(JsonSerializer.Serialize(reply, ConfigJson.Options));
        var started = await send("start");
        Print(started);
        if (!started.Ok || started.Status?.ProcessId is null) return 2;
        output.WriteLine("SM_PROCESS_RUNNING multiplayer_readiness=unverified");
        await output.FlushAsync();
        var interval = pollInterval ?? TimeSpan.FromSeconds(2);
        var line = input.ReadLineAsync();
        while (true)
        {
            var tick = Task.Delay(interval);
            if (await Task.WhenAny(line, tick) == line)
            {
                var command = await line;
                if (command is null)
                {
                    // AMP closing/killing the bridge must never kill the independently hosted game.
                    output.WriteLine("SM_BRIDGE_DETACHED game_state=unchanged");
                    return 3;
                }
                command = command.Trim();
                if (command.Length == 0) { line = input.ReadLineAsync(); continue; }
                var response = await send(command);
                Print(response);
                await output.FlushAsync();
                if (command == "stop" && response.Ok) return 0;
                // Failed stop stays alive for inspection/retry. Persistent interlock also prevents
                // relaunch if AMP later times out and kills this bridge.
                line = input.ReadLineAsync();
            }
            else
            {
                var status = await send("status");
                if (!status.Ok)
                {
                    Print(status);
                    return 4; // Control plane unhealthy. Game is left untouched.
                }
                if (status.Status?.ProcessId is null)
                {
                    Print(status);
                    output.WriteLine("SM_PROCESS_EXITED see_agent_log");
                    return 5;
                }
            }
        }
    }
}
