using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Navigation;
using iRacing_Spotter_Generator.Models;
using iRacing_Spotter_Generator.Services;

namespace iRacing_Spotter_Generator
{
    /// <summary>
    /// A single row's translation proposal, editable before being applied
    /// back to the underlying <see cref="SpotterMessage"/>.
    /// </summary>
    public class TranslationProposalItem : INotifyPropertyChanged
    {
        private string _proposedText = string.Empty;
        private bool _accepted = true;

        public required SpotterMessage Message { get; init; }

        public string MsgId => Message.MsgId;

        public string OriginalText { get; init; } = string.Empty;

        public string ProposedText
        {
            get => _proposedText;
            set
            {
                if (_proposedText == value)
                {
                    return;
                }

                _proposedText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProposedText)));
            }
        }

        /// <summary>
        /// Whether this row's proposal should be written back to the
        /// message's Text on Apply. Defaults to true once a translation
        /// was successfully proposed.
        /// </summary>
        public bool Accepted
        {
            get => _accepted;
            set
            {
                if (_accepted == value)
                {
                    return;
                }

                _accepted = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Accepted)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// Dialog that proposes translated text for one, several, or all
    /// spotter messages using the Google Cloud Translation API, letting the
    /// user review, edit, and selectively accept each proposal before it is
    /// written back to the message's Text.
    /// </summary>
    public partial class TranslationProposalWindow : Window
    {
        private readonly IReadOnlyList<SpotterMessage> _messages;
        private readonly string _apiKey;
        private readonly string _targetLanguageCode;
        private readonly string _targetLanguageName;

        public ObservableCollection<TranslationProposalItem> Items { get; } = new();

        public TranslationProposalWindow(
            IReadOnlyList<SpotterMessage> messages, string apiKey, string targetLanguageCode, string targetLanguageName)
        {
            InitializeComponent();

            _messages = messages;
            _apiKey = apiKey;
            _targetLanguageCode = targetLanguageCode;
            _targetLanguageName = targetLanguageName;

            ProposalsDataGrid.ItemsSource = Items;
            HeaderTextBlock.Text = $"Übersetzungsvorschläge nach {_targetLanguageName} ({_messages.Count} Nachricht(en))";
            StatusTextBlock.Text = "Übersetze...";

            Loaded += TranslationProposalWindow_Loaded;
        }

        private async void TranslationProposalWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var client = new GoogleTranslateClient(_apiKey);
                var originalTexts = _messages.Select(m => m.Text).ToList();
                var translated = await client.TranslateAsync(originalTexts, _targetLanguageCode);

                Items.Clear();
                for (var i = 0; i < _messages.Count; i++)
                {
                    Items.Add(new TranslationProposalItem
                    {
                        Message = _messages[i],
                        OriginalText = originalTexts[i],
                        ProposedText = translated[i]
                    });
                }

                StatusTextBlock.Text = $"{Items.Count} Vorschläge erhalten. Bitte prüfen und anpassen.";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"{LocalizationManager.GetString("Str_TranslationFailed")} {ex.Message}";
                ShowErrorLink(ex.Message);
            }
        }

        private static readonly Regex UrlRegex = new(@"https?://\S+", RegexOptions.Compiled);

        /// <summary>
        /// Shows a clickable link to the relevant Google Cloud Console page.
        /// If the API error message contains its own "enable this API" URL
        /// (as Google's "API not enabled" errors do), that exact link is
        /// used; otherwise a generic link to the Translation API library
        /// page is shown.
        /// </summary>
        private void ShowErrorLink(string errorMessage)
        {
            var match = UrlRegex.Match(errorMessage);
            var rawUrl = match.Success
                ? match.Value.TrimEnd('.', ',', ')', '"')
                : "https://console.cloud.google.com/apis/library/translate.googleapis.com";

            ErrorLinkHyperlink.NavigateUri = new Uri(rawUrl);
            ErrorLinkTextBlock.Visibility = Visibility.Visible;
        }

        private void ErrorLinkHyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in Items.Where(i => i.Accepted))
            {
                item.Message.Text = item.ProposedText;
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
