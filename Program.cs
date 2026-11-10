using System.Runtime.InteropServices;
using SoundModem.Audio;
using SoundModem.Modem;
using SoundModem.Transport;

namespace SoundModem;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        if (!TryGetProtocol(args, out var protocol))
        {
            Console.Error.WriteLine("Protocol must be either v21 or v22.");
            return 2;
        }

        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            return protocol == ModemProtocol.V22 ? V22DqpskSelfTest.Run() : V21FskSelfTest.Run();
        }

        var endpoint = GetOption(args, "--endpoint") ??
            (OperatingSystem.IsWindows() ? "V21ModemPipe" : "/tmp/ttyV21Modem");
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            using var audio = AudioHostFactory.Create(protocol);
            await using var transport = await SerialTransport.OpenAsync(endpoint, cancellation.Token);
            Console.Error.WriteLine($"{GetProtocolName(protocol)} modem ready. Serial endpoint: {transport.Endpoint}");
            Console.Error.WriteLine("Use Ctrl+C to stop.");

            var session = new ModemSession(transport.Stream, audio, protocol);
            await session.RunAsync(cancellation.Token);
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"{GetProtocolName(protocol)} modem failed: {exception.Message}");
            return 1;
        }
    }

    private static bool TryGetProtocol(string[] args, out ModemProtocol protocol)
    {
        var value = GetOption(args, "--protocol");
        if (value is null || string.Equals(value, "v21", StringComparison.OrdinalIgnoreCase))
        {
            protocol = ModemProtocol.V21;
            return true;
        }

        if (string.Equals(value, "v22", StringComparison.OrdinalIgnoreCase))
        {
            protocol = ModemProtocol.V22;
            return true;
        }

        protocol = default;
        return false;
    }

    private static string GetProtocolName(ModemProtocol protocol) =>
        protocol == ModemProtocol.V22 ? "V.22" : "V.21";

    private static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static void PrintUsage()
    {
        var defaultEndpoint = OperatingSystem.IsWindows() ? "V21ModemPipe" : "/tmp/ttyV21Modem";
        Console.WriteLine("SoundModem [--protocol v21|v22] [--endpoint PATH_OR_PIPE_NAME]");
        Console.WriteLine("SoundModem --self-test [--protocol v21|v22]");
        Console.WriteLine($"Default endpoint: {defaultEndpoint}");
        Console.WriteLine("On Unix, the endpoint is a symlink to a PTY slave. On Windows, it is a named pipe.");
    }
}