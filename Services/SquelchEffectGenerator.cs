using NAudio.Wave;

namespace iRacing_Spotter_Generator.Services
{
    /// <summary>
    /// Generates a short synthetic radio noise/click burst (like a squelch tail)
    /// and prepends/appends it to an existing WAV file, so generated samples
    /// sound like they came through a real radio, just like iRacing's own
    /// spotter/pit voice clips do.
    /// </summary>
    public static class SquelchEffectGenerator
    {
        /// <summary>
        /// Wraps the WAV file at <paramref name="sourcePath"/> with optional squelch signals
        /// at the start and/or end, writing the result to <paramref name="destinationPath"/>.
        /// The source file's own format (sample rate / bits / channels) is preserved.
        /// </summary>
        /// <param name="sourcePath">Path to the input WAV file.</param>
        /// <param name="destinationPath">Path where the output WAV file will be written.</param>
        /// <param name="durationMs">Duration of each squelch burst in milliseconds.</param>
        /// <param name="volume">Volume of the squelch effect (0.0 to 1.0).</param>
        /// <param name="addStart">Whether to add squelch at the start. Defaults to true.</param>
        /// <param name="addEnd">Whether to add squelch at the end. Defaults to true.</param>
        public static void ApplySquelch(
            string sourcePath, string destinationPath, int durationMs, double volume,
            bool addStart = true, bool addEnd = true)
        {
            using var reader = new WaveFileReader(sourcePath);
            var format = reader.WaveFormat;

            var burst = addStart || addEnd 
                ? GenerateNoiseBurst(format, durationMs, volume) 
                : Array.Empty<byte>();

            using var writer = new WaveFileWriter(destinationPath, format);

            if (addStart)
            {
                writer.Write(burst, 0, burst.Length);
            }

            var buffer = new byte[format.AverageBytesPerSecond];
            int bytesRead;
            while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                writer.Write(buffer, 0, bytesRead);
            }

            if (addEnd)
            {
                writer.Write(burst, 0, burst.Length);
            }
        }

        private static byte[] GenerateNoiseBurst(WaveFormat format, int durationMs, double volume)
        {
            var sampleCount = (int)(format.SampleRate * (durationMs / 1000.0)) * format.Channels;
            var random = new Random();

            volume = Math.Clamp(volume, 0.0, 1.0);

            if (format.BitsPerSample == 8)
            {
                var buffer = new byte[sampleCount];
                for (var i = 0; i < sampleCount; i++)
                {
                    var fade = GetEnvelope(i, sampleCount);
                    var noise = (random.NextDouble() * 2.0 - 1.0) * volume * fade;
                    buffer[i] = (byte)Math.Clamp(128 + noise * 127, 0, 255);
                }

                return buffer;
            }
            else
            {
                var buffer = new byte[sampleCount * 2];
                for (var i = 0; i < sampleCount; i++)
                {
                    var fade = GetEnvelope(i, sampleCount);
                    var noise = (random.NextDouble() * 2.0 - 1.0) * volume * fade;
                    var sampleValue = (short)Math.Clamp(noise * short.MaxValue, short.MinValue, short.MaxValue);
                    var bytes = BitConverter.GetBytes(sampleValue);
                    buffer[i * 2] = bytes[0];
                    buffer[i * 2 + 1] = bytes[1];
                }

                return buffer;
            }
        }

        /// <summary>
        /// Simple attack/decay envelope so the burst starts with a sharp "click"
        /// and tails off into static, similar to a radio squelch opening/closing.
        /// </summary>
        private static double GetEnvelope(int index, int totalSamples)
        {
            if (totalSamples <= 1)
            {
                return 1.0;
            }

            var position = (double)index / totalSamples;
            return position < 0.1
                ? position / 0.1
                : 1.0 - (position - 0.1) / 0.9 * 0.6;
        }
    }
}
