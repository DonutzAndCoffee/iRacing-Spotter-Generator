using NAudio.Wave;

namespace iRacing_Spotter_Generator.Services
{
    /// <summary>
    /// Applies a flexible "radio transmission" effect to a WAV file: a
    /// bandpass filter (simulating the narrow frequency response of a radio
    /// speaker/microphone) plus optional soft-clip distortion, so generated
    /// samples sound more like they came through an actual radio.
    /// Unlike <see cref="SquelchEffectGenerator"/> (which only adds a noise
    /// burst at the start/end), this shapes the voice signal itself.
    /// </summary>
    public static class RadioEffectProcessor
    {
        /// <summary>
        /// Reads the WAV file at <paramref name="sourcePath"/>, applies a
        /// bandpass filter and optional soft-clip distortion, and writes the
        /// result to <paramref name="destinationPath"/>. The source file's
        /// format (sample rate / bits / channels) is preserved.
        /// </summary>
        /// <param name="sourcePath">Path to the input WAV file.</param>
        /// <param name="destinationPath">Path where the output WAV file will be written.</param>
        /// <param name="lowCutHz">Low cutoff frequency (Hz) of the bandpass filter (frequencies below this are attenuated).</param>
        /// <param name="highCutHz">High cutoff frequency (Hz) of the bandpass filter (frequencies above this are attenuated).</param>
        /// <param name="distortion">Amount of soft-clip distortion (0.0 = none, 1.0 = strong).</param>
        public static void Apply(
            string sourcePath, string destinationPath, int lowCutHz, int highCutHz, double distortion)
        {
            using var reader = new WaveFileReader(sourcePath);
            var format = reader.WaveFormat;

            var samples = ReadSamples(reader, format);

            var rmsBefore = ComputeRms(samples);

            ApplyBandpass(samples, format.SampleRate, lowCutHz, highCutHz);

            // The narrow-band cascade removes a lot of energy (bass and
            // treble), which makes the effect much quieter than the source
            // and, as a result, much less audible/noticeable. Restore the
            // original loudness so the timbral change (not just a volume
            // drop) is what stands out - just like a real radio, which is
            // still "loud", just narrow-band.
            NormalizeToRms(samples, rmsBefore);

            if (distortion > 0.0)
            {
                ApplyDistortion(samples, distortion);
            }

            WriteSamples(destinationPath, format, samples);
        }

        private static double[] ReadSamples(WaveFileReader reader, WaveFormat format)
        {
            var byteCount = (int)reader.Length;

            if (format.BitsPerSample == 8)
            {
                var raw = new byte[byteCount];
                reader.Read(raw, 0, raw.Length);

                var samples = new double[raw.Length];
                for (var i = 0; i < raw.Length; i++)
                {
                    samples[i] = (raw[i] - 128) / 128.0;
                }
                return samples;
            }
            else
            {
                var raw = new byte[byteCount];
                reader.Read(raw, 0, raw.Length);

                var sampleCount = raw.Length / 2;
                var samples = new double[sampleCount];
                for (var i = 0; i < sampleCount; i++)
                {
                    var value = (short)(raw[i * 2] | (raw[i * 2 + 1] << 8));
                    samples[i] = value / (double)short.MaxValue;
                }
                return samples;
            }
        }

        private static void WriteSamples(string destinationPath, WaveFormat format, double[] samples)
        {
            using var writer = new WaveFileWriter(destinationPath, format);

            if (format.BitsPerSample == 8)
            {
                var buffer = new byte[samples.Length];
                for (var i = 0; i < samples.Length; i++)
                {
                    buffer[i] = (byte)Math.Clamp(128 + samples[i] * 127, 0, 255);
                }
                writer.Write(buffer, 0, buffer.Length);
            }
            else
            {
                var buffer = new byte[samples.Length * 2];
                for (var i = 0; i < samples.Length; i++)
                {
                    var value = (short)Math.Clamp(samples[i] * short.MaxValue, short.MinValue, short.MaxValue);
                    var bytes = BitConverter.GetBytes(value);
                    buffer[i * 2] = bytes[0];
                    buffer[i * 2 + 1] = bytes[1];
                }
                writer.Write(buffer, 0, buffer.Length);
            }
        }

