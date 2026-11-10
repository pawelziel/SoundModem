# SoundModem

SoundModem is a cross-platform C#/.NET 8 software modem that bridges a serial byte stream and audio using V.21 or V.22 modulation. V.21 uses 300 baud, 8N1 framing, mono 8 kHz PCM, 1180 Hz mark, and 980 Hz space tones. V.22 uses 1200 bit/s, 600 baud differential QPSK, and a 1200 Hz carrier.

The V.22 mode provides the 1200 bit/s DQPSK data path; call negotiation, training/equalization, and full-duplex channel selection are not implemented.

## Project layout

The solution and application project are at the repository root. Source modules are organized directly beneath it:

```text
SoundModem.sln
SoundModem.csproj
Program.cs
Audio/
Modem/
Transport/
```

## Modules and classes

- **Application (`Program`)**: parses command-line options, creates the audio and transport services, and runs the modem session.
- **Audio (`SoundModem.Audio`)**: `PortAudioHost` owns the native audio stream and callback; `PortAudioNative` declares the PortAudio bindings; the V.21 FSK and V.22 differential-QPSK codec pairs encode and decode serial frames; `ByteRingBuffer` transfers bytes between real-time audio processing and application tasks; each codec has a DSP loopback self-test.
- **Modem (`SoundModem.Modem`)**: `ModemSession` handles AT commands, command/data modes, serial input/output, and the `+++` escape sequence.
- **Transport (`SoundModem.Transport`)**: `ISerialTransport` describes the stream endpoint; `SerialTransport` selects the platform implementation; its `PtyTransport` creates a Unix PTY and `PipeTransport` hosts a Windows named pipe. `PtyNative` wraps the required Unix calls.

## Build and run

Install the native PortAudio runtime and connect an audio input/output device, then run:

```sh
dotnet build SoundModem.sln
dotnet run --project SoundModem.csproj
```

Use `--protocol v21|v22` to select the modulation (V.21 is the default), `--endpoint` to override the default endpoint, or `--self-test` to run a DSP loopback without audio hardware. For example, `dotnet run --project SoundModem.csproj -- --protocol v22 --self-test` tests V.22. The modem supports `AT`, `ATE0/1`, `ATV0/1`, `ATD/ATDT`, `ATA`, `ATH`, and `+++`. Windows named-pipe use requires an external COM bridge.

## Releases

Each successful push to `main` publishes a GitHub Release tagged on that commit. Versions start at `v0.1.0`; every subsequent release increments the minor version (`v0.2.0`, `v0.3.0`, and so on).
