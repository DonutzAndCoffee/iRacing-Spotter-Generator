using System.Windows;
using iRacing_Spotter_Generator.Models;
using iRacing_Spotter_Generator.Services;

namespace iRacing_Spotter_Generator
{
    public partial class SettingsWindow : Window
    {
        private static readonly int[] SampleRates = { 5512, 8000, 11025, 16000, 22050, 44100 };
        private static readonly int[] BitsPerSampleOptions = { 8, 16 };

        public AppSettings Settings { get; private set; }

        public SettingsWindow(AppSettings currentSettings)
        {
            InitializeComponent();
            Settings = currentSettings;
            ApiKeyPasswordBox.Password = currentSettings.GoogleApiKey;

            SampleRateComboBox.ItemsSource = SampleRates;
            SampleRateComboBox.SelectedItem = currentSettings.RecordingSampleRate;

            BitsPerSampleComboBox.ItemsSource = BitsPerSampleOptions;
            BitsPerSampleComboBox.SelectedItem = currentSettings.RecordingBitsPerSample;

            DefaultVoiceComboBox.SelectedValue = currentSettings.DefaultGoogleVoiceName;

            if (!string.IsNullOrWhiteSpace(currentSettings.GoogleApiKey))
            {
                _ = LoadVoicesAsync(currentSettings);
            }
        }

        private async System.Threading.Tasks.Task LoadVoicesAsync(AppSettings currentSettings)
        {
            try
            {
                var client = new GoogleTtsClient(currentSettings.GoogleApiKey);
                var voices = await client.ListVoicesAsync();

                DefaultVoiceComboBox.ItemsSource = voices;
                DefaultVoiceComboBox.SelectedValue = currentSettings.DefaultGoogleVoiceName;
            }
            catch (Exception)
            {
                // Ignore; user can still type/select once voices are loaded via the main window.
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            Settings = new AppSettings
            {
                GoogleApiKey = ApiKeyPasswordBox.Password.Trim(),
                DefaultGoogleVoiceName = DefaultVoiceComboBox.SelectedValue as string ?? string.Empty,
                RecordingSampleRate = SampleRateComboBox.SelectedItem is int rate ? rate : 5512,
                RecordingBitsPerSample = BitsPerSampleComboBox.SelectedItem is int bits ? bits : 8
            };

            AppSettingsService.Save(Settings);
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
