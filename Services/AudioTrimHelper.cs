using NAudio.Wave;

namespace iRacing_Spotter_Generator.Services
{
    /// <summary>
    /// Simple helper to inspect and trim WAV files, used by the recording
    /// dialog's cut tool so the operator can remove the microphone
    /// click/silence at the start and end of a take before using it.
    /// </summary>
    public static class AudioTrimHelper
    {
        public static TimeSpan GetDuration(string filePath)
        {
            using var reader = new WaveFileReader(filePath);
            return reader.TotalTime;
        }

        /// <summary>
        /// Writes the portion of <paramref name="sourcePath"/> between
        /// <paramref name="start"/> and <paramref name="end"/> to <paramref name="destinationPath"/>,
        /// preserving the original WAV format.
        /// </summary>
        public static void Trim(string sourcePath, string destinationPath, TimeSpan start, TimeSpan end)
        {
            using var reader = new WaveFileReader(sourcePath);

            if (start < TimeSpan.Zero)
            {
                start = TimeSpan.Zero;
            }

            if (end > reader.TotalTime)
            {
                end = reader.TotalTime;
            }

            if (end <= start)
            {
                throw new InvalidOperationException("Das Ende muss nach dem Start liegen.");
            }

            var format = reader.WaveFormat;
            var startByte = AlignToBlock((long)(start.TotalSeconds * format.AverageBytesPerSecond), format.BlockAlign);
            var endByte = AlignToBlock((long)(end.TotalSeconds * format.AverageBytesPerSecond), format.BlockAlign);
            endByte = Math.Min(endByte, reader.Length);

            reader.Position = startByte;

            using var writer = new WaveFileWriter(destinationPath, format);

            var buffer = new byte[format.AverageBytesPerSecond];
            var remaining = endByte - startByte;

            while (remaining > 0)
            {
                var toRead = (int)Math.Min(buffer.Length, remaining);
                var bytesRead = reader.Read(buffer, 0, toRead);
                if (bytesRead <= 0)
                {
                    break;
                }

                writer.Write(buffer, 0, bytesRead);
                remaining -= bytesRead;
            }
        }

        private static long AlignToBlock(long position, int blockAlign)
        {
            if (blockAlign <= 0)
            {
                return position;
            }

            return position - position % blockAlign;
        }
    }
}
