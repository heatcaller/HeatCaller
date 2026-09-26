using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// A gain that glides to its target instead of jumping there - used for
/// the music's duck under the announcer.
///
/// The duck used to be a straight volume change, applied the instant the
/// announcer started or stopped. A line that started and then ended
/// almost at once - dropped before it spoke, or a very short one - cut
/// the music to a quarter and straight back, which sounds like the music
/// dropping out. Gliding, the level moves down in about a tenth of a
/// second and back up over half a second, so a blip that short barely
/// moves it, and a real line still gets the music out of its way.
///
/// Worked out per sample on the audio thread, so the glide is smooth
/// whatever the output buffer size, and there is no timer to go wrong.
/// </summary>
public sealed class SmoothGainSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;

    // Changes per second, as a fraction of full gain.
    private readonly float _downPerSecond;
    private readonly float _upPerSecond;

    private float _gain;
    private volatile float _target;

    /// <param name="initialGain">Where the gain starts - no glide from 1 into a duck that was already on.</param>
    /// <param name="downSeconds">Time to glide the whole way from 1 to 0.</param>
    /// <param name="upSeconds">Time to glide the whole way from 0 to 1.</param>
    public SmoothGainSampleProvider(ISampleProvider source, float initialGain, double downSeconds = 0.12, double upSeconds = 0.6)
    {
        _source = source;
        _channels = Math.Max(1, source.WaveFormat.Channels);
        _gain = Math.Clamp(initialGain, 0f, 1f);
        _target = _gain;
        _downPerSecond = (float)(1.0 / Math.Max(0.001, downSeconds));
        _upPerSecond = (float)(1.0 / Math.Max(0.001, upSeconds));
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>The gain to glide to, 0 to 1. Safe to set from any thread.</summary>
    public float Target
    {
        get => _target;
        set => _target = Math.Clamp(value, 0f, 1f);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);

        var target = _target;
        var rate = Math.Max(1, WaveFormat.SampleRate);
        var down = _downPerSecond / rate;
        var up = _upPerSecond / rate;

        for (var i = 0; i < read; i += _channels)
        {
            if (_gain > target)
            {
                _gain = Math.Max(target, _gain - down);
            }
            else if (_gain < target)
            {
                _gain = Math.Min(target, _gain + up);
            }

            for (var c = 0; c < _channels && i + c < read; c++)
            {
                buffer[offset + i + c] *= _gain;
            }
        }

        return read;
    }
}
