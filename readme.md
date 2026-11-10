# SoundModem

SoundModem is a cross-platform C#/.NET 8 software modem that bridges a serial byte stream and audio using V.21 originate-channel FSK. It uses 300 baud, 8N1 framing, mono 8 kHz PCM, 1180 Hz mark, and 980 Hz space tones.

## Modules and classes

- **Application (`Program`)**: parses command-line options, creates the audio and transport services, and runs the modem session.
- **Audio (`SoundModem.Audio`)**: `PortAudioHost` owns the native audio stream and callback; `PortAudioNative` declares the PortAudio bindings; `V21FskModulator` and `V21FskDemodulator` encode and decode serial frames; `ByteRingBuffer` transfers bytes between real-time audio processing and application tasks; `V21FskSelfTest` checks the DSP loopback.
- **Modem (`SoundModem.Modem`)**: `ModemSession` handles AT commands, command/data modes, serial input/output, and the `+++` escape sequence.
- **Transport (`SoundModem.Transport`)**: `ISerialTransport` describes the stream endpoint; `SerialTransport` selects the platform implementation; its `PtyTransport` creates a Unix PTY and `PipeTransport` hosts a Windows named pipe. `PtyNative` wraps the required Unix calls.

## Build and run

Install the native PortAudio runtime and connect an audio input/output device, then run:

```sh
dotnet build V21Modem/SoundModem.csproj
dotnet run --project V21Modem/SoundModem.csproj
```

Use `--endpoint` to override the default endpoint or `--self-test` to run the DSP loopback without audio hardware. The modem supports `AT`, `ATE0/1`, `ATV0/1`, `ATD/ATDT`, `ATA`, `ATH`, and `+++`. Windows named-pipe use requires an external COM bridge.
