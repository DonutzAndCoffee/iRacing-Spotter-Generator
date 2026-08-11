using NAudio.Wave;

namespace iRacing_Spotter_Generator.Services
{
    /// <summary>
    /// Converts WAV audio to a target sample rate / bits per sample (mono),
    /// used to make Google AI generated speech sound like iRacing's own
    /// spotter radio (which plays samples back at 5512 Hz / 8-bit by default).
    /// </summary>
    public static class AudioFormatConverter
    {
        public static void ConvertFile(string sourcePath, string destinationPath, int sampleRate, int bitsPerSample)
        {
            using var reader = new WaveFileReader(sourcePath);

            var targetFormat = new WaveFormat(sampleRate, bitsPerSample, 1);

            using var resampled = new MediaFoundationResampler(reader, targetFormat)
            {
                ResamplerQuality = 60
            };

            WaveFileWriter.CreateWaveFile(destinationPath, resampled);
        }
    }
}
