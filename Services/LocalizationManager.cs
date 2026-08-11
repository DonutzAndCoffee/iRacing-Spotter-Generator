using System.Linq;
using System.Windows;

namespace iRacing_Spotter_Generator.Services
{
    /// <summary>
    /// Handles runtime switching of the UI language by swapping the merged
    /// string-resource dictionary in the application's resources, and
    /// provides a lookup helper for strings used from code-behind
    /// (e.g. MessageBox texts, status messages).
    /// </summary>
    public static class LocalizationManager
    {
        private const string GermanDictionaryUri = "Resources/Strings.de.xaml";
        private const string EnglishDictionaryUri = "Resources/Strings.en.xaml";

        public static string CurrentLanguage { get; private set; } = "de";

        /// <summary>
        /// Applies the given language ("de" or "en") by replacing the
        /// merged string-resource dictionary on the application resources.
        /// Any UI bound via {DynamicResource} updates immediately.
        /// </summary>
        public static void SetLanguage(string languageCode)
        {
            var normalized = string.Equals(languageCode, "en", System.StringComparison.OrdinalIgnoreCase) ? "en" : "de";
            var uri = normalized == "en" ? EnglishDictionaryUri : GermanDictionaryUri;

            var dictionary = new ResourceDictionary { Source = new System.Uri(uri, System.UriKind.Relative) };

            var appResources = Application.Current.Resources;
            var existing = appResources.MergedDictionaries
                .FirstOrDefault(d => d.Source is not null &&
                    (d.Source.OriginalString == GermanDictionaryUri || d.Source.OriginalString == EnglishDictionaryUri));

            if (existing is not null)
            {
                appResources.MergedDictionaries.Remove(existing);
            }

            appResources.MergedDictionaries.Add(dictionary);
            CurrentLanguage = normalized;
        }

        /// <summary>
        /// Looks up a localized string by resource key for use in code-behind
        /// (e.g. MessageBox captions/messages). Falls back to the key itself
        /// if not found, so a missing translation is still visible/debuggable.
        /// </summary>
        public static string GetString(string key)
        {
            if (Application.Current.Resources[key] is string value)
            {
                return value;
            }

            return key;
        }
    }
}
