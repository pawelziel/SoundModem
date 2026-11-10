namespace SoundModem.Audio;

internal enum ModemProtocol
{
    V21,
    V22
}

internal interface IAudioModulator
{
    short NextSample();
}

internal interface IAudioDemodulator
{
    void ProcessSample(short sample);
}

internal interface IAudioHost : IDisposable
{
    bool IsAudioAvailable { get; }
    bool TryQueueTransmit(byte value);
    bool TryReadReceived(out byte value);
}

internal static class AudioHostFactory
{
    public static IAudioHost Create(ModemProtocol protocol)
    {
        if (OperatingSystem.IsLinux() && !Directory.Exists("/dev/snd"))
        {
            return new SilentAudioHost();
        }

        try
        {
            return new PortAudioHost(protocol);
        }
        catch (AudioDeviceUnavailableException)
        {
            return new SilentAudioHost();
        }
    }
}

internal sealed class SilentAudioHost : IAudioHost
{
    public bool IsAudioAvailable => false;

    public bool TryQueueTransmit(byte value) => true;

    public bool TryReadReceived(out byte value)
    {
        value = 0;
        return false;
    }

    public void Dispose()
    {
    }
}

internal sealed class AudioDeviceUnavailableException(string message) : Exception(message);
