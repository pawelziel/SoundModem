namespace SoundModem.Transport;

internal interface ISerialTransport : IAsyncDisposable
{
    Stream Stream { get; }
    string Endpoint { get; }
}