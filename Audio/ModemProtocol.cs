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
