using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using iRacing_Spotter_Generator.Models;

namespace iRacing_Spotter_Generator.Services
{
    /// <summary>
    /// Tracks, per MsgId, a hash of everything that influences the generated
    /// wav file, so that re-exporting a pack only regenerates wav files whose
    /// relevant data actually changed since the last export.
    /// </summary>
    internal class PackManifest
    {
        public Dictionary<string, string> EntryHashes { get; set; } = new();

        private static string GetManifestPath(string outputFolder) =>
            Path.Combine(outputFolder, ".spgen_manifest.json");

        public static PackManifest Load(string outputFolder)
        {
            var path = GetManifestPath(outputFolder);
            if (!File.Exists(path))
            {
                return new PackManifest();
            }

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<PackManifest>(json) ?? new PackManifest();
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return new PackManifest();
            }
        }

        public void Save(string outputFolder)
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetManifestPath(outputFolder), json);
        }
    }

    public class PackGenerationOptions
    {
        public required string OutputFolder { get; set; }
        public string? GoogleApiKey { get; set; }

        /// <summary>
        /// Google voice used for rows that don't have their own explicit voice selected.
        /// </summary>
        public string? DefaultGoogleVoiceName { get; set; }

        /// <summary>
        /// Sample rate (Hz) that generated Google AI audio, as well as raw
        /// (high quality) recorded takes, are downsampled to on export so
        /// they sound like iRacing's own spotter radio.
        /// </summary>
        public int GoogleOutputSampleRate { get; set; } = 5512;

        /// <summary>
        /// Bits per sample that generated Google AI audio is downsampled to.
        /// </summary>
        public int GoogleOutputBitsPerSample { get; set; } = 8;

        /// <summary>
        /// Whether a short radio-style noise/click burst is automatically added
        /// at the start and end of every generated sample.
        /// </summary>
        public bool SquelchEnabled { get; set; } = true;

        /// <summary>
        /// Duration (in milliseconds) of the noise/click burst added at the
        /// start and end of every generated sample.
        /// </summary>
        public int SquelchDurationMs { get; set; } = 150;

        /// <summary>
        /// Volume (0.0 - 1.0) of the noise/click burst.
        /// </summary>
        public double SquelchVolume { get; set; } = 0.5;

        /// <summary>
        /// Whether the flexible radio effect (bandpass filter + optional
        /// distortion) is applied to every generated sample.
        /// </summary>
        public bool RadioEffectEnabled { get; set; } = false;

        /// <summary>
        /// Low cutoff frequency (Hz) of the radio effect's bandpass filter.
        /// </summary>
        public int RadioEffectLowCutHz { get; set; } = 300;

        /// <summary>
        /// High cutoff frequency (Hz) of the radio effect's bandpass filter.
        /// </summary>
        public int RadioEffectHighCutHz { get; set; } = 3000;

        /// <summary>
        /// Amount of soft-clip distortion applied by the radio effect (0.0 - 1.0).
        /// </summary>
        public double RadioEffectDistortion { get; set; } = 0.2;

        /// <summary>
        /// Whether a synthetic PTT (push-to-talk) Roger Beep tone is
        /// automatically added to the end of every generated sample,
        /// independently of the squelch noise burst.
        /// </summary>
        public bool PttEnabled { get; set; } = false;

        /// <summary>
        /// Duration (in milliseconds) of the PTT Roger Beep.
        /// </summary>
        public int PttDurationMs { get; set; } = 200;

        /// <summary>
        /// Volume (0.0 - 1.0) of the PTT Roger Beep.
        /// </summary>
        public double PttVolume { get; set; } = 0.5;

        /// <summary>
        /// Frequency (Hz) of the synthesized "stop talking" Roger Beep.
        /// </summary>
        public int PttEndFrequencyHz { get; set; } = 800;

        /// <summary>
        /// Optional custom WAV file used instead of the synthesized Roger Beep.
        /// </summary>
        public string? PttEndFilePath { get; set; }

        /// <summary>
        /// Output volume/gain applied to every generated sample (1.0 = unchanged).
        /// </summary>
        public double OutputVolume { get; set; } = 1.0;

        /// <summary>
        /// All MsgIds that must be present
        /// even if every row for them was removed (written as NULL, NULL in that case).
        /// </summary>
        public IEnumerable<string>? RequiredMsgIds { get; set; }

        /// <summary>
        /// When true (the default), re-exporting an already generated pack
        /// skips wav files whose underlying data (text, voice, recording,
        /// squelch settings, ...) hasn't changed since the last export,
        /// saving time and AI usage. When false, every enabled message is
        /// regenerated regardless of the manifest.
        /// </summary>
        public bool OnlyGenerateChanged { get; set; } = true;
    }

    public class PackGenerationProgress
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public string? CurrentMsgId { get; set; }
    }

    /// <summary>
    /// Generates an iRacing spotter pack: produces a WAV file per enabled
    /// spotter message (via Google Cloud AI TTS or a recorded take,
    /// depending on each row's SourceType) and writes the spmsg.ini file.
    /// </summary>
    public static class PackGenerator
    {
        /// <summary>
        /// Applies the flexible radio effect (bandpass filter + optional
        /// distortion) to <paramref name="wavPath"/> in place, if enabled
        /// in <paramref name="options"/> and not opted out for <paramref name="message"/>.
        /// </summary>
        private static void ApplyRadioEffectIfEnabled(PackGenerationOptions options, SpotterMessage message, string wavPath)
        {
            if (!options.RadioEffectEnabled || !message.AddRadioEffect)
            {
                return;
            }

            var processedPath = Path.Combine(Path.GetTempPath(), $"spgen_radio_{Guid.NewGuid():N}.wav");
            try
            {
                RadioEffectProcessor.Apply(
                    wavPath, processedPath,
                    options.RadioEffectLowCutHz, options.RadioEffectHighCutHz, options.RadioEffectDistortion);
                File.Copy(processedPath, wavPath, overwrite: true);
            }
            finally
            {
                try
                {
                    File.Delete(processedPath);
                }
                catch (IOException)
                {
                    // Ignore cleanup failures for temp radio-effect files.
                }
            }
        }

        /// <summary>
        /// Applies the PTT Roger Beep to <paramref name="wavPath"/> in place,
        /// if enabled in <paramref name="options"/>, independently of squelch.
        /// </summary>
        private static void ApplyPttIfEnabled(PackGenerationOptions options, SpotterMessage message, string wavPath)
        {
            if (!options.PttEnabled)
            {
                return;
            }

            var processedPath = Path.Combine(Path.GetTempPath(), $"spgen_ptt_{Guid.NewGuid():N}.wav");
            try
            {
                PttEffectGenerator.ApplyPtt(
                    wavPath, processedPath, options.PttDurationMs, options.PttVolume,
                    options.PttEndFrequencyHz,
                    message.AddPttEnd,
                    options.PttEndFilePath);
                File.Copy(processedPath, wavPath, overwrite: true);
            }
            finally
            {
                try
                {
                    File.Delete(processedPath);
                }
                catch (IOException)
                {
                    // Ignore cleanup failures for temp PTT-effect files.
                }
            }
        }

        /// <summary>
        /// Applies just the closing squelch tail to <paramref name="wavPath"/>
        /// in place. Used when PTT is enabled, so the realistic effect order
        /// (squelch-open, speech, Roger Beep, squelch-close) can be achieved
        /// by deferring the squelch-close noise until after the Roger Beep
        /// has been appended.
        /// </summary>
        private static void ApplySquelchEndIfNeeded(PackGenerationOptions options, SpotterMessage message, string wavPath)
        {
            if (!options.SquelchEnabled || !options.PttEnabled || !message.AddSquelchEnd)
            {
                return;
            }

            var processedPath = Path.Combine(Path.GetTempPath(), $"spgen_squelchend_{Guid.NewGuid():N}.wav");
            try
            {
                SquelchEffectGenerator.ApplySquelch(
                    wavPath, processedPath, options.SquelchDurationMs, options.SquelchVolume,
                    addStart: false, addEnd: true);
                File.Copy(processedPath, wavPath, overwrite: true);
            }
            finally
            {
                try
                {
                    File.Delete(processedPath);
                }
                catch (IOException)
                {
                    // Ignore cleanup failures for temp squelch-effect files.
                }
            }
        }

        /// <summary>
        /// Applies the configured output volume/gain to <paramref name="wavPath"/>
        /// in place, if it differs from 1.0 (unchanged).
        /// </summary>
        private static void ApplyVolumeIfNeeded(PackGenerationOptions options, string wavPath)
        {
            if (Math.Abs(options.OutputVolume - 1.0) < 0.0001)
            {
                return;
            }

            var processedPath = Path.Combine(Path.GetTempPath(), $"spgen_volume_{Guid.NewGuid():N}.wav");
            try
            {
                VolumeProcessor.Apply(wavPath, processedPath, options.OutputVolume);
                File.Copy(processedPath, wavPath, overwrite: true);
            }
            finally
            {
                try
                {
                    File.Delete(processedPath);
                }
                catch (IOException)
                {
                    // Ignore cleanup failures for temp volume files.
                }
            }
        }

        public static async Task GenerateAsync(
            IReadOnlyList<SpotterMessage> messages,
            PackGenerationOptions options,
            IProgress<PackGenerationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(options.OutputFolder);

            var toGenerate = messages
                .Where(m => m.Enabled && !string.IsNullOrWhiteSpace(m.Text))
                .ToList();

            GoogleTtsClient? googleClient = !string.IsNullOrWhiteSpace(options.GoogleApiKey)
                ? new GoogleTtsClient(options.GoogleApiKey)
                : null;

            var manifest = PackManifest.Load(options.OutputFolder);

            for (var i = 0; i < toGenerate.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var message = toGenerate[i];
                var wavFileName = string.IsNullOrWhiteSpace(message.WavFileName)
                    ? message.MsgId + ".wav"
                    : message.WavFileName;
                var wavPath = Path.Combine(options.OutputFolder, wavFileName);

                var currentHash = ComputeMessageHash(message, options);
                if (options.OnlyGenerateChanged &&
                    File.Exists(wavPath) &&
                    manifest.EntryHashes.TryGetValue(wavFileName, out var previousHash) &&
                    previousHash == currentHash)
                {
                    progress?.Report(new PackGenerationProgress
                    {
                        Current = i + 1,
                        Total = toGenerate.Count,
                        CurrentMsgId = message.MsgId
                    });
                    continue;
                }

                switch (message.SourceType)
                {
                    case AudioSourceType.Recording:
                        if (string.IsNullOrWhiteSpace(message.RecordedTakePath) || !File.Exists(message.RecordedTakePath))
                        {
                            throw new InvalidOperationException(
                                $"'{message.MsgId}' is set to use a recording, but no take has been recorded yet.");
                        }

                        // Recordings are captured in high quality; downsample
                        // to the target quality only now, at export time.
                        var convertedRecordingPath = Path.Combine(
                            Path.GetTempPath(), $"spgen_rec_conv_{Guid.NewGuid():N}.wav");
                        try
                        {
                            AudioFormatConverter.ConvertFile(
                                message.RecordedTakePath, convertedRecordingPath,
                                options.GoogleOutputSampleRate, options.GoogleOutputBitsPerSample);
                            ApplyRadioEffectIfEnabled(options, message, convertedRecordingPath);

                            if (options.SquelchEnabled)
                            {
                                // Defer the squelch-close tail until after the
                                // Roger Beep when PTT is enabled, so the
                                // realistic order is squelch-open, speech,
                                // Roger Beep, squelch-close.
                                SquelchEffectGenerator.ApplySquelch(
                                    convertedRecordingPath, wavPath, options.SquelchDurationMs, options.SquelchVolume,
                                    message.AddSquelchStart, message.AddSquelchEnd && !options.PttEnabled);
                            }
                            else
                            {
                                File.Copy(convertedRecordingPath, wavPath, overwrite: true);
                            }

                            ApplyPttIfEnabled(options, message, wavPath);
                            ApplySquelchEndIfNeeded(options, message, wavPath);
                            ApplyVolumeIfNeeded(options, wavPath);
                        }
                        finally
                        {
                            try
                            {
                                File.Delete(convertedRecordingPath);
                            }
                            catch (IOException)
                            {
                                // Ignore cleanup failures for temp conversion files.
                            }
                        }
                        break;

                    case AudioSourceType.GoogleAi:
                    default:
                        if (googleClient is null)
                        {
                            throw new InvalidOperationException(
                                $"'{message.MsgId}' is set to use a Google AI voice, but no Google API key is configured.");
                        }

                        var voiceName = string.IsNullOrWhiteSpace(message.GoogleVoiceName)
                            ? options.DefaultGoogleVoiceName
                            : message.GoogleVoiceName;

                        if (string.IsNullOrWhiteSpace(voiceName))
                        {
                            throw new InvalidOperationException(
                                $"'{message.MsgId}' is set to use a Google AI voice, but no voice has been selected and no default voice is configured.");
                        }

                        var languageCode = GoogleTtsClient.GetLanguageCodeFromVoiceName(voiceName);
                        var audioBytes = await googleClient.SynthesizeAsync(
                            message.Text, voiceName, languageCode, cancellationToken);

                        var tempWavPath = Path.Combine(Path.GetTempPath(), $"spgen_{Guid.NewGuid():N}.wav");
                        var convertedWavPath = Path.Combine(Path.GetTempPath(), $"spgen_conv_{Guid.NewGuid():N}.wav");
                        try
                        {
                            await File.WriteAllBytesAsync(tempWavPath, audioBytes, cancellationToken);
                            AudioFormatConverter.ConvertFile(
                                tempWavPath, convertedWavPath, options.GoogleOutputSampleRate, options.GoogleOutputBitsPerSample);
                            ApplyRadioEffectIfEnabled(options, message, convertedWavPath);

                            if (options.SquelchEnabled)
                            {
                                // Defer the squelch-close tail until after the
                                // Roger Beep when PTT is enabled, so the
                                // realistic order is squelch-open, speech,
                                // Roger Beep, squelch-close.
                                SquelchEffectGenerator.ApplySquelch(
                                    convertedWavPath, wavPath, options.SquelchDurationMs, options.SquelchVolume,
                                    message.AddSquelchStart, message.AddSquelchEnd && !options.PttEnabled);
                            }
                            else
                            {
                                File.Copy(convertedWavPath, wavPath, overwrite: true);
                            }

                            ApplyPttIfEnabled(options, message, wavPath);
                            ApplySquelchEndIfNeeded(options, message, wavPath);
                            ApplyVolumeIfNeeded(options, wavPath);
                        }
                        finally
                        {
                            foreach (var tempFile in new[] { tempWavPath, convertedWavPath })
                            {
                                try
                                {
                                    File.Delete(tempFile);
                                }
                                catch (IOException)
                                {
                                    // Ignore cleanup failures for temp Google audio files.
                                }
                            }
                        }
                        break;
                }

                manifest.EntryHashes[wavFileName] = currentHash;

                progress?.Report(new PackGenerationProgress
                {
                    Current = i + 1,
                    Total = toGenerate.Count,
                    CurrentMsgId = message.MsgId
                });
            }

            manifest.Save(options.OutputFolder);

            var iniContent = SpmsgTemplateParser.Serialize(messages, options.RequiredMsgIds);

            var iniPath = Path.Combine(options.OutputFolder, "spmsg.txt");
            await File.WriteAllTextAsync(iniPath, iniContent, cancellationToken);
        }

        /// <summary>
        /// Determines, without generating anything, which enabled messages would
        /// actually produce a new/updated wav file on the next export (i.e. new
        /// messages or ones whose relevant data changed since the last export).
        /// Used to show the user a preview list before generating.
        /// </summary>
        public static IReadOnlyList<SpotterMessage> GetPendingMessages(
            IReadOnlyList<SpotterMessage> messages, PackGenerationOptions options)
        {
            var toCheck = messages
                .Where(m => m.Enabled && !string.IsNullOrWhiteSpace(m.Text))
                .ToList();

            if (!options.OnlyGenerateChanged)
            {
                return toCheck;
            }

            var manifest = PackManifest.Load(options.OutputFolder);
            var pending = new List<SpotterMessage>();

            foreach (var message in toCheck)
            {
                var wavFileName = string.IsNullOrWhiteSpace(message.WavFileName)
                    ? message.MsgId + ".wav"
                    : message.WavFileName;
                var wavPath = Path.Combine(options.OutputFolder, wavFileName);

                var currentHash = ComputeMessageHash(message, options);
                var unchanged = File.Exists(wavPath) &&
                    manifest.EntryHashes.TryGetValue(wavFileName, out var previousHash) &&
                    previousHash == currentHash;

                if (!unchanged)
                {
                    pending.Add(message);
                }
            }

            return pending;
        }

        /// <summary>
        /// Computes a stable hash over everything that influences the generated
        /// wav file for a message, so unchanged messages can be skipped on re-export.
        /// </summary>
        private static string ComputeMessageHash(SpotterMessage message, PackGenerationOptions options)
        {
            var sb = new StringBuilder();
            sb.Append(message.SourceType).Append('|');
            sb.Append(message.Text).Append('|');

            if (message.SourceType == AudioSourceType.Recording)
            {
                sb.Append(message.RecordedTakePath).Append('|');
                if (!string.IsNullOrWhiteSpace(message.RecordedTakePath) && File.Exists(message.RecordedTakePath))
                {
                    sb.Append(File.GetLastWriteTimeUtc(message.RecordedTakePath).Ticks).Append('|');
                }
            }
            else
            {
                var voiceName = string.IsNullOrWhiteSpace(message.GoogleVoiceName)
                    ? options.DefaultGoogleVoiceName
                    : message.GoogleVoiceName;
                sb.Append(voiceName).Append('|');
                sb.Append(options.GoogleOutputSampleRate).Append('|');
                sb.Append(options.GoogleOutputBitsPerSample).Append('|');
            }

            sb.Append(options.SquelchEnabled).Append('|');
            sb.Append(options.SquelchDurationMs).Append('|');
            sb.Append(options.SquelchVolume).Append('|');
            sb.Append(message.AddSquelchStart).Append('|');
            sb.Append(message.AddSquelchEnd).Append('|');
            sb.Append(message.AddRadioEffect).Append('|');
            sb.Append(options.RadioEffectEnabled).Append('|');
            sb.Append(options.RadioEffectLowCutHz).Append('|');
            sb.Append(options.RadioEffectHighCutHz).Append('|');
            sb.Append(options.RadioEffectDistortion).Append('|');
            sb.Append(message.AddPttEnd).Append('|');
            sb.Append(options.PttEnabled).Append('|');
            sb.Append(options.PttDurationMs).Append('|');
            sb.Append(options.PttVolume).Append('|');
            sb.Append(options.PttEndFrequencyHz).Append('|');
            sb.Append(options.PttEndFilePath).Append('|');
            sb.Append(options.OutputVolume).Append('|');

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var hashBytes = SHA256.HashData(bytes);
            return Convert.ToHexString(hashBytes);
        }

        public static async Task PreviewGoogleAsync(
            string text, string apiKey, string voiceName, string languageCode,
            int sampleRate = 5512, int bitsPerSample = 8,
            bool squelchEnabled = true, int squelchDurationMs = 150, double squelchVolume = 0.5,
            bool radioEffectEnabled = false, int radioEffectLowCutHz = 300, int radioEffectHighCutHz = 3000,
            double radioEffectDistortion = 0.2, double outputVolume = 1.0,
            bool pttEnabled = false, int pttDurationMs = 200, double pttVolume = 0.5,
            int pttEndFrequencyHz = 800,
            string? pttEndFilePath = null,
            CancellationToken cancellationToken = default)
        {
            var client = new GoogleTtsClient(apiKey);
            var audioBytes = await client.SynthesizeAsync(text, voiceName, languageCode, cancellationToken);

            var rawTempPath = Path.Combine(Path.GetTempPath(), $"spgen_preview_raw_{Guid.NewGuid():N}.wav");
            var convertedTempPath = Path.Combine(Path.GetTempPath(), $"spgen_preview_conv_{Guid.NewGuid():N}.wav");
            var tempPath = Path.Combine(Path.GetTempPath(), $"spgen_preview_{Guid.NewGuid():N}.wav");
            await File.WriteAllBytesAsync(rawTempPath, audioBytes, cancellationToken);

            try
            {
                AudioFormatConverter.ConvertFile(rawTempPath, convertedTempPath, sampleRate, bitsPerSample);

                if (radioEffectEnabled)
                {
                    var radioEffectTempPath = Path.Combine(Path.GetTempPath(), $"spgen_preview_radio_{Guid.NewGuid():N}.wav");
                    RadioEffectProcessor.Apply(
                        convertedTempPath, radioEffectTempPath, radioEffectLowCutHz, radioEffectHighCutHz, radioEffectDistortion);
                    File.Copy(radioEffectTempPath, convertedTempPath, overwrite: true);
                    File.Delete(radioEffectTempPath);
                }

                if (squelchEnabled)
                {
                    // Defer the squelch-close tail until after the Roger Beep
                    // when PTT is enabled, so the realistic order is
                    // squelch-open, speech, Roger Beep, squelch-close.
                    SquelchEffectGenerator.ApplySquelch(
                        convertedTempPath, tempPath, squelchDurationMs, squelchVolume,
                        addStart: true, addEnd: !pttEnabled);
                }
                else
                {
                    File.Copy(convertedTempPath, tempPath, overwrite: true);
                }

                if (pttEnabled)
                {
                    var pttTempPath = Path.Combine(Path.GetTempPath(), $"spgen_preview_ptt_{Guid.NewGuid():N}.wav");
                    PttEffectGenerator.ApplyPtt(
                        tempPath, pttTempPath, pttDurationMs, pttVolume,
                        pttEndFrequencyHz, endFilePath: pttEndFilePath);
                    File.Copy(pttTempPath, tempPath, overwrite: true);
                    File.Delete(pttTempPath);

                    if (squelchEnabled)
                    {
                        var squelchEndTempPath = Path.Combine(Path.GetTempPath(), $"spgen_preview_squelchend_{Guid.NewGuid():N}.wav");
                        SquelchEffectGenerator.ApplySquelch(
                            tempPath, squelchEndTempPath, squelchDurationMs, squelchVolume,
                            addStart: false, addEnd: true);
                        File.Copy(squelchEndTempPath, tempPath, overwrite: true);
                        File.Delete(squelchEndTempPath);
                    }
                }

                if (Math.Abs(outputVolume - 1.0) >= 0.0001)
                {
                    var volumeTempPath = Path.Combine(Path.GetTempPath(), $"spgen_preview_vol_{Guid.NewGuid():N}.wav");
                    VolumeProcessor.Apply(tempPath, volumeTempPath, outputVolume);
                    File.Copy(volumeTempPath, tempPath, overwrite: true);
                    File.Delete(volumeTempPath);
                }

                using var player = new System.Media.SoundPlayer(tempPath);
                player.PlaySync();
            }
            finally
            {
                foreach (var path in new[] { rawTempPath, convertedTempPath, tempPath })
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (IOException)
                    {
                        // Ignore cleanup failures for temp preview files.
                    }
                }
            }
        }
    }
}
