using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using iRacing_Spotter_Generator.Models;
using NAudio.Wave;

namespace iRacing_Spotter_Generator.Services
{
    /// <summary>
    /// Manages long, imported "source recordings" (e.g. full race sessions)
    /// that individual message takes can later be cut out from. Handles
    /// import/conversion (WAV/MP3/MP4 -> normalized WAV), persistence of the
    /// catalog (including which regions have already been used), stored
    /// under %AppData%\iRacingSpotterGenerator\SourceRecordings.
    /// </summary>
    public static class SourceRecordingService
    {
        private static readonly string RootFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "iRacingSpotterGenerator", "SourceRecordings");

        private static readonly string CatalogFilePath = Path.Combine(RootFolder, "catalog.json");

        public static List<SourceRecording> Load()
        {
            try
            {
                if (File.Exists(CatalogFilePath))
                {
                    var json = File.ReadAllText(CatalogFilePath);
                    var recordings = JsonSerializer.Deserialize<List<SourceRecording>>(json);
                    if (recordings is not null)
                    {
                        // Skip entries whose backing file has been removed
                        // externally so the list doesn't show broken rows.
                        return recordings.Where(r => File.Exists(r.FilePath)).ToList();
                    }
                }
            }
            catch (IOException)
            {
                // Fall back to an empty catalog if it can't be read.
            }
            catch (JsonException)
            {
                // Fall back to an empty catalog if it's corrupted.
            }

            return new List<SourceRecording>();
        }

        public static void Save(IReadOnlyList<SourceRecording> recordings)
        {
            Directory.CreateDirectory(RootFolder);
            var json = JsonSerializer.Serialize(recordings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(CatalogFilePath, json);
        }

        /// <summary>
        /// Imports a WAV/MP3/MP4 file as a new source recording: extracts
        /// the audio track (for MP4) and converts everything to a single
        /// normalized WAV so the existing trim/waveform helpers (which only
        /// understand WAV) can operate on it unchanged.
        /// </summary>
        public static SourceRecording Import(string sourcePath, int sampleRate, int bitsPerSample)
        {
            Directory.CreateDirectory(RootFolder);

            var id = Guid.NewGuid().ToString("N");
            var destinationPath = Path.Combine(RootFolder, $"{id}.wav");

            using (var reader = new MediaFoundationReader(sourcePath))
            {
                var targetFormat = new WaveFormat(sampleRate, bitsPerSample, 1);
                using var resampled = new MediaFoundationResampler(reader, targetFormat)
                {
                    ResamplerQuality = 60
                };
                WaveFileWriter.CreateWaveFile(destinationPath, resampled);
            }

            var duration = AudioTrimHelper.GetDuration(destinationPath);

            return new SourceRecording
            {
                Id = id,
                FilePath = destinationPath,
                Name = Path.GetFileNameWithoutExtension(sourcePath),
                DurationSeconds = duration.TotalSeconds
            };
        }

        /// <summary>
        /// Deletes the backing WAV file (and cached waveform peaks, if any)
        /// for a source recording.
        /// </summary>
        public static void DeleteFiles(SourceRecording recording)
        {
            try
            {
                if (File.Exists(recording.FilePath))
                {
                    File.Delete(recording.FilePath);
                }

                var peaksPath = WaveformPeakCache.GetCachePath(recording.FilePath);
                if (File.Exists(peaksPath))
                {
                    File.Delete(peaksPath);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup; leftover files don't break anything.
            }
        }
    }
}
