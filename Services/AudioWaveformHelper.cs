using NAudio.Wave;

namespace iRacing_Spotter_Generator.Services
{
    /// <summary>
    /// Computes a simplified amplitude envelope (peaks per time bucket) for a
    /// WAV file, used to draw a waveform preview in the recording/trim UI.
    /// </summary>
    public static class AudioWaveformHelper
    {
        /// <summary>
        /// Returns <paramref name="bucketCount"/> normalized amplitude values (0.0 - 1.0),
        /// each representing the peak absolute sample value within that time slice.
        /// </summary>
        public static float[] GetPeaks(string filePath, int bucketCount)
        {
            if (bucketCount <= 0)
            {
                return Array.Empty<float>();
            }

            using var reader = new WaveFileReader(filePath);
            var sampleProvider = reader.ToSampleProvider();
            var channels = Math.Max(1, sampleProvider.WaveFormat.Channels);

            var totalFrames = reader.WaveFormat.BlockAlign > 0
                ? reader.Length / reader.WaveFormat.BlockAlign
                : 0;

            if (totalFrames <= 0)
            {
                return new float[bucketCount];
            }

            var peaks = new float[bucketCount];
            var framesPerBucket = Math.Max(1.0, (double)totalFrames / bucketCount);

            var buffer = new float[4096 * channels];
            long framesRead = 0;
            int bucketIndex = 0;
            var bucketFrameLimit = framesPerBucket;

            int read;
            while ((read = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
            {
                var framesInBuffer = read / channels;

                for (var frame = 0; frame < framesInBuffer; frame++)
                {
                    var maxAbs = 0f;
                    for (var ch = 0; ch < channels; ch++)
                    {
                        var value = Math.Abs(buffer[frame * channels + ch]);
                        if (value > maxAbs)
                        {
                            maxAbs = value;
                        }
                    }

                    if (bucketIndex < bucketCount && maxAbs > peaks[bucketIndex])
                    {
                        peaks[bucketIndex] = maxAbs;
                    }

                    framesRead++;
                    if (framesRead >= bucketFrameLimit && bucketIndex < bucketCount - 1)
                    {
                        bucketIndex++;
                        bucketFrameLimit += framesPerBucket;
                    }
                }
            }

            return peaks;
        }
    }
}
