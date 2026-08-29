using System.IO;

namespace iRacing_Spotter_Generator.Services
{
    /// <summary>
    /// Caches the time-based amplitude envelope (peaks) computed by
    /// <see cref="AudioWaveformHelper"/> for a WAV file next to the file
    /// itself, so long source recordings (up to ~60 minutes) don't need to
    /// be fully re-decoded every time their waveform is displayed.
    /// The cache uses a fixed, time-based bucket duration (not pixel-based)
    /// so it stays valid regardless of the window size; the UI can further
    /// downsample the cached peaks for display as needed.
    /// </summary>
    public static class WaveformPeakCache
    {
        /// <summary>
        /// Roughly one peak every 100ms, which is far more than enough detail
        /// for visual navigation even when later downsampled for display.
        /// </summary>
        private const double BucketDurationSeconds = 0.1;

        public static string GetCachePath(string wavFilePath) => wavFilePath + ".peaks";

        /// <summary>
        /// Returns cached peaks for <paramref name="wavFilePath"/>, computing
        /// and caching them first if no valid cache exists yet (or if the
        /// source file has changed since the cache was written).
        /// </summary>
        public static float[] GetOrComputePeaks(string wavFilePath)
        {
            var cachePath = GetCachePath(wavFilePath);
            var sourceInfo = new FileInfo(wavFilePath);

            if (File.Exists(cachePath))
            {
                var cacheInfo = new FileInfo(cachePath);
                if (cacheInfo.LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc)
                {
                    var cached = TryReadCache(cachePath);
                    if (cached is not null)
                    {
                        return cached;
                    }
                }
            }

            var duration = AudioTrimHelper.GetDuration(wavFilePath);
            var bucketCount = Math.Max(1, (int)(duration.TotalSeconds / BucketDurationSeconds));
            var peaks = AudioWaveformHelper.GetPeaks(wavFilePath, bucketCount);

            TryWriteCache(cachePath, peaks);

            return peaks;
        }

        private static float[]? TryReadCache(string cachePath)
        {
            try
            {
                using var stream = File.OpenRead(cachePath);
                using var reader = new BinaryReader(stream);
                var count = reader.ReadInt32();
                var peaks = new float[count];
                for (var i = 0; i < count; i++)
                {
                    peaks[i] = reader.ReadSingle();
                }

                return peaks;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static void TryWriteCache(string cachePath, float[] peaks)
        {
            try
            {
                using var stream = File.Create(cachePath);
                using var writer = new BinaryWriter(stream);
                writer.Write(peaks.Length);
                foreach (var peak in peaks)
                {
                    writer.Write(peak);
                }
            }
            catch (IOException)
            {
                // Caching is best-effort; a failed write just means slower
                // reloads next time.
            }
        }

        /// <summary>
        /// Downsamples cached peaks to a smaller display-friendly bucket
        /// count (e.g. matching the current waveform canvas width), taking
        /// the max amplitude within each display bucket.
        /// </summary>
        public static float[] Downsample(float[] peaks, int targetBucketCount)
        {
            if (peaks.Length == 0 || targetBucketCount <= 0)
            {
                return Array.Empty<float>();
            }

            if (peaks.Length <= targetBucketCount)
            {
                return peaks;
            }

            var result = new float[targetBucketCount];
            var sourcePerTarget = (double)peaks.Length / targetBucketCount;

            for (var i = 0; i < targetBucketCount; i++)
            {
                var startIndex = (int)(i * sourcePerTarget);
                var endIndex = (int)((i + 1) * sourcePerTarget);
                endIndex = Math.Min(endIndex, peaks.Length);
                if (endIndex <= startIndex)
                {
                    endIndex = startIndex + 1;
                }

                var max = 0f;
                for (var j = startIndex; j < endIndex && j < peaks.Length; j++)
                {
                    if (peaks[j] > max)
                    {
                        max = peaks[j];
                    }
                }

                result[i] = max;
            }

            return result;
        }
    }
}
