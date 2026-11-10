using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using SoundModem.Audio;

namespace SoundModem.Modem;

internal sealed class ModemSession
{
    private static readonly TimeSpan EscapeGuardTime = TimeSpan.FromSeconds(1);
    private readonly Stream _serial;
    private readonly PortAudioHost _audio;
    private readonly ModemProtocol _protocol;
    private readonly Channel<byte> _input = Channel.CreateBounded<byte>(new BoundedChannelOptions(4096)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = true
    });
    private readonly List<byte> _command = new(64);
    private readonly byte[] _echoBuffer = new byte[1];
    private volatile bool _dataMode;
    private bool _echo = true;
    private bool _verbose = true;
    private int _pendingPluses;
    private long _lastDataActivity = Stopwatch.GetTimestamp();
    private long _lastPlus = Stopwatch.GetTimestamp();

    public ModemSession(Stream serial, PortAudioHost audio, ModemProtocol protocol)
    {
        _serial = serial;
        _audio = audio;
        _protocol = protocol;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var serialReader = ReadSerialAsync(cancellationToken);
        var audioReader = ReadAudioAsync(cancellationToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            while (_input.Reader.TryRead(out var value))
            {
                await ProcessInputAsync(value, cancellationToken);
            }

            await ProcessEscapeTimeoutAsync(cancellationToken);

            if (serialReader.IsFaulted)
            {
                await serialReader;
            }

            if (audioReader.IsFaulted)
            {
                await audioReader;
            }
        }
    }

    private async Task ReadSerialAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[512];
        while (true)
        {
            var count = await _serial.ReadAsync(buffer, cancellationToken);
            if (count == 0)
            {
                throw new EndOfStreamException("The serial terminal disconnected.");
            }

            for (var index = 0; index < count; index++)
            {
                await _input.Writer.WriteAsync(buffer[index], cancellationToken);
            }
        }
    }

    private async Task ReadAudioAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[256];
        while (true)
        {
            var count = 0;
            while (count < buffer.Length && _audio.TryReadReceived(out var value))
            {
                if (_dataMode)
                {
                    buffer[count++] = value;
                }
            }

            if (count > 0)
            {
                await _serial.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
            else
            {
                await Task.Delay(2, cancellationToken);
            }
        }
    }

    private async Task ProcessInputAsync(byte value, CancellationToken cancellationToken)
    {
        if (!_dataMode)
        {
            if (_echo)
            {
                _echoBuffer[0] = value;
                await _serial.WriteAsync(_echoBuffer, cancellationToken);
            }

            if (value is (byte)'\r' or (byte)'\n')
            {
                if (_command.Count > 0)
                {
                    await ProcessCommandAsync(cancellationToken);
                }
            }
            else if (_command.Count < 256)
            {
                _command.Add(value);
            }
            else
            {
                _command.Clear();
                await RespondAsync("ERROR", "4", cancellationToken);
            }

            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (_pendingPluses > 0 && Stopwatch.GetElapsedTime(_lastPlus, now) >= EscapeGuardTime)
        {
            await ProcessEscapeTimeoutAsync(cancellationToken);
        }

        if (!_dataMode)
        {
            await ProcessInputAsync(value, cancellationToken);
            return;
        }

        if (value == (byte)'+' &&
            (_pendingPluses > 0 || Stopwatch.GetElapsedTime(_lastDataActivity, now) >= EscapeGuardTime))
        {
            _pendingPluses++;
            _lastPlus = now;
            return;
        }

        if (_pendingPluses > 0)
        {
            await FlushPendingPlusesAsync(cancellationToken);
        }

        await QueueTransmitAsync(value, cancellationToken);
    }

    private async Task ProcessEscapeTimeoutAsync(CancellationToken cancellationToken)
    {
        if (_pendingPluses == 0 || Stopwatch.GetElapsedTime(_lastPlus) < EscapeGuardTime)
        {
            return;
        }

        if (_pendingPluses == 3)
        {
            _pendingPluses = 0;
            _dataMode = false;
            await RespondAsync("OK", "0", cancellationToken);
        }
        else
        {
            await FlushPendingPlusesAsync(cancellationToken);
        }
    }

    private async Task FlushPendingPlusesAsync(CancellationToken cancellationToken)
    {
        var count = _pendingPluses;
        _pendingPluses = 0;
        for (var index = 0; index < count; index++)
        {
            await QueueTransmitAsync((byte)'+', cancellationToken);
        }
    }

    private async Task QueueTransmitAsync(byte value, CancellationToken cancellationToken)
    {
        while (!_audio.TryQueueTransmit(value))
        {
            await Task.Delay(1, cancellationToken);
        }

        _lastDataActivity = Stopwatch.GetTimestamp();
    }

    private async Task ProcessCommandAsync(CancellationToken cancellationToken)
    {
        var command = Encoding.ASCII.GetString(_command.ToArray()).Trim();
        _command.Clear();
        if (!command.StartsWith("AT", StringComparison.OrdinalIgnoreCase))
        {
            await RespondAsync("ERROR", "4", cancellationToken);
            return;
        }

        var commands = command[2..].ToUpperInvariant();
        if (commands.Length == 0)
        {
            await RespondAsync("OK", "0", cancellationToken);
            return;
        }

        for (var index = 0; index < commands.Length;)
        {
            if (commands.AsSpan(index).StartsWith("DT", StringComparison.Ordinal))
            {
                await ConnectAsync(cancellationToken);
                return;
            }

            switch (commands[index])
            {
                case 'D':
                case 'A':
                    await ConnectAsync(cancellationToken);
                    return;
                case 'H':
                    _dataMode = false;
                    await RespondAsync("OK", "0", cancellationToken);
                    return;
                case 'E' when index + 1 < commands.Length && commands[index + 1] is '0' or '1':
                    _echo = commands[index + 1] == '1';
                    index += 2;
                    break;
                case 'V' when index + 1 < commands.Length && commands[index + 1] is '0' or '1':
                    _verbose = commands[index + 1] == '1';
                    index += 2;
                    break;
                default:
                    await RespondAsync("ERROR", "4", cancellationToken);
                    return;
            }
        }

        await RespondAsync("OK", "0", cancellationToken);
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        _pendingPluses = 0;
        _lastDataActivity = Stopwatch.GetTimestamp();
        _dataMode = true;
        await RespondAsync(_protocol == ModemProtocol.V22 ? "CONNECT 1200" : "CONNECT 300", "1", cancellationToken);
    }

    private Task RespondAsync(string verbose, string numeric, CancellationToken cancellationToken)
    {
        var response = Encoding.ASCII.GetBytes($"\r\n{(_verbose ? verbose : numeric)}\r\n");
        return _serial.WriteAsync(response, cancellationToken).AsTask();
    }
}