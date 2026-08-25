using System;
using System.IO;
using NAudio.Wave;

namespace iRacing_Spotter_Generator.Services
{
    /// <summary>
    /// Generates short synthetic "push-to-talk" (PTT) signal tone sequences -
    /// a rising two-tone chirp for the start and a falling two-tone chirp for
    /// the end of a transmission, like a real analog radio - and
    /// prepends/appends them to an existing WAV file. This is an
    /// alternative/addition to <see cref="SquelchEffectGenerator"/>'s noise
    /// burst. Custom WAV files can be used instead of the synthesized tones
    /// for either signal.
    /// </summary>
    public static class PttEffectGenerator
    {
        /// <summary>
        /// Wraps the WAV file at <paramref name="sourcePath"/> with optional PTT
        /// start/stop signals, writing the result to <paramref name="destinationPath"/>.
        /// The source file's own format (sample rate / bits / channels) is preserved.
        /// </summary>
        public static void ApplyPtt(
            string sourcePath, string destinationPath, int durationMs, double volume,
            int startFrequencyHz, int endFrequencyHz,
            bool addStart = true, bool addEnd = true,
            string? startFilePath = null, string? endFilePath = null)
        {
            using var reader = new WaveFileReader(sourcePath);
            var format = reader.WaveFormat;

            var startBurst = addStart
                ? GetBurst(format, durationMs, volume, startFrequencyHz, startFilePath, isRising: true)
                : Array.Empty<byte>();
            var endBurst = addEnd
                ? GetBurst(format, durationMs, volume, endFrequencyHz, endFilePath, isRising: false)
                : Array.Empty<byte>();

            using var writer = new WaveFileWriter(destinationPath, format);

            if (addStart)
            {
                writer.Write(startBurst, 0, startBurst.Length);
            }

            var buffer = new byte[format.AverageBytesPerSecond];
            int bytesRead;
            while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                writer.Write(buffer, 0, bytesRead);
            }

            if (addEnd)
            {
                writer.Write(endBurst, 0, endBurst.Length);
            }
        }

        private static byte[] GetBurst(WaveFormat format, int durationMs, double volume, int frequencyHz, string? customFilePath, bool isRising)
        {
            if (!string.IsNullOrWhiteSpace(customFilePath) && File.Exists(customFilePath))
            {
                return ReadCustomBurst(format, customFilePath);
            }

            return GenerateTone(format, durationMs, volume, frequencyHz, isRising);
        }

        /// <summary>
        /// Converts a user-supplied WAV file to the target format so it seamlessly
        /// concatenates with the generated speech, and returns its raw sample bytes.
        /// </summary>
        private static byte[] ReadCustomBurst(WaveFormat format, string filePath)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"spgen_ptt_src_{Guid.NewGuid():N}.wav");
            try
            {
                AudioFormatConverter.ConvertFile(filePath, tempPath, format.SampleRate, format.BitsPerSample);

                using var reader = new WaveFileReader(tempPath);
                var buffer = new byte[reader.Length];
                reader.Read(buffer, 0, buffer.Length);
                return buffer;
            }
            finally
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                    // Ignore cleanup failures for temp conversion files.
                }
            }
        }

        private static byte[] GenerateTone(WaveFormat format, int durationMs, double volume, int frequencyHz, bool isRising)
        {
            // Real analog radios don't signal PTT start/stop with a single
            // steady tone, but with a short sequence of two quick tone
            // "chirps" - a rising pair (low then high) for the start signal,
            // a falling pair (high then low) for the stop signal. We
            // approximate that here with a two-tone sequence derived from the
            // configured base frequency, each half taking up the configured
            // total duration, separated by a brief silent gap.
            const double secondToneRatio = 1.5;
            const double gapFraction = 0.08;

            var lowFrequencyHz = frequencyHz;
            var highFrequencyHz = (int)Math.Round(frequencyHz * secondToneRatio);
            var firstFrequencyHz = isRising ? lowFrequencyHz : highFrequencyHz;
            var secondFrequencyHz = isRising ? highFrequencyHz : lowFrequencyHz;

            var totalSampleCount = (int)(format.SampleRate * (durationMs / 1000.0));
            var gapSampleCount = (int)(totalSampleCount * gapFraction);
            var toneSampleCount = Math.Max(1, (totalSampleCount - gapSampleCount) / 2);

            var firstTone = GenerateToneSegment(format, toneSampleCount, volume, firstFrequencyHz);
            var secondTone = GenerateToneSegment(format, toneSampleCount, volume, secondFrequencyHz);
            var gap = new byte[gapSampleCount * format.Channels * (format.BitsPerSample == 8 ? 1 : 2)];
            if (format.BitsPerSample == 8)
            {
                Array.Fill(gap, (byte)128);
            }

            var result = new byte[firstTone.Length + gap.Length + secondTone.Length];
            Buffer.BlockCopy(firstTone, 0, result, 0, firstTone.Length);
            Buffer.BlockCopy(gap, 0, result, firstTone.Length, gap.Length);
            Buffer.BlockCopy(secondTone, 0, result, firstTone.Length + gap.Length, secondTone.Length);
            return result;
        }

        private static byte[] GenerateToneSegment(WaveFormat format, int sampleCount, double volume, int frequencyHz)
        {
            var channels = format.Channels;

            volume = Math.Clamp(volume, 0.0, 1.0);

            if (format.BitsPerSample == 8)
            {
                var buffer = new byte[sampleCount * channels];
                for (var i = 0; i < sampleCount; i++)
                {
                    var fade = GetEnvelope(i, sampleCount);
                    var tone = Math.Sin(2 * Math.PI * frequencyHz * i / format.SampleRate) * volume * fade;
                    var value = (byte)Math.Clamp(128 + tone * 127, 0, 255);
                    for (var c = 0; c < channels; c++)
                    {
                        buffer[i * channels + c] = value;
                    }
                }

                return buffer;
            }
            else
            {
                var buffer = new byte[sampleCount * channels * 2];
                for (var i = 0; i < sampleCount; i++)
                {
                    var fade = GetEnvelope(i, sampleCount);
                    var tone = Math.Sin(2 * Math.PI * frequencyHz * i / format.SampleRate) * volume * fade;
                    var sampleValue = (short)Math.Clamp(tone * short.MaxValue, short.MinValue, short.MaxValue);
                    var bytes = BitConverter.GetBytes(sampleValue);
                    for (var c = 0; c < channels; c++)
                    {
                        var offset = (i * channels + c) * 2;
                        buffer[offset] = bytes[0];
                        buffer[offset + 1] = bytes[1];
                    }
                }

                return buffer;
            }
        }

        /// <summary>
        /// Quick attack / short sustain / quick release envelope, so the beep
        /// starts and ends cleanly without clicking, like a real PTT tone.
        /// </summary>
        private static double GetEnvelope(int index, int totalSamples)
        {
            if (totalSamples <= 1)
            {
                return 1.0;
            }

            var position = (double)index / totalSamples;

            if (position < 0.1)
            {
                return position / 0.1;
            }

            if (position > 0.85)
            {
                return (1.0 - position) / 0.15;
            }

            return 1.0;
        }
    }
}
