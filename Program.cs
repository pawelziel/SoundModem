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

        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            return V21FskSelfTest.Run();
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
            using var audio = new PortAudioHost();
            await using var transport = await SerialTransport.OpenAsync(endpoint, cancellation.Token);
            Console.Error.WriteLine($"V.21 modem ready. Serial endpoint: {transport.Endpoint}");
            Console.Error.WriteLine("Use Ctrl+C to stop.");

            var session = new ModemSession(transport.Stream, audio);
            await session.RunAsync(cancellation.Token);
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"V.21 modem failed: {exception.Message}");
            return 1;
        }
    }

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
        Console.WriteLine("SoundModem [--endpoint PATH_OR_PIPE_NAME]");
        Console.WriteLine("SoundModem --self-test");
        Console.WriteLine($"Default endpoint: {defaultEndpoint}");
        Console.WriteLine("On Unix, the endpoint is a symlink to a PTY slave. On Windows, it is a named pipe.");
    }
}