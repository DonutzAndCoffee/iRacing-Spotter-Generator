using System.Windows;
using iRacing_Spotter_Generator.Models;

namespace iRacing_Spotter_Generator
{
    public partial class AddMessageWindow : Window
    {
        private readonly string? _defaultGoogleVoiceName;

        public SpotterMessage? CreatedMessage { get; private set; }

        public AddMessageWindow(IEnumerable<string> knownMsgIds, string? defaultGoogleVoiceName = null)
        {
            InitializeComponent();
            _defaultGoogleVoiceName = defaultGoogleVoiceName;
            MsgIdComboBox.ItemsSource = knownMsgIds.OrderBy(id => id).ToList();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var msgId = (MsgIdComboBox.Text ?? string.Empty).Trim();
            var text = (TextTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(msgId))
            {
                MessageBox.Show(this, "Bitte eine Message-ID auswählen oder eingeben.",
                    "Neue Nachricht", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(this, "Bitte einen Text eingeben.",
                    "Neue Nachricht", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // The actual sequential WAV file name (e.g. MSGID.wav, MSGID_2.wav, ...) is assigned
            // by MainWindow.RenumberWavFileNames once the message has been added to the list.
            CreatedMessage = new SpotterMessage
            {
                MsgId = msgId,
                WavFileName = msgId + ".wav",
                Text = text,
                Enabled = true,
                SourceType = AudioSourceType.GoogleAi,
                GoogleVoiceName = _defaultGoogleVoiceName
            };

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