        /// <summary>
        /// Number of one-pole stages cascaded for each of the high-pass and
        /// low-pass filters. A single one-pole stage only rolls off at
        /// ~6 dB/octave, which is far too gentle to sound like a narrow-band
        /// radio channel - most of the "outside the band" energy is still
        /// clearly audible. Cascading several stages steepens the roll-off
        /// (~4 stages ≈ 24 dB/octave), producing the tight, honky, band-
        /// limited character of actual push-to-talk radio audio.
        /// </summary>
        private const int FilterStages = 4;

        /// <summary>
        /// Applies a cascaded one-pole high-pass followed by a cascaded
        /// one-pole low-pass filter (an RC bandpass cascade), attenuating
        /// frequencies outside [lowCutHz, highCutHz], similar to the narrow
        /// response of a radio.
        /// </summary>
        private static void ApplyBandpass(double[] samples, int sampleRate, int lowCutHz, int highCutHz)
        {
            if (lowCutHz > 0)
            {
                for (var stage = 0; stage < FilterStages; stage++)
                {
                    ApplyHighPass(samples, sampleRate, lowCutHz);
                }
            }

            if (highCutHz > 0 && highCutHz < sampleRate / 2)
            {
                for (var stage = 0; stage < FilterStages; stage++)
                {
                    ApplyLowPass(samples, sampleRate, highCutHz);
                }
            }
        }

        private static void ApplyHighPass(double[] samples, int sampleRate, int cutoffHz)
        {
            var rc = 1.0 / (2.0 * Math.PI * cutoffHz);
            var dt = 1.0 / sampleRate;
            var alpha = rc / (rc + dt);

            var previousInput = 0.0;
            var previousOutput = 0.0;
            for (var i = 0; i < samples.Length; i++)
            {
                var output = alpha * (previousOutput + samples[i] - previousInput);
                previousInput = samples[i];
                previousOutput = output;
                samples[i] = output;
            }
        }

        private static void ApplyLowPass(double[] samples, int sampleRate, int cutoffHz)
        {
            var rc = 1.0 / (2.0 * Math.PI * cutoffHz);
            var dt = 1.0 / sampleRate;
            var alpha = dt / (rc + dt);

            var previousOutput = 0.0;
            for (var i = 0; i < samples.Length; i++)
            {
                var output = previousOutput + alpha * (samples[i] - previousOutput);
                previousOutput = output;
                samples[i] = output;
            }
        }

        /// <summary>
        /// Computes the root-mean-square (average loudness) of the signal.
        /// </summary>
        private static double ComputeRms(double[] samples)
        {
            if (samples.Length == 0)
            {
                return 0.0;
            }

            var sumOfSquares = 0.0;
            for (var i = 0; i < samples.Length; i++)
            {
                sumOfSquares += samples[i] * samples[i];
            }

            return Math.Sqrt(sumOfSquares / samples.Length);
        }

        /// <summary>
        /// Rescales the signal so its RMS loudness matches
        /// <paramref name="targetRms"/>, compensating for the level lost to
        /// the bandpass filter's roll-off. The result is peak-limited to
        /// avoid clipping from the gain boost.
        /// </summary>
        private static void NormalizeToRms(double[] samples, double targetRms)
        {
            if (targetRms <= 0.0)
            {
                return;
            }

            var currentRms = ComputeRms(samples);
            if (currentRms <= 0.0001)
            {
                return;
            }

            var gain = targetRms / currentRms;

            // Avoid boosting so hard that the loudest peaks would clip;
            // scale the gain down if necessary based on the current peak.
            var peak = 0.0;
            for (var i = 0; i < samples.Length; i++)
            {
                var abs = Math.Abs(samples[i]);
                if (abs > peak)
                {
                    peak = abs;
                }
            }

            if (peak > 0.0001)
            {
                var maxGain = 0.98 / peak;
                gain = Math.Min(gain, maxGain);
            }

            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] *= gain;
            }
        }

        /// <summary>
        /// Applies a tanh-based soft-clip distortion, driving the signal
        /// harder for higher <paramref name="amount"/> values (0-1) to emulate
        /// an overdriven/clipped radio transmitter.
        /// </summary>
        private static void ApplyDistortion(double[] samples, double amount)
        {
            amount = Math.Clamp(amount, 0.0, 1.0);
            var drive = 1.0 + amount * 9.0;
            var normalize = Math.Tanh(drive);

            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = Math.Tanh(samples[i] * drive) / normalize;
            }
        }
    }
}
