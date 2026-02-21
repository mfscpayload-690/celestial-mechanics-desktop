namespace CelestialMechanics.Physics.Extensions;

/// <summary>
/// Fixed-capacity circular buffer for gravitational wave strain samples.
///
/// Stores pairs of (time, strain) for real-time waveform visualization
/// and frequency analysis. When full, the oldest samples are overwritten.
///
/// No heap allocations occur after construction.
/// </summary>
public sealed class WaveformBuffer
{
    private readonly double[] _time;
    private readonly double[] _strain;
    private int _head;
    private int _count;

    /// <summary>Maximum number of samples this buffer holds.</summary>
    public int Capacity { get; }

    /// <summary>Current number of stored samples (≤ Capacity).</summary>
    public int Count => _count;

    /// <summary>Peak absolute strain observed since last reset.</summary>
    public double PeakStrain { get; private set; }

    /// <summary>Simulation time at which peak strain was observed.</summary>
    public double PeakStrainTime { get; private set; }

    public WaveformBuffer(int capacity = 8192)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
        _time = new double[capacity];
        _strain = new double[capacity];
        _head = 0;
        _count = 0;
        PeakStrain = 0.0;
        PeakStrainTime = 0.0;
    }

    /// <summary>
    /// Add a (time, strain) sample. Overwrites oldest if at capacity.
    /// </summary>
    public void Add(double time, double strain)
    {
        _time[_head] = time;
        _strain[_head] = strain;
        _head = (_head + 1) % Capacity;
        if (_count < Capacity) _count++;

        double absStrain = System.Math.Abs(strain);
        if (absStrain > PeakStrain)
        {
            PeakStrain = absStrain;
            PeakStrainTime = time;
        }
    }

    /// <summary>
    /// Copy all stored samples into the provided arrays, ordered oldest-first.
    /// Returns the number of samples copied.
    /// </summary>
    public int GetSamples(double[] timeOut, double[] strainOut)
    {
        int n = System.Math.Min(_count, System.Math.Min(timeOut.Length, strainOut.Length));
        int start = _count < Capacity ? 0 : _head;

        for (int i = 0; i < n; i++)
        {
            int idx = (start + i) % Capacity;
            timeOut[i] = _time[idx];
            strainOut[i] = _strain[idx];
        }
        return n;
    }

    /// <summary>
    /// Get the most recent strain sample, or 0 if buffer is empty.
    /// </summary>
    public double LatestStrain => _count > 0
        ? _strain[(_head - 1 + Capacity) % Capacity]
        : 0.0;

    /// <summary>Reset the buffer and peak tracking.</summary>
    public void Clear()
    {
        _head = 0;
        _count = 0;
        PeakStrain = 0.0;
        PeakStrainTime = 0.0;
    }
}
