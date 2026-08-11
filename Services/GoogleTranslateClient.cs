using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace iRacing_Spotter_Generator.Services
{
    /// <summary>
    /// Thin REST client for the Google Cloud Translation API v2, used to
    /// propose translations of message texts into a chosen target language
    /// (source language is auto-detected by the API).
    /// </summary>
    public class GoogleTranslateClient
    {
        private const string BaseUrl = "https://translation.googleapis.com/language/translate/v2";
        private static readonly HttpClient HttpClient = new();

        private readonly string _apiKey;

        public GoogleTranslateClient(string apiKey)
        {
            _apiKey = apiKey;
        }

        /// <summary>
        /// Translates a batch of texts into the given target language
        /// (e.g. "de", "en", "fr") in a single request, preserving order.
        /// Empty/whitespace-only entries are passed through unchanged.
        /// </summary>
        public async Task<List<string>> TranslateAsync(
            IReadOnlyList<string> texts, string targetLanguageCode, CancellationToken cancellationToken = default)
        {
            var results = new List<string>(new string[texts.Count]);

            var indexedNonEmpty = texts
                .Select((text, index) => (text, index))
                .Where(t => !string.IsNullOrWhiteSpace(t.text))
                .ToList();

            for (var i = 0; i < texts.Count; i++)
            {
                results[i] = texts[i];
            }

            if (indexedNonEmpty.Count == 0)
            {
                return results;
            }

            // Protect known racing terminology (e.g. "black flag") with
            // translate="no" spans so Google Translate leaves the marker
            // untouched; the correct localized term is substituted back in afterwards.
            var protectedTexts = new List<(string Html, List<string> FoundTerms)>();
            foreach (var (text, _) in indexedNonEmpty)
            {
                protectedTexts.Add(ProtectGlossaryTerms(text));
            }

            var url = $"{BaseUrl}?key={Uri.EscapeDataString(_apiKey)}";
            var requestBody = new TranslateRequest
            {
                Q = protectedTexts.Select(t => t.Html).ToList(),
                Target = targetLanguageCode,
                Format = "html"
            };

            using var response = await HttpClient.PostAsJsonAsync(url, requestBody, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Google Translate API request failed ({(int)response.StatusCode}): {errorBody}");
            }

            var payload = await response.Content.ReadFromJsonAsync<TranslateResponse>(cancellationToken: cancellationToken);
            var translations = payload?.Data?.Translations;

            if (translations is null || translations.Count != indexedNonEmpty.Count)
            {
                throw new InvalidOperationException("Google Translate API returned an unexpected response.");
            }

            for (var i = 0; i < indexedNonEmpty.Count; i++)
            {
                var restored = RestoreGlossaryTerms(
                    translations[i].TranslatedText, protectedTexts[i].FoundTerms, targetLanguageCode);

                // Racing communication customarily uses the informal "Du"
                // address in German, whereas Google Translate defaults to "Sie".
                if (string.Equals(targetLanguageCode, "de", StringComparison.OrdinalIgnoreCase))
                {
                    restored = GermanInformalizer.ApplyInformalAddress(restored);
                }

                results[indexedNonEmpty[i].index] = restored;
            }

            return results;
        }

        private static readonly Regex GlossaryMarkerRegex = new(@"GLOSSARYTERM_(\d+)", RegexOptions.Compiled);
        private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

        /// <summary>
        /// Wraps known glossary terms found in <paramref name="text"/> with
        /// a translate="no" span containing a unique marker, so the
        /// Translation API (called with format=html) passes them through
        /// unchanged. Longer phrases are matched before shorter overlapping
        /// ones (e.g. "furled black flag" before "black flag").
        /// </summary>
        private static (string Html, List<string> FoundTerms) ProtectGlossaryTerms(string text)
        {
            var foundTerms = new List<string>();
            var html = System.Net.WebUtility.HtmlEncode(text);

            foreach (var term in RacingTerminologyGlossary.OrderedTerms)
            {
                var pattern = $@"\b{Regex.Escape(term)}\b";
                html = Regex.Replace(html, pattern, match =>
                {
                    var index = foundTerms.Count;
                    foundTerms.Add(term);
                    return $"<span translate=\"no\">GLOSSARYTERM_{index}</span>";
                }, RegexOptions.IgnoreCase);
            }

            return (html, foundTerms);
        }

        /// <summary>
        /// Replaces glossary markers in the translated HTML with the
        /// correct localized term for the target language (falling back to
        /// the original English term if no curated translation exists),
        /// then strips any remaining HTML tags and decodes entities.
        /// </summary>
        private static string RestoreGlossaryTerms(string translatedHtml, List<string> foundTerms, string targetLanguageCode)
        {
            var withTermsRestored = GlossaryMarkerRegex.Replace(translatedHtml, match =>
            {
                var index = int.Parse(match.Groups[1].Value);
                var term = foundTerms[index];

                if (RacingTerminologyGlossary.Terms.TryGetValue(term, out var translationsForTerm) &&
                    translationsForTerm.TryGetValue(targetLanguageCode, out var localizedTerm))
                {
                    return localizedTerm;
                }

                return term;
            });

            var withoutTags = HtmlTagRegex.Replace(withTermsRestored, string.Empty);
            return System.Net.WebUtility.HtmlDecode(withoutTags).Trim();
        }


        private class TranslateRequest
        {
            [JsonPropertyName("q")]
            public List<string> Q { get; set; } = new();

            [JsonPropertyName("target")]
            public string Target { get; set; } = string.Empty;

            [JsonPropertyName("format")]
            public string Format { get; set; } = "text";
        }

        private class TranslateResponse
        {
            [JsonPropertyName("data")]
            public TranslateResponseData? Data { get; set; }
        }

        private class TranslateResponseData
        {
            [JsonPropertyName("translations")]
            public List<TranslationItem>? Translations { get; set; }
        }

        private class TranslationItem
        {
            [JsonPropertyName("translatedText")]
            public string TranslatedText { get; set; } = string.Empty;

            [JsonPropertyName("detectedSourceLanguage")]
            public string? DetectedSourceLanguage { get; set; }
        }
    }
}
