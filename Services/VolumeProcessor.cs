using System.IO;
using NAudio.Wave;

namespace iRacing_Spotter_Generator.Services
{
    /// <summary>
    /// Applies a simple linear gain to a WAV file, so users can boost or
    /// attenuate the overall output volume of generated/recorded samples
    /// (e.g. because iRacing plays spotter clips comparatively quiet).
    /// </summary>
    public static class VolumeProcessor
    {
        /// <summary>
        /// Applies <paramref name="gain"/> (1.0 = unchanged, &gt;1.0 = louder,
        /// &lt;1.0 = quieter) to the WAV file at <paramref name="sourcePath"/>,
        /// writing the result to <paramref name="destinationPath"/>. Samples
        /// are clipped to avoid wraparound/distortion when boosting volume.
        /// Does nothing (simple copy) when <paramref name="gain"/> is 1.0.
        /// </summary>
        public static void Apply(string sourcePath, string destinationPath, double gain)
        {
            if (Math.Abs(gain - 1.0) < 0.0001)
            {
                if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(sourcePath, destinationPath, overwrite: true);
                }

                return;
            }

            using var reader = new WaveFileReader(sourcePath);
            var format = reader.WaveFormat;
            using var writer = new WaveFileWriter(destinationPath, format);

            if (format.BitsPerSample == 8)
            {
                var buffer = new byte[format.AverageBytesPerSecond];
                int bytesRead;
                while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (var i = 0; i < bytesRead; i++)
                    {
                        var centered = buffer[i] - 128;
                        var scaled = (int)Math.Round(centered * gain);
                        buffer[i] = (byte)(Math.Clamp(scaled, -128, 127) + 128);
                    }

                    writer.Write(buffer, 0, bytesRead);
                }
            }
            else
            {
                var buffer = new byte[format.AverageBytesPerSecond];
                int bytesRead;
                while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (var i = 0; i + 1 < bytesRead; i += 2)
                    {
                        var sample = BitConverter.ToInt16(buffer, i);
                        var scaled = (int)Math.Round(sample * gain);
                        var clamped = (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
                        var bytes = BitConverter.GetBytes(clamped);
                        buffer[i] = bytes[0];
                        buffer[i + 1] = bytes[1];
                    }

                    writer.Write(buffer, 0, bytesRead);
                }
            }
        }
    }
}
