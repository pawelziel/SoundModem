using System.Reflection;
using System.Runtime.InteropServices;

namespace SoundModem.Audio;

internal sealed class PortAudioHost : IDisposable
{
    private const int Continue = 0;
    private const ulong Int16SampleFormat = 0x00000008;
    private const int BufferCapacity = 4096;
    private readonly ByteRingBuffer _transmitQueue = new(BufferCapacity);
    private readonly ByteRingBuffer _receiveQueue = new(BufferCapacity);
    private readonly V21FskModulator _modulator;
    private readonly V21FskDemodulator _demodulator;
    private readonly PortAudioNative.StreamCallback _callback;
    private IntPtr _stream;
    private bool _initialized;
    private bool _disposed;

    public PortAudioHost()
    {
        _modulator = new V21FskModulator(_transmitQueue);
        _demodulator = new V21FskDemodulator(_receiveQueue);
        _callback = ProcessAudio;

        try
        {
            Check(PortAudioNative.Initialize(), "initialize");
            _initialized = true;

            var result = PortAudioNative.OpenDefaultStream(
                out _stream,
                inputChannels: 1,
                outputChannels: 1,
                sampleFormat: Int16SampleFormat,
                sampleRate: 8000,
                framesPerBuffer: 0,
                callback: _callback,
                userData: IntPtr.Zero);
            Check(result, "open the default audio input/output stream");
            Check(PortAudioNative.StartStream(_stream), "start the audio stream");
        }
        catch
        {
            if (_stream != IntPtr.Zero)
            {
                PortAudioNative.CloseStream(_stream);
                _stream = IntPtr.Zero;
            }

            if (_initialized)
            {
                PortAudioNative.Terminate();
                _initialized = false;
            }

            throw;
        }
    }

    public bool TryQueueTransmit(byte value) => _transmitQueue.TryWrite(value);

    public bool TryReadReceived(out byte value) => _receiveQueue.TryRead(out value);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_stream != IntPtr.Zero)
        {
            PortAudioNative.StopStream(_stream);
            PortAudioNative.CloseStream(_stream);
            _stream = IntPtr.Zero;
        }

        if (_initialized)
        {
            PortAudioNative.Terminate();
            _initialized = false;
        }
    }

    private unsafe int ProcessAudio(
        IntPtr input,
        IntPtr output,
        UIntPtr frameCount,
        IntPtr timeInfo,
        ulong statusFlags,
        IntPtr userData)
    {
        var frames = checked((int)frameCount.ToUInt64());
        var inputSamples = (short*)input;
        var outputSamples = (short*)output;

        for (var index = 0; index < frames; index++)
        {
            if (inputSamples != null)
            {
                _demodulator.ProcessSample(inputSamples[index]);
            }

            if (outputSamples != null)
            {
                outputSamples[index] = _modulator.NextSample();
            }
        }

        return Continue;
    }

    private static void Check(int result, string operation)
    {
        if (result != 0)
        {
            var message = Marshal.PtrToStringUTF8(PortAudioNative.GetErrorText(result));
            throw new InvalidOperationException($"PortAudio could not {operation}: {message ?? $"error {result}"}.");
        }
    }
}

internal static class PortAudioNative
{
    private const string LibraryName = "portaudio_v21";

    static PortAudioNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(PortAudioNative).Assembly, ResolveLibrary);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int StreamCallback(
        IntPtr input,
        IntPtr output,
        UIntPtr frameCount,
        IntPtr timeInfo,
        ulong statusFlags,
        IntPtr userData);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Pa_Initialize")]
    internal static extern int Initialize();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Pa_Terminate")]
    internal static extern int Terminate();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Pa_OpenDefaultStream")]
    internal static extern int OpenDefaultStream(
        out IntPtr stream,
        int inputChannels,
        int outputChannels,
        ulong sampleFormat,
        double sampleRate,
        uint framesPerBuffer,
        StreamCallback callback,
        IntPtr userData);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Pa_StartStream")]
    internal static extern int StartStream(IntPtr stream);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Pa_StopStream")]
    internal static extern int StopStream(IntPtr stream);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Pa_CloseStream")]
    internal static extern int CloseStream(IntPtr stream);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Pa_GetErrorText")]
    internal static extern IntPtr GetErrorText(int errorCode);

    private static IntPtr ResolveLibrary(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryName)
        {
            return IntPtr.Zero;
        }

        var candidates = OperatingSystem.IsWindows()
            ? new[] { "portaudio.dll", "libportaudio-2.dll" }
            : OperatingSystem.IsMacOS()
                ? new[] { "libportaudio.2.dylib", "libportaudio.dylib" }
                : new[] { "libportaudio.so.2", "libportaudio.so" };

        foreach (var candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
            {
                return handle;
            }
        }

        throw new DllNotFoundException(
            $"PortAudio was not found. Install the PortAudio runtime library (for example, libportaudio2 on Debian/Ubuntu). Tried: {string.Join(", ", candidates)}.");
    }
}