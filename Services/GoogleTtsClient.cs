using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace iRacing_Spotter_Generator.Services
{
    public class GoogleVoiceInfo
    {
        public string Name { get; set; } = string.Empty;
        public string LanguageCode { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;

        /// <summary>
        /// Text shown in selection dropdowns. Normally equals <see cref="Name"/>,
        /// but the synthetic "use default voice" entry overrides this.
        /// </summary>
        public string DisplayText { get; set; } = string.Empty;

        public override string ToString() => $"{Name} ({LanguageCode}, {Gender})";
    }

    /// <summary>
    /// Thin REST client for the Google Cloud Text-to-Speech API, used to list
    /// available high quality AI voices and synthesize speech to WAV bytes.
    /// </summary>
    public class GoogleTtsClient
    {
        private const string BaseUrl = "https://texttospeech.googleapis.com/v1";
        private static readonly HttpClient HttpClient = new();

        private readonly string _apiKey;

        public GoogleTtsClient(string apiKey)
        {
            _apiKey = apiKey;
        }

        /// <summary>
        /// Derives the Google language code (e.g. "de-DE") from a voice name
        /// such as "de-DE-Neural2-G" or "en-US-Studio-O", since the synthesize
        /// request's languageCode must match the selected voice's language.
        /// </summary>
        public static string GetLanguageCodeFromVoiceName(string voiceName)
        {
            var parts = voiceName.Split('-');
            return parts.Length >= 2 ? $"{parts[0]}-{parts[1]}" : voiceName;
        }

        public async Task<List<GoogleVoiceInfo>> ListVoicesAsync(CancellationToken cancellationToken = default)
        {
            var url = $"{BaseUrl}/voices?key={Uri.EscapeDataString(_apiKey)}";
            using var response = await HttpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<VoicesListResponse>(cancellationToken: cancellationToken);
            var voices = new List<GoogleVoiceInfo>();

            if (payload?.Voices is null)
            {
                return voices;
            }

            foreach (var voice in payload.Voices)
            {
                var languageCode = voice.LanguageCodes?.FirstOrDefault() ?? string.Empty;

                // Prefer the highest quality voice families to keep the list manageable.
                if (voice.Name is null || !(voice.Name.Contains("Neural2") ||
                                             voice.Name.Contains("Studio") ||
                                             voice.Name.Contains("Wavenet") ||
                                             voice.Name.Contains("Polyglot")))
                {
                    continue;
                }

                voices.Add(new GoogleVoiceInfo
                {
                    Name = voice.Name,
                    LanguageCode = languageCode,
                    Gender = voice.SsmlGender ?? string.Empty,
                    DisplayText = voice.Name
                });
            }

            return voices
                .OrderBy(v => v.LanguageCode)
                .ThenBy(v => v.Name)
                .ToList();
        }

        public async Task<byte[]> SynthesizeAsync(string text, string voiceName, string languageCode, CancellationToken cancellationToken = default)
        {
            var url = $"{BaseUrl}/text:synthesize?key={Uri.EscapeDataString(_apiKey)}";

            var request = new SynthesizeRequest
            {
                Input = new SynthesizeInput { Text = text },
                Voice = new SynthesizeVoice { LanguageCode = languageCode, Name = voiceName },
                AudioConfig = new SynthesizeAudioConfig { AudioEncoding = "LINEAR16", SampleRateHertz = 24000 }
            };

            using var response = await HttpClient.PostAsJsonAsync(url, request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Google TTS request failed ({(int)response.StatusCode}): {errorBody}");
            }

            var payload = await response.Content.ReadFromJsonAsync<SynthesizeResponse>(cancellationToken: cancellationToken);

            if (payload?.AudioContent is null)
            {
                throw new InvalidOperationException("Google TTS response did not contain audio content.");
            }

            return Convert.FromBase64String(payload.AudioContent);
        }

        private class VoicesListResponse
        {
            [JsonPropertyName("voices")]
            public List<VoiceEntry>? Voices { get; set; }
        }

        private class VoiceEntry
        {
            [JsonPropertyName("languageCodes")]
            public List<string>? LanguageCodes { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("ssmlGender")]
            public string? SsmlGender { get; set; }
        }

        private class SynthesizeRequest
        {
            [JsonPropertyName("input")]
            public SynthesizeInput Input { get; set; } = new();

            [JsonPropertyName("voice")]
            public SynthesizeVoice Voice { get; set; } = new();

            [JsonPropertyName("audioConfig")]
            public SynthesizeAudioConfig AudioConfig { get; set; } = new();
        }

        private class SynthesizeInput
        {
            [JsonPropertyName("text")]
            public string Text { get; set; } = string.Empty;
        }

        private class SynthesizeVoice
        {
            [JsonPropertyName("languageCode")]
            public string LanguageCode { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;
        }

        private class SynthesizeAudioConfig
        {
            [JsonPropertyName("audioEncoding")]
            public string AudioEncoding { get; set; } = "LINEAR16";

            [JsonPropertyName("sampleRateHertz")]
            public int SampleRateHertz { get; set; } = 24000;
        }

        private class SynthesizeResponse
        {
            [JsonPropertyName("audioContent")]
            public string? AudioContent { get; set; }
        }
    }
}
