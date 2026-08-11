using System.Text.RegularExpressions;

namespace iRacing_Spotter_Generator.Services
{
    /// <summary>
    /// Converts German machine-translation output from the formal "Sie"
    /// address form to the informal "Du" form customary in motorsport/
    /// racing communication. The Google Translate API has no built-in
    /// formality switch for the (v2) Translation API, so this applies a
    /// targeted set of pronoun and verb-conjugation replacements instead.
    /// </summary>
    public static class GermanInformalizer
    {
        /// <summary>
        /// Known irregular/modal verb pairs: 3rd-person-plural/formal form
        /// (identical to the infinitive for regular verbs) -> "du" form.
        /// </summary>
        private static readonly Dictionary<string, string> VerbMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["können"] = "kannst",
            ["dürfen"] = "darfst",
            ["müssen"] = "musst",
            ["sollen"] = "sollst",
            ["möchten"] = "möchtest",
            ["wollen"] = "willst",
            ["sind"] = "bist",
            ["haben"] = "hast",
            ["werden"] = "wirst",
            ["mögen"] = "magst",
            ["wissen"] = "weißt",
            ["lassen"] = "lässt",
            ["geben"] = "gibst",
            ["nehmen"] = "nimmst",
            ["sehen"] = "siehst",
            ["fahren"] = "fährst",
            ["tun"] = "tust"
        };

        private static readonly Regex SieVerbRegex = new(@"\bSie\s+(\p{L}+)\b", RegexOptions.Compiled);
        private static readonly Regex VerbSieRegex = new(@"\b(\p{L}+)\s+Sie\b", RegexOptions.Compiled);
        private static readonly Regex RemainingSieRegex = new(@"\bSie\b", RegexOptions.Compiled);
        private static readonly Regex SentenceStartRegex = new(@"(^|[.!?]\s+)(du|dein\p{L}*)", RegexOptions.Compiled);

        /// <summary>
        /// Rewrites a German text from formal to informal address.
        /// Best-effort: covers common pronouns and verb forms used in
        /// short advisory/racing callouts; unusual constructs are left
        /// untouched rather than risking an incorrect rewrite.
        /// </summary>
        public static string ApplyInformalAddress(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            // "Sie <verb>" -> "du <verb-du-form>" (statement order)
            text = SieVerbRegex.Replace(text, match => $"du {ConjugateDu(match.Groups[1].Value)}");

            // "<verb> Sie" -> "<verb-du-form> du" (question order), only for known verbs to avoid false positives
            text = VerbSieRegex.Replace(text, match =>
            {
                var verb = match.Groups[1].Value;
                return VerbMap.ContainsKey(verb) ? $"{ConjugateDu(verb)} du" : match.Value;
            });

            text = Regex.Replace(text, @"\bIhre\b", "deine");
            text = Regex.Replace(text, @"\bIhrer\b", "deiner");
            text = Regex.Replace(text, @"\bIhrem\b", "deinem");
            text = Regex.Replace(text, @"\bIhren\b", "deinen");
            text = Regex.Replace(text, @"\bIhres\b", "deines");
            text = Regex.Replace(text, @"\bIhnen\b", "dir");

            // Any standalone "Sie" left over (not matched by the verb patterns above)
            text = RemainingSieRegex.Replace(text, "du");

            // Capitalize "du"/"dein..." when it starts a sentence
            text = SentenceStartRegex.Replace(text, match =>
                match.Groups[1].Value + char.ToUpperInvariant(match.Groups[2].Value[0]) + match.Groups[2].Value[1..]);

            return text;
        }

        private static string ConjugateDu(string verb)
        {
            if (VerbMap.TryGetValue(verb, out var mapped))
            {
                return mapped;
            }

            if (verb.EndsWith("eln", StringComparison.OrdinalIgnoreCase) ||
                verb.EndsWith("ern", StringComparison.OrdinalIgnoreCase))
            {
                return verb[..^1] + "st";
            }

            if (verb.EndsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                var stem = verb[..^2];
                return stem.EndsWith("d", StringComparison.OrdinalIgnoreCase) ||
                       stem.EndsWith("t", StringComparison.OrdinalIgnoreCase)
                    ? stem + "est"
                    : stem + "st";
            }

            return verb;
        }
    }
}
