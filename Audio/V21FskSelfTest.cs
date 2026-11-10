namespace SoundModem.Audio;

internal static class V21FskSelfTest
{
    public static int Run()
    {
        byte[] expected = [0x55, 0xA3, 0x00, 0xFF];
        var transmitQueue = new ByteRingBuffer(16);
        var receiveQueue = new ByteRingBuffer(16);
        var modulator = new V21FskModulator(transmitQueue);
        var demodulator = new V21FskDemodulator(receiveQueue);
        foreach (var value in expected)
        {
            transmitQueue.TryWrite(value);
        }

        var actual = new List<byte>();
        for (var sampleIndex = 0; sampleIndex < 8000; sampleIndex++)
        {
            demodulator.ProcessSample(modulator.NextSample());
            while (receiveQueue.TryRead(out var value))
            {
                actual.Add(value);
            }
        }

        if (!actual.SequenceEqual(expected))
        {
            Console.Error.WriteLine(
                $"V.21 DSP self-test failed. Expected {Convert.ToHexString(expected)}, received {Convert.ToHexString(actual.ToArray())}.");
            return 1;
        }

        Console.WriteLine("V.21 DSP self-test passed (300 baud, 8N1, 980/1180 Hz).");
        return 0;
    }
}