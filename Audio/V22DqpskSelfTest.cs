namespace SoundModem.Audio;

internal static class V22DqpskSelfTest
{
    public static int Run()
    {
        byte[] expected = [0x55, 0xA3, 0x00, 0xFF];
        var transmitQueue = new ByteRingBuffer(16);
        var receiveQueue = new ByteRingBuffer(16);
        var modulator = new V22DqpskModulator(transmitQueue);
        var demodulator = new V22DqpskDemodulator(receiveQueue);
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
                $"V.22 DSP self-test failed. Expected {Convert.ToHexString(expected)}, received {Convert.ToHexString(actual.ToArray())}.");
            return 1;
        }

        Console.WriteLine("V.22 DSP self-test passed (1200 bit/s, 600 baud, differential QPSK, 1200 Hz).");
        return 0;
    }
}
