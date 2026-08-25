using NAudio.Wave;

namespace iRacing_Spotter_Generator.Services
{
    /// <summary>
    /// Generates a short, speech-like synthetic placeholder tone (a warbling
    /// sine wave), used to let users preview how Squelch/PTT/Radio Effect/
    /// Volume settings will sound without needing a real recording or a
    /// Google API key.
    /// </summary>
    public static class TestToneGenerator
    {
        public static void GenerateTestTone(string destinationPath, int sampleRate, int bitsPerSample, int durationMs = 900)
        {
            var format = new WaveFormat(sampleRate, bitsPerSample, 1);
            using var writer = new WaveFileWriter(destinationPath, format);

            var sampleCount = (int)(sampleRate * (durationMs / 1000.0));
            var random = new Random(12345);

            for (var i = 0; i < sampleCount; i++)
            {
                var t = (double)i / sampleRate;

                // Slowly wandering base frequency plus a bit of vibrato and
                // noise, to loosely resemble the cadence of spoken words.
                var baseFreq = 160 + 60 * Math.Sin(2 * Math.PI * 1.4 * t);
                var vibrato = 15 * Math.Sin(2 * Math.PI * 6.0 * t);
                var value = Math.Sin(2 * Math.PI * (baseFreq + vibrato) * t);
                value += 0.15 * (random.NextDouble() * 2.0 - 1.0);

                // Envelope with a couple of short "syllable" pulses instead
                // of one flat tone, plus overall fade in/out.
                var syllablePosition = (t * 3.0) % 1.0;
                var syllableEnvelope = syllablePosition < 0.7 ? 1.0 : 0.3;
                var fade = Math.Min(1.0, Math.Min(t / 0.05, (durationMs / 1000.0 - t) / 0.05));
                value *= 0.6 * syllableEnvelope * Math.Clamp(fade, 0.0, 1.0);

                if (bitsPerSample == 8)
                {
                    writer.WriteByte((byte)Math.Clamp(128 + value * 127, 0, 255));
                }
                else
                {
                    var sampleValue = (short)Math.Clamp(value * short.MaxValue, short.MinValue, short.MaxValue);
                    writer.WriteByte((byte)(sampleValue & 0xFF));
                    writer.WriteByte((byte)((sampleValue >> 8) & 0xFF));
                }
            }
        }
    }
}
