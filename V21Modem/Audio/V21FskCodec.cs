namespace SoundModem.Audio;

internal sealed class V21FskModulator
{
    private const int SampleRate = 8000;
    private const int BaudRate = 300;
    private const double Amplitude = 11000;
    private readonly ByteRingBuffer _transmitQueue;
    private double _phase;
    private double _bitClock;
    private int _frameBitIndex = 10;
    private byte _frame;
    private int _currentBit = 1;

    public V21FskModulator(ByteRingBuffer transmitQueue)
    {
        _transmitQueue = transmitQueue;
    }

    public short NextSample()
    {
        var frequency = _currentBit == 1 ? 1180 : 980;
        var sample = (short)(Amplitude * Math.Sin(_phase));
        _phase += 2 * Math.PI * frequency / SampleRate;
        if (_phase >= 2 * Math.PI)
        {
            _phase -= 2 * Math.PI;
        }

        _bitClock += BaudRate;
        if (_bitClock >= SampleRate)
        {
            _bitClock -= SampleRate;
            _currentBit = NextBit();
        }

        return sample;
    }

    private int NextBit()
    {
        if (_frameBitIndex >= 10)
        {
            if (!_transmitQueue.TryRead(out _frame))
            {
                _frameBitIndex = 10;
                return 1;
            }

            _frameBitIndex = 0;
        }

        var index = _frameBitIndex++;
        return index switch
        {
            0 => 0,
            <= 8 => (_frame >> (index - 1)) & 1,
            _ => 1
        };
    }
}

internal sealed class V21FskDemodulator
{
    private const int SampleRate = 8000;
    private const int BaudRate = 300;
    private const double MinimumEnergy = 1e7;
    private readonly ByteRingBuffer _receiveQueue;
    private double _markPhase;
    private double _spacePhase;
    private double _symbolClock;
    private double _markI;
    private double _markQ;
    private double _spaceI;
    private double _spaceQ;
    private int _receiveBitIndex = -1;
    private byte _receiveByte;

    public V21FskDemodulator(ByteRingBuffer receiveQueue)
    {
        _receiveQueue = receiveQueue;
    }

    public void ProcessSample(short sample)
    {
        var value = (double)sample;
        _markI += value * Math.Cos(_markPhase);
        _markQ += value * Math.Sin(_markPhase);
        _spaceI += value * Math.Cos(_spacePhase);
        _spaceQ += value * Math.Sin(_spacePhase);

        _markPhase = AdvancePhase(_markPhase, 1180);
        _spacePhase = AdvancePhase(_spacePhase, 980);
        _symbolClock += BaudRate;
        if (_symbolClock < SampleRate)
        {
            return;
        }

        _symbolClock -= SampleRate;
        var markEnergy = _markI * _markI + _markQ * _markQ;
        var spaceEnergy = _spaceI * _spaceI + _spaceQ * _spaceQ;
        _markI = _markQ = _spaceI = _spaceQ = 0;

        if (Math.Max(markEnergy, spaceEnergy) < MinimumEnergy)
        {
            return;
        }

        ProcessBit(markEnergy >= spaceEnergy ? 1 : 0);
    }

    private void ProcessBit(int bit)
    {
        if (_receiveBitIndex < 0)
        {
            if (bit == 0)
            {
                _receiveBitIndex = 0;
                _receiveByte = 0;
            }

            return;
        }

        if (_receiveBitIndex < 8)
        {
            _receiveByte |= (byte)(bit << _receiveBitIndex);
            _receiveBitIndex++;
            return;
        }

        if (bit == 1)
        {
            _receiveQueue.TryWrite(_receiveByte);
        }

        _receiveBitIndex = -1;
    }

    private static double AdvancePhase(double phase, int frequency)
    {
        phase += 2 * Math.PI * frequency / SampleRate;
        return phase >= 2 * Math.PI ? phase - 2 * Math.PI : phase;
    }
}