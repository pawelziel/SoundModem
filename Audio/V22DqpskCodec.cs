namespace SoundModem.Audio;

internal sealed class V22DqpskModulator : IAudioModulator
{
    private const int SampleRate = 8000;
    private const int SymbolRate = 600;
    private const double CarrierFrequency = 1200;
    private const double Amplitude = 11000;
    private readonly ByteRingBuffer _transmitQueue;
    private double _phase;
    private double _carrierPhase;
    private double _symbolClock;
    private int _frameBitIndex = 10;
    private byte _frame;

    public V22DqpskModulator(ByteRingBuffer transmitQueue)
    {
        _transmitQueue = transmitQueue;
    }

    public short NextSample()
    {
        var sample = (short)(Amplitude * Math.Sin(_carrierPhase + _phase));
        _carrierPhase = AdvanceCarrierPhase(_carrierPhase);
        _symbolClock += SymbolRate;
        if (_symbolClock >= SampleRate)
        {
            _symbolClock -= SampleRate;
            _phase = AdvanceDataPhase(_phase, NextSymbol());
        }

        return sample;
    }

    private int NextSymbol()
    {
        var firstBit = NextBit();
        var secondBit = NextBit();
        return (firstBit << 1) | secondBit;
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

    private static double AdvanceDataPhase(double phase, int symbol)
    {
        var phaseChange = symbol switch
        {
            0b00 => 0,
            0b01 => Math.PI / 2,
            0b11 => Math.PI,
            _ => -Math.PI / 2
        };

        phase += phaseChange;
        return phase >= 2 * Math.PI
            ? phase - 2 * Math.PI
            : phase < 0
                ? phase + 2 * Math.PI
                : phase;
    }

    private static double AdvanceCarrierPhase(double phase)
    {
        phase += 2 * Math.PI * CarrierFrequency / SampleRate;
        return phase >= 2 * Math.PI ? phase - 2 * Math.PI : phase;
    }
}

internal sealed class V22DqpskDemodulator : IAudioDemodulator
{
    private const int SampleRate = 8000;
    private const int SymbolRate = 600;
    private const double CarrierFrequency = 1200;
    private const double MinimumEnergy = 1e7;
    private readonly ByteRingBuffer _receiveQueue;
    private double _carrierPhase;
    private double _symbolClock;
    private double _inPhase;
    private double _quadrature;
    private double _previousPhase;
    private bool _hasPreviousPhase;
    private int _receiveBitIndex = -1;
    private byte _receiveByte;

    public V22DqpskDemodulator(ByteRingBuffer receiveQueue)
    {
        _receiveQueue = receiveQueue;
    }

    public void ProcessSample(short sample)
    {
        var value = (double)sample;
        _inPhase += value * Math.Cos(_carrierPhase);
        _quadrature += value * Math.Sin(_carrierPhase);
        _carrierPhase = AdvanceCarrierPhase(_carrierPhase);

        _symbolClock += SymbolRate;
        if (_symbolClock < SampleRate)
        {
            return;
        }

        _symbolClock -= SampleRate;
        var energy = _inPhase * _inPhase + _quadrature * _quadrature;
        if (energy >= MinimumEnergy)
        {
            var phase = Math.Atan2(_inPhase, _quadrature);
            if (_hasPreviousPhase)
            {
                ProcessSymbol(DecodeSymbol(phase - _previousPhase));
            }

            _previousPhase = phase;
            _hasPreviousPhase = true;
        }
        else
        {
            _hasPreviousPhase = false;
            _receiveBitIndex = -1;
        }

        _inPhase = _quadrature = 0;
    }

    private void ProcessSymbol(int symbol)
    {
        ProcessBit((symbol >> 1) & 1);
        ProcessBit(symbol & 1);
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

    private static int DecodeSymbol(double phaseDifference)
    {
        var quarterTurns = (int)Math.Round(phaseDifference / (Math.PI / 2));
        var normalizedQuarterTurns = ((quarterTurns % 4) + 4) % 4;
        return normalizedQuarterTurns switch
        {
            0 => 0b00,
            1 => 0b01,
            2 => 0b11,
            _ => 0b10
        };
    }

    private static double AdvanceCarrierPhase(double phase)
    {
        phase += 2 * Math.PI * CarrierFrequency / SampleRate;
        return phase >= 2 * Math.PI ? phase - 2 * Math.PI : phase;
    }
}
