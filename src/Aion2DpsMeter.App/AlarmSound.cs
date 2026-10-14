using System.IO;
using System.Media;

namespace Aion2DpsMeter.App;

/// <summary>
/// Spielt einfache, selbst erzeugte Töne (Sinus, 16 Bit, 44,1 kHz) – keine fremden Audiodateien. Die Lautstärke
/// skaliert die Amplitude.
/// </summary>
public static class AlarmSound
{
    private const int SampleRate = 44100;
    private static SoundPlayer? _player;

    public static void Play(string tone, double volume)
    {
        var samples = tone switch
        {
            "Piep" => Beeps(volume),
            "Gong" => Decay([220, 330, 440], 1.8, volume),
            _ => Decay([880, 1320, 1760], 1.2, volume), // Glocke
        };

        try
        {
            _player?.Stop();
            _player = new SoundPlayer(ToWav(samples));
            _player.Play(); // asynchron
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
        {
            // Kein Ton möglich (z. B. kein Ausgabegerät) – die Einblendung im Overlay bleibt.
        }
    }

    /// <summary>Glocken-/Gong-Klang: Grundton mit Obertönen, exponentiell abklingend.</summary>
    private static short[] Decay(double[] frequencies, double seconds, double volume)
    {
        var count = (int)(SampleRate * seconds);
        var samples = new short[count];
        for (var i = 0; i < count; i++)
        {
            var t = (double)i / SampleRate;
            var value = 0.0;
            for (var k = 0; k < frequencies.Length; k++)
            {
                value += Math.Sin(2 * Math.PI * frequencies[k] * t) / (k + 1);
            }

            var envelope = Math.Exp(-3.5 * t / seconds) * Math.Min(1, t * 200); // weicher Einsatz, Abklingen
            samples[i] = (short)(value / 1.8 * envelope * volume * short.MaxValue * 0.8);
        }

        return samples;
    }

    /// <summary>Zwei kurze Pieptöne.</summary>
    private static short[] Beeps(double volume)
    {
        var samples = new short[(int)(SampleRate * 0.6)];
        for (var i = 0; i < samples.Length; i++)
        {
            var t = (double)i / SampleRate;
            var on = t < 0.18 || (t > 0.3 && t < 0.48);
            if (!on) continue;
            samples[i] = (short)(Math.Sin(2 * Math.PI * 1000 * t) * volume * short.MaxValue * 0.6);
        }

        return samples;
    }

    private static MemoryStream ToWav(short[] samples)
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            var dataBytes = samples.Length * 2;
            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + dataBytes);
            writer.Write("WAVE"u8.ToArray());
            writer.Write("fmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)1); // Mono
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8.ToArray());
            writer.Write(dataBytes);
            foreach (var s in samples) writer.Write(s);
        }

        stream.Position = 0;
        return stream;
    }
}
