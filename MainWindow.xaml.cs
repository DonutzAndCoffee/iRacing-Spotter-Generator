using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using iRacing_Spotter_Generator.Models;
using iRacing_Spotter_Generator.Services;
using Microsoft.Win32;

namespace iRacing_Spotter_Generator
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly List<SpotterMessage> _allMessages = new();
        private readonly ObservableCollection<SpotterMessage> _viewMessages = new();
        private readonly HashSet<string> _knownMsgIds = new(StringComparer.OrdinalIgnoreCase);

        public ObservableCollection<GoogleVoiceInfo> GoogleVoices { get; } = new();

        private AppSettings _settings = new();

        private static readonly int[] SampleRates = { 5512, 8000, 11025, 16000, 22050, 44100 };
        private static readonly int[] BitsPerSampleOptions = { 8, 16 };

        /// <summary>
        /// Tracks whether there are unsaved changes since the last save/load/new-project,
        /// so the user can be prompted before discarding them.
        /// </summary>
        private bool _isDirty;

        public MainWindow()
        {
            InitializeComponent();
            _settings = AppSettingsService.Load();
            LoadTemplate();

            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            VersionTextBlock.Text = version is not null ? $"v{version.Major}.{version.Minor}.{version.Build}" : string.Empty;

            MessagesDataGrid.DataContext = _viewMessages;

            Closing += MainWindow_Closing;

            ApiKeyPasswordBox.Password = _settings.GoogleApiKey;
            SampleRateComboBox.ItemsSource = SampleRates;
            SampleRateComboBox.SelectedItem = _settings.RecordingSampleRate;
            BitsPerSampleComboBox.ItemsSource = BitsPerSampleOptions;
            BitsPerSampleComboBox.SelectedItem = _settings.RecordingBitsPerSample;
            SquelchEnabledCheckBox.IsChecked = _settings.SquelchEnabled;
            SquelchDurationTextBox.Text = _settings.SquelchDurationMs.ToString();
            SquelchVolumeTextBox.Text = _settings.SquelchVolume.ToString(System.Globalization.CultureInfo.InvariantCulture);
            RadioEffectEnabledCheckBox.IsChecked = _settings.RadioEffectEnabled;
            RadioEffectLowCutTextBox.Text = _settings.RadioEffectLowCutHz.ToString();
            RadioEffectHighCutTextBox.Text = _settings.RadioEffectHighCutHz.ToString();
            RadioEffectDistortionTextBox.Text = _settings.RadioEffectDistortion.ToString(System.Globalization.CultureInfo.InvariantCulture);
            OutputVolumeTextBox.Text = _settings.OutputVolume.ToString(System.Globalization.CultureInfo.InvariantCulture);

            foreach (ComboBoxItem item in LanguageComboBox.Items)
            {
                if (string.Equals(item.Tag as string, _settings.Language, StringComparison.OrdinalIgnoreCase))
                {
                    LanguageComboBox.SelectedItem = item;
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(_settings.GoogleApiKey))
            {
                _ = LoadGoogleVoicesAsync();
            }
            else
            {
                GoogleStatusTextBlock.Text = LocalizationManager.GetString("Str_NoGoogleApiKey");
            }
        }

        private void LanguageComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (LanguageComboBox.SelectedItem is not ComboBoxItem { Tag: string languageCode })
            {
                return;
            }

            LocalizationManager.SetLanguage(languageCode);
            _settings.Language = languageCode;
            AppSettingsService.Save(_settings);
        }

        /// <summary>
        /// Generic handler for in-app hyperlinks (Discord, Google Cloud
        /// Console deep links, etc.) that opens the URI in the user's
        /// default browser.
        /// </summary>
        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void LoadTemplate()
        {
            var templatePath = Path.Combine(AppContext.BaseDirectory, "INFO", "spmsg-samples-2026-06-03.txt");

            if (!File.Exists(templatePath))
            {
                MessageBox.Show(this, $"{LocalizationManager.GetString("Str_TemplateNotFound")}\n{templatePath}",
                    LocalizationManager.GetString("Str_AppTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _allMessages.Clear();
            _allMessages.AddRange(SpmsgTemplateParser.Parse(templatePath));

            _knownMsgIds.Clear();
            foreach (var msgId in _allMessages.Select(m => m.MsgId))
            {
                _knownMsgIds.Add(msgId);
            }

            foreach (var message in _allMessages)
            {
                message.PropertyChanged += Message_PropertyChanged;
            }

            RefreshView(string.Empty);
            _isDirty = false;
        }

        private void Message_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            MarkDirty();
        }

        private void MarkDirty()
        {
            _isDirty = true;
        }

        /// <summary>
        /// If there are unsaved changes, asks the user whether to save them before
        /// proceeding with a destructive action (new project, open project, closing).
        /// Returns true if the caller may proceed, false if the action should be cancelled.
        /// </summary>
        private bool ConfirmDiscardUnsavedChanges()
        {
            if (!_isDirty)
            {
                return true;
            }

            var result = MessageBox.Show(this,
                LocalizationManager.GetString("Str_UnsavedChangesMessage"),
                LocalizationManager.GetString("Str_UnsavedChangesTitle"), MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            switch (result)
            {
                case MessageBoxResult.Yes:
                    return SaveProject();
                case MessageBoxResult.No:
                    return true;
                default:
                    return false;
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!ConfirmDiscardUnsavedChanges())
            {
                e.Cancel = true;
            }
        }

        private void NewProjectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscardUnsavedChanges())
            {
                return;
            }

            foreach (var message in _allMessages)
            {
                message.PropertyChanged -= Message_PropertyChanged;
            }

            DestinationTextBox.Text = string.Empty;
            PackNameTextBox.Text = string.Empty;

            LoadTemplate();
            StatusTextBlock.Text = LocalizationManager.GetString("Str_NewProjectStarted");
        }

        private void RefreshView(string filter)
        {
            _viewMessages.Clear();

            var query = string.IsNullOrWhiteSpace(filter)
                ? _allMessages
                : _allMessages.Where(m =>
                    m.MsgId.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    m.Text.Contains(filter, StringComparison.OrdinalIgnoreCase));

            foreach (var message in query)
            {
                _viewMessages.Add(message);
            }
        }

        private void FilterTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            RefreshView(FilterTextBox.Text);
        }

        private void AddMessageButton_Click(object sender, RoutedEventArgs e)
        {
            var knownMsgIds = _allMessages.Select(m => m.MsgId).Distinct(StringComparer.OrdinalIgnoreCase);
            var addWindow = new AddMessageWindow(knownMsgIds, _settings.DefaultGoogleVoiceName) { Owner = this };

            if (addWindow.ShowDialog() == true && addWindow.CreatedMessage is not null)
            {
                _allMessages.Add(addWindow.CreatedMessage);
                addWindow.CreatedMessage.PropertyChanged += Message_PropertyChanged;
                _knownMsgIds.Add(addWindow.CreatedMessage.MsgId);
                RenumberWavFileNames(addWindow.CreatedMessage.MsgId);
                RefreshView(FilterTextBox.Text);
                MarkDirty();
            }
        }

        /// <summary>
        /// Renumbers the WAV file names of all messages sharing the given MsgId so that they
        /// follow the sequential scheme used by the iRacing template (MSGID.WAV, MSGID_2.WAV,
        /// MSGID_3.WAV, ...). This keeps names free of gaps/duplicates after messages have been
        /// added, duplicated or removed, without touching the naming of unrelated MsgIds.
        /// </summary>
        private void RenumberWavFileNames(string msgId)
        {
            var rows = _allMessages
                .Where(m => string.Equals(m.MsgId, msgId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (rows.Count == 0)
            {
                return;
            }

            var baseName = Regex.Replace(msgId, @"^SPCC_", string.Empty, RegexOptions.IgnoreCase);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = msgId;
            }

            for (var i = 0; i < rows.Count; i++)
            {
                var newName = i == 0 ? $"{baseName}.wav" : $"{baseName}_{i + 1}.wav";
                rows[i].WavFileName = newName;
            }
        }

        private void DuplicateMessage(SpotterMessage message)
        {
            var index = _allMessages.IndexOf(message);

            var duplicate = new SpotterMessage
            {
                MsgId = message.MsgId,
                WavFileName = message.WavFileName,
                Text = message.Text,
                Enabled = message.Enabled,
                SourceType = message.SourceType,
                GoogleVoiceName = message.GoogleVoiceName,
                RecordedTakePath = message.RecordedTakePath,
                AllTakes = message.AllTakes.Select(t => new TakeInfo { FilePath = t.FilePath, Name = t.Name, Rating = t.Rating }).ToList()
            };

            if (index >= 0 && index + 1 <= _allMessages.Count)
            {
                _allMessages.Insert(index + 1, duplicate);
            }
            else
            {
                _allMessages.Add(duplicate);
            }

            duplicate.PropertyChanged += Message_PropertyChanged;
            RenumberWavFileNames(duplicate.MsgId);
            RefreshView(FilterTextBox.Text);
            MarkDirty();
        }

        private void RemoveMessage(SpotterMessage message)
        {
            var occurrences = _allMessages.Count(m => string.Equals(m.MsgId, message.MsgId, StringComparison.OrdinalIgnoreCase));

            if (occurrences <= 1)
            {
                MessageBox.Show(this,
                    $"'{message.MsgId}' {LocalizationManager.GetString("Str_LastMsgIdCannotBeDeleted")}\n" +
                    LocalizationManager.GetString("Str_LastMsgIdCannotBeDeletedHint"),
                    LocalizationManager.GetString("Str_CannotRemoveTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(this, $"'{message.MsgId}' ({message.Text}) {LocalizationManager.GetString("Str_RemoveConfirm")}",
                LocalizationManager.GetString("Str_RemoveTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            message.PropertyChanged -= Message_PropertyChanged;
            _allMessages.Remove(message);
            _viewMessages.Remove(message);
            RenumberWavFileNames(message.MsgId);
            MarkDirty();
        }

        private void RemoveMessageButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: SpotterMessage message })
            {
                return;
            }

            RemoveMessage(message);
        }

        private void DuplicateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem { DataContext: SpotterMessage message })
            {
                return;
            }

            DuplicateMessage(message);
        }

        /// <summary>
        /// Lets the checkbox columns (Enabled, √Start, √End, ...) toggle on
        /// the very first click even when the row/cell wasn't selected yet,
        /// instead of requiring one click to select and a second to toggle.
        /// </summary>
        private void MessagesDataGrid_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var dependencyObject = e.OriginalSource as DependencyObject;
            while (dependencyObject is not null and not DataGridCell)
            {
                dependencyObject = System.Windows.Media.VisualTreeHelper.GetParent(dependencyObject);
            }

            if (dependencyObject is DataGridCell { Content: CheckBox } cell && !cell.IsEditing)
            {
                if (!cell.IsFocused)
                {
                    cell.Focus();
                }

                MessagesDataGrid.BeginEdit(e);
            }
        }

        private void RemoveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem { DataContext: SpotterMessage message })
            {
                return;
            }

            RemoveMessage(message);
        }

        /// <summary>
        /// Determines the target messages for a translation proposal
        /// triggered from a specific row's context menu: if that row is
        /// part of a multi-row selection, all selected (non-comment) rows
        /// are targeted; otherwise just that single row.
        /// </summary>
        private List<SpotterMessage> GetTranslationScopeForRow(SpotterMessage row)
        {
            var selected = MessagesDataGrid.SelectedItems.Cast<SpotterMessage>().ToList();

            if (selected.Count > 1 && selected.Contains(row))
            {
                return selected.Where(m => !m.IsComment && !string.IsNullOrWhiteSpace(m.Text)).ToList();
            }

            return row.IsComment || string.IsNullOrWhiteSpace(row.Text)
                ? new List<SpotterMessage>()
                : new List<SpotterMessage> { row };
        }

        private void ShowTranslationProposal(List<SpotterMessage> messages, string languageCode, string languageName)
        {
            if (messages.Count == 0)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_settings.GoogleApiKey))
            {
                MessageBox.Show(this, LocalizationManager.GetString("Str_NoGoogleApiKeyForTranslation"),
                    LocalizationManager.GetString("Str_AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var window = new TranslationProposalWindow(messages, _settings.GoogleApiKey, languageCode, languageName)
            {
                Owner = this
            };

            if (window.ShowDialog() == true)
            {
                MarkDirty();
            }
        }

        private static (string Code, string Name) ParseLanguageTag(object? tag)
        {
            var parts = (tag as string ?? "en:English").Split(':', 2);
            return (parts[0], parts.Length > 1 ? parts[1] : parts[0]);
        }

        private void RowTranslationLanguageMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem menuItem)
            {
                return;
            }

            // The submenu's DataContext flows down from the row's context menu.
            if (menuItem.DataContext is not SpotterMessage row)
            {
                return;
            }

            var (code, name) = ParseLanguageTag(menuItem.Tag);
            ShowTranslationProposal(GetTranslationScopeForRow(row), code, name);
        }

        private void TranslateAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.ContextMenu is not null)
            {
                element.ContextMenu.PlacementTarget = element;
                element.ContextMenu.IsOpen = true;
            }
        }

        private void TranslateAllLanguageMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem menuItem)
            {
                return;
            }

            var (code, name) = ParseLanguageTag(menuItem.Tag);
            var messages = _allMessages.Where(m => !m.IsComment && !string.IsNullOrWhiteSpace(m.Text)).ToList();
            ShowTranslationProposal(messages, code, name);
        }

        private async Task LoadGoogleVoicesAsync()
        {
            if (string.IsNullOrWhiteSpace(_settings.GoogleApiKey))
            {
                GoogleStatusTextBlock.Text = LocalizationManager.GetString("Str_NoGoogleApiKeyShort");
                return;
            }

            GoogleStatusTextBlock.Text = LocalizationManager.GetString("Str_LoadingGoogleVoices");

            try
            {
                var client = new GoogleTtsClient(_settings.GoogleApiKey);
                var voices = await client.ListVoicesAsync();

                GoogleVoices.Clear();
                GoogleVoices.Add(new GoogleVoiceInfo
                {
                    Name = string.Empty,
                    DisplayText = string.IsNullOrWhiteSpace(_settings.DefaultGoogleVoiceName)
                        ? "Standard (keine Stimme gewählt)"
                        : $"Standard ({_settings.DefaultGoogleVoiceName})"
                });

                foreach (var voice in voices)
                {
                    GoogleVoices.Add(voice);
                }

                DefaultVoiceComboBox.ItemsSource = voices;
                DefaultVoiceComboBox.SelectedValue = _settings.DefaultGoogleVoiceName;

                GoogleStatusTextBlock.Text = $"{voices.Count} {LocalizationManager.GetString("Str_GoogleVoicesLoaded")}";
            }
            catch (Exception ex)
            {
                GoogleStatusTextBlock.Text = $"{LocalizationManager.GetString("Str_GoogleVoicesLoadFailed")} {ex.Message}";
            }
        }

        private async void LoadGoogleVoicesButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadGoogleVoicesAsync();
        }

        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            _settings = new AppSettings
            {
                GoogleApiKey = ApiKeyPasswordBox.Password.Trim(),
                DefaultGoogleVoiceName = DefaultVoiceComboBox.SelectedValue as string ?? string.Empty,
                RecordingSampleRate = SampleRateComboBox.SelectedItem is int rate ? rate : 5512,
                RecordingBitsPerSample = BitsPerSampleComboBox.SelectedItem is int bits ? bits : 8,
                SquelchEnabled = SquelchEnabledCheckBox.IsChecked == true,
                SquelchDurationMs = int.TryParse(SquelchDurationTextBox.Text, out var durationMs) ? durationMs : 150,
                SquelchVolume = double.TryParse(
                    SquelchVolumeTextBox.Text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var volume) ? volume : 0.5,
                RadioEffectEnabled = RadioEffectEnabledCheckBox.IsChecked == true,
                RadioEffectLowCutHz = int.TryParse(RadioEffectLowCutTextBox.Text, out var lowCutHz) ? lowCutHz : 300,
                RadioEffectHighCutHz = int.TryParse(RadioEffectHighCutTextBox.Text, out var highCutHz) ? highCutHz : 3000,
                RadioEffectDistortion = double.TryParse(
                    RadioEffectDistortionTextBox.Text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var distortion) ? distortion : 0.2,
                OutputVolume = double.TryParse(
                    OutputVolumeTextBox.Text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var outputVolume) ? outputVolume : 1.0,
                Language = LocalizationManager.CurrentLanguage
            };

            AppSettingsService.Save(_settings);
            _ = LoadGoogleVoicesAsync();
        }

        /// <summary>
        /// Resolves the effective Google voice name for a message: its own explicit
        /// selection, or the configured default voice when the row is set to "Standard".
        /// </summary>
        private string? ResolveGoogleVoiceName(SpotterMessage message) =>
            string.IsNullOrWhiteSpace(message.GoogleVoiceName)
                ? _settings.DefaultGoogleVoiceName
                : message.GoogleVoiceName;

        private async void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: SpotterMessage message })
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(message.Text))
            {
                return;
            }

            if (message.SourceType == AudioSourceType.Recording)
            {
                if (!string.IsNullOrWhiteSpace(message.RecordedTakePath) && File.Exists(message.RecordedTakePath))
                {
                    var takePath = message.RecordedTakePath;
                    var sampleRate = _settings.RecordingSampleRate;
                    var bitsPerSample = _settings.RecordingBitsPerSample;
                    var squelchEnabled = _settings.SquelchEnabled;
                    var squelchDurationMs = _settings.SquelchDurationMs;
                    var squelchVolume = _settings.SquelchVolume;
                    var addSquelchStart = message.AddSquelchStart;
                    var addSquelchEnd = message.AddSquelchEnd;
                    var radioEffectEnabled = _settings.RadioEffectEnabled && message.AddRadioEffect;
                    var radioEffectLowCutHz = _settings.RadioEffectLowCutHz;
                    var radioEffectHighCutHz = _settings.RadioEffectHighCutHz;
                    var radioEffectDistortion = _settings.RadioEffectDistortion;
                    var outputVolume = _settings.OutputVolume;

                    Task.Run(() =>
                    {
                        var convertedPath = Path.Combine(Path.GetTempPath(), $"iracing_preview_conv_{Guid.NewGuid():N}.wav");
                        var squelchedPath = Path.Combine(Path.GetTempPath(), $"iracing_preview_{Guid.NewGuid():N}.wav");
                        var volumePath = Path.Combine(Path.GetTempPath(), $"iracing_preview_vol_{Guid.NewGuid():N}.wav");

                        try
                        {
                            // Downsample the raw (high quality) take to the
                            // configured target quality first, exactly like
                            // the pack export does, so the preview matches
                            // what will end up in the final pack.
                            AudioFormatConverter.ConvertFile(takePath, convertedPath, sampleRate, bitsPerSample);

                            if (radioEffectEnabled)
                            {
                                var radioEffectTempPath = Path.Combine(Path.GetTempPath(), $"iracing_preview_radio_{Guid.NewGuid():N}.wav");
                                RadioEffectProcessor.Apply(
                                    convertedPath, radioEffectTempPath, radioEffectLowCutHz, radioEffectHighCutHz, radioEffectDistortion);
                                File.Copy(radioEffectTempPath, convertedPath, overwrite: true);
                                File.Delete(radioEffectTempPath);
                            }

                            string playbackPath;
                            if (squelchEnabled)
                            {
                                SquelchEffectGenerator.ApplySquelch(
                                    convertedPath, squelchedPath, squelchDurationMs, squelchVolume,
                                    addSquelchStart, addSquelchEnd);
                                playbackPath = squelchedPath;
                            }
                            else
                            {
                                playbackPath = convertedPath;
                            }

                            if (Math.Abs(outputVolume - 1.0) >= 0.0001)
                            {
                                VolumeProcessor.Apply(playbackPath, volumePath, outputVolume);
                                playbackPath = volumePath;
                            }

                            using var player = new System.Media.SoundPlayer(playbackPath);
                            player.PlaySync();
                        }
                        finally
                        {
                            foreach (var tempFile in new[] { convertedPath, squelchedPath, volumePath })
                            {
                                try
                                {
                                    File.Delete(tempFile);
                                }
                                catch (IOException)
                                {
                                    // Ignore cleanup failures for temp preview files.
                                }
                            }
                        }
                    });
                }
                else
                {
                    MessageBox.Show(this, LocalizationManager.GetString("Str_NoTakeRecorded"),
                        LocalizationManager.GetString("Str_PreviewTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            if (message.SourceType == AudioSourceType.GoogleAi)
            {
                if (string.IsNullOrWhiteSpace(_settings.GoogleApiKey))
                {
                    MessageBox.Show(this, LocalizationManager.GetString("Str_NoGoogleApiKeyForPreview"),
                        LocalizationManager.GetString("Str_PreviewTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var voiceName = ResolveGoogleVoiceName(message);

                if (string.IsNullOrWhiteSpace(voiceName))
                {
                    MessageBox.Show(this, LocalizationManager.GetString("Str_NoGoogleVoiceForPreview"),
                        LocalizationManager.GetString("Str_PreviewTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    var languageCode = GoogleTtsClient.GetLanguageCodeFromVoiceName(voiceName);
                    await PackGenerator.PreviewGoogleAsync(
                        message.Text, _settings.GoogleApiKey, voiceName, languageCode,
                        _settings.RecordingSampleRate, _settings.RecordingBitsPerSample,
                        _settings.SquelchEnabled, _settings.SquelchDurationMs, _settings.SquelchVolume,
                        _settings.RadioEffectEnabled && message.AddRadioEffect, _settings.RadioEffectLowCutHz, _settings.RadioEffectHighCutHz,
                        _settings.RadioEffectDistortion, _settings.OutputVolume);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"{LocalizationManager.GetString("Str_GooglePreviewFailed")} {ex.Message}",
                        LocalizationManager.GetString("Str_PreviewTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: SpotterMessage message })
            {
                return;
            }

            var takesWindow = new TakesWindow(
                message.Id, message.MsgId, message.Text, message.AllTakes, message.RecordedTakePath,
                _settings.RecordingSampleRate, _settings.RecordingBitsPerSample) { Owner = this };
            if (takesWindow.ShowDialog() == true)
            {
                message.RecordedTakePath = takesWindow.SelectedTakePath;
                message.SourceType = AudioSourceType.Recording;
            }

            // Always capture the full take list, even if the user only
            // recorded/trimmed takes and cancelled without picking one,
            // so nothing recorded during this session gets lost.
            message.AllTakes = takesWindow.AllTakes.ToList();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select destination folder for the spotter pack"
            };

            if (dialog.ShowDialog(this) == true)
            {
                DestinationTextBox.Text = dialog.FolderName;
            }
        }

        /// <summary>
        /// Imports a finished/exported spotter pack (a folder containing a
        /// spmsg.ini plus its generated wav files) and takes it over as the
        /// current project, so it can be reviewed/edited further instead of
        /// being treated as a black box. Every message covered by the pack's
        /// spmsg.ini is set to use the existing wav file as a "Recording"
        /// source (no regeneration), while every other known MsgId keeps
        /// coming from the built-in template as usual.
        /// </summary>
        private void LicenseButton_Click(object sender, RoutedEventArgs e)
        {
            var licenseWindow = new LicenseWindow { Owner = this };
            licenseWindow.ShowDialog();
        }

        private void ImportPackButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscardUnsavedChanges())
            {
                return;
            }

            var folderDialog = new OpenFolderDialog
            {
                Title = LocalizationManager.GetString("Str_ImportPackTitle")
            };

            if (folderDialog.ShowDialog(this) != true)
            {
                return;
            }

            var folder = folderDialog.FolderName;
            var spmsgPath = Directory.EnumerateFiles(folder)
                .FirstOrDefault(f =>
                {
                    var name = Path.GetFileName(f);
                    return string.Equals(name, "spmsg.ini", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(name, "spmsg.txt", StringComparison.OrdinalIgnoreCase);
                });

            if (spmsgPath is null)
            {
                MessageBox.Show(this, LocalizationManager.GetString("Str_ImportPackNoSpmsg"),
                    LocalizationManager.GetString("Str_ImportPackTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var importedMessages = SpmsgTemplateParser.Parse(spmsgPath);
                var importedByMsgId = importedMessages
                    .Where(m => !m.IsComment)
                    .GroupBy(m => m.MsgId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                foreach (var message in _allMessages)
                {
                    message.PropertyChanged -= Message_PropertyChanged;
                }

                DestinationTextBox.Text = string.Empty;
                PackNameTextBox.Text = string.Empty;
                LoadTemplate();

                var importedCount = 0;
                foreach (var message in _allMessages)
                {
                    if (message.IsComment || !importedByMsgId.TryGetValue(message.MsgId, out var imported))
                    {
                        continue;
                    }

                    message.Enabled = imported.Enabled;

                    if (!imported.Enabled || string.IsNullOrWhiteSpace(imported.WavFileName))
                    {
                        continue;
                    }

                    var wavPath = Path.Combine(folder, imported.WavFileName);
                    if (!File.Exists(wavPath))
                    {
                        continue;
                    }

                    message.Text = imported.Text;
                    message.SourceType = AudioSourceType.Recording;
                    message.RecordedTakePath = wavPath;
                    message.AllTakes = new List<TakeInfo>
                    {
                        new TakeInfo { FilePath = wavPath, Name = "Imported" }
                    };
                    importedCount++;
                }

                DestinationTextBox.Text = folder;
                PackNameTextBox.Text = new DirectoryInfo(folder).Name;

                RefreshView(FilterTextBox.Text);
                RefreshExportedFlags();
                StatusTextBlock.Text = $"{LocalizationManager.GetString("Str_PackImported")} {importedCount}";
                _isDirty = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{LocalizationManager.GetString("Str_ImportPackFailed")} {ex.Message}",
                    LocalizationManager.GetString("Str_ImportPackTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveProjectButton_Click(object sender, RoutedEventArgs e)
        {
            SaveProject();
        }

        /// <summary>
        /// Saves the current project to a user-chosen file. Returns true if the
        /// project was saved successfully, false if the user cancelled the dialog
        /// or the save failed.
        /// </summary>
        private bool SaveProject()
        {
            var dialog = new SaveFileDialog
            {
                Filter = SpotterProjectService.FileFilter,
                DefaultExt = SpotterProjectService.DefaultExtension,
                FileName = string.IsNullOrWhiteSpace(PackNameTextBox.Text) ? "MySpotterPack" : PackNameTextBox.Text
            };

            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            try
            {
                // Recorded/trimmed takes currently live in a per-row temp
                // folder (created by TakesWindow). Copy all of them next to
                // the project file so every take survives temp-folder
                // cleanup and project reloads, not just the selected one.
                var takesFolder = Path.Combine(Path.GetDirectoryName(dialog.FileName) ?? ".", "Takes");

                foreach (var message in _allMessages)
                {
                    var persistedTakes = new List<TakeInfo>();
                    for (var i = 0; i < message.AllTakes.Count; i++)
                    {
                        var take = message.AllTakes[i];
                        var persistedPath = PersistTakeFile(message.MsgId, i + 1, take.FilePath, takesFolder);
                        if (persistedPath is not null)
                        {
                            persistedTakes.Add(new TakeInfo { FilePath = persistedPath, Name = take.Name, Rating = take.Rating });
                        }
                    }

                    var selectedIndex = message.AllTakes.FindIndex(t =>
                        string.Equals(t.FilePath, message.RecordedTakePath, StringComparison.OrdinalIgnoreCase));
                    message.AllTakes = persistedTakes;
                    message.RecordedTakePath = selectedIndex >= 0 && selectedIndex < persistedTakes.Count
                        ? persistedTakes[selectedIndex].FilePath
                        : persistedTakes.LastOrDefault()?.FilePath ?? message.RecordedTakePath;
                }

                var project = new SpotterProject
                {
                    DestinationFolder = DestinationTextBox.Text,
                    PackName = PackNameTextBox.Text,
                    RecordingSampleRate = SampleRateComboBox.SelectedItem is int rate ? rate : _settings.RecordingSampleRate,
                    RecordingBitsPerSample = BitsPerSampleComboBox.SelectedItem is int bits ? bits : _settings.RecordingBitsPerSample,
                    SquelchEnabled = SquelchEnabledCheckBox.IsChecked == true,
                    SquelchDurationMs = int.TryParse(SquelchDurationTextBox.Text, out var durationMs) ? durationMs : _settings.SquelchDurationMs,
                    SquelchVolume = double.TryParse(
                        SquelchVolumeTextBox.Text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var volume) ? volume : _settings.SquelchVolume,
                    DefaultGoogleVoiceName = DefaultVoiceComboBox.SelectedValue as string ?? _settings.DefaultGoogleVoiceName
                };

                foreach (var message in _allMessages)
                {
                    project.Messages.Add(new SpotterProjectMessage
                    {
                        MsgId = message.MsgId,
                        WavFileName = message.WavFileName,
                        Text = message.Text,
                        Enabled = message.Enabled,
                        SourceType = message.SourceType,
                        GoogleVoiceName = message.GoogleVoiceName,
                        RecordedTakePath = message.RecordedTakePath,
                        AllTakes = message.AllTakes.Select(t => new TakeInfo { FilePath = t.FilePath, Name = t.Name, Rating = t.Rating }).ToList(),
                        IsComment = message.IsComment,
                        RawLine = message.RawLine,
                        Status = message.Status,
                        AddSquelchStart = message.AddSquelchStart,
                        AddSquelchEnd = message.AddSquelchEnd,
                        AddRadioEffect = message.AddRadioEffect
                    });
                }

                SpotterProjectService.Save(dialog.FileName, project);
                StatusTextBlock.Text = $"{LocalizationManager.GetString("Str_ProjectSaved")} {dialog.FileName}";
                _isDirty = false;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{LocalizationManager.GetString("Str_ProjectSaveFailed")} {ex.Message}",
                    LocalizationManager.GetString("Str_SaveProjectTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Ensures a recorded take referenced by a message survives beyond the
        /// temporary folder it may have been created in (e.g. by TakesWindow),
        /// by copying it into a project-owned "Takes" folder next to the
        /// project file. Returns the stable path to store, or the original
        /// value if there is nothing to persist.
        /// </summary>
        private static string? PersistTakeFile(string msgId, int takeNumber, string? takePath, string takesFolder)
        {
            if (string.IsNullOrWhiteSpace(takePath) || !File.Exists(takePath))
            {
                return null;
            }

            var fullTakesFolder = Path.GetFullPath(takesFolder);
            var fullTakeDirectory = Path.GetFullPath(Path.GetDirectoryName(takePath) ?? ".");

            // Already stored inside the project's Takes folder: nothing to do.
            if (string.Equals(fullTakeDirectory, fullTakesFolder, StringComparison.OrdinalIgnoreCase))
            {
                return takePath;
            }

            Directory.CreateDirectory(fullTakesFolder);

            var destinationPath = Path.Combine(fullTakesFolder, $"{msgId}_take{takeNumber}.wav");
            File.Copy(takePath, destinationPath, overwrite: true);

            return destinationPath;
        }

        private void OpenProjectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscardUnsavedChanges())
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Filter = SpotterProjectService.FileFilter,
                DefaultExt = SpotterProjectService.DefaultExtension
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                var project = SpotterProjectService.Load(dialog.FileName);

                foreach (var message in _allMessages)
                {
                    message.PropertyChanged -= Message_PropertyChanged;
                }

                _allMessages.Clear();
                foreach (var message in project.Messages)
                {
                    _allMessages.Add(new SpotterMessage
                    {
                        MsgId = message.MsgId,
                        WavFileName = message.WavFileName,
                        Text = message.Text,
                        Enabled = message.Enabled,
                        SourceType = message.SourceType,
                        GoogleVoiceName = message.GoogleVoiceName,
                        RecordedTakePath = message.RecordedTakePath,
                        AllTakes = message.AllTakes.Select(t => new TakeInfo { FilePath = t.FilePath, Name = t.Name, Rating = t.Rating }).ToList(),
                        IsComment = message.IsComment,
                        RawLine = message.RawLine,
                        Status = message.Status,
                        AddSquelchStart = message.AddSquelchStart,
                        AddSquelchEnd = message.AddSquelchEnd,
                        AddRadioEffect = message.AddRadioEffect
                    });
                }

                _knownMsgIds.Clear();
                foreach (var msgId in _allMessages.Select(m => m.MsgId))
                {
                    _knownMsgIds.Add(msgId);
                }

                foreach (var message in _allMessages)
                {
                    message.PropertyChanged += Message_PropertyChanged;
                }

                DestinationTextBox.Text = project.DestinationFolder;
                PackNameTextBox.Text = project.PackName;

                _settings.RecordingSampleRate = project.RecordingSampleRate ?? _settings.RecordingSampleRate;
                _settings.RecordingBitsPerSample = project.RecordingBitsPerSample ?? _settings.RecordingBitsPerSample;
                _settings.SquelchEnabled = project.SquelchEnabled ?? _settings.SquelchEnabled;
                _settings.SquelchDurationMs = project.SquelchDurationMs ?? _settings.SquelchDurationMs;
                _settings.SquelchVolume = project.SquelchVolume ?? _settings.SquelchVolume;
                _settings.DefaultGoogleVoiceName = project.DefaultGoogleVoiceName ?? _settings.DefaultGoogleVoiceName;

                SampleRateComboBox.SelectedItem = _settings.RecordingSampleRate;
                BitsPerSampleComboBox.SelectedItem = _settings.RecordingBitsPerSample;
                SquelchEnabledCheckBox.IsChecked = _settings.SquelchEnabled;
                SquelchDurationTextBox.Text = _settings.SquelchDurationMs.ToString();
                SquelchVolumeTextBox.Text = _settings.SquelchVolume.ToString(System.Globalization.CultureInfo.InvariantCulture);
                DefaultVoiceComboBox.SelectedValue = _settings.DefaultGoogleVoiceName;

                RefreshView(FilterTextBox.Text);
                RefreshExportedFlags();
                StatusTextBlock.Text = $"{LocalizationManager.GetString("Str_ProjectLoaded")} {dialog.FileName}";
                _isDirty = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{LocalizationManager.GetString("Str_ProjectLoadFailed")} {ex.Message}",
                    LocalizationManager.GetString("Str_OpenProjectTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Builds the PackGenerationOptions used for both real generation and
        /// the pending-sounds preview, based on current settings/UI state.
        /// </summary>
        private PackGenerationOptions BuildPackGenerationOptions(string outputFolder)
        {
            return new PackGenerationOptions
            {
                OutputFolder = outputFolder,
                GoogleApiKey = _settings.GoogleApiKey,
                DefaultGoogleVoiceName = _settings.DefaultGoogleVoiceName,
                GoogleOutputSampleRate = _settings.RecordingSampleRate,
                GoogleOutputBitsPerSample = _settings.RecordingBitsPerSample,
                SquelchEnabled = _settings.SquelchEnabled,
                SquelchDurationMs = _settings.SquelchDurationMs,
                SquelchVolume = _settings.SquelchVolume,
                RadioEffectEnabled = _settings.RadioEffectEnabled,
                RadioEffectLowCutHz = _settings.RadioEffectLowCutHz,
                RadioEffectHighCutHz = _settings.RadioEffectHighCutHz,
                RadioEffectDistortion = _settings.RadioEffectDistortion,
                OutputVolume = _settings.OutputVolume,
                RequiredMsgIds = _knownMsgIds,
                OnlyGenerateChanged = OnlyChangedCheckBox.IsChecked == true
            };
        }

        /// <summary>
        /// Recomputes each message's IsExported flag by checking which rows
        /// would be skipped (i.e. already up to date) on the next export,
        /// so the "Already exported" column reflects the real manifest state.
        /// </summary>
        private void RefreshExportedFlags()
        {
            if (string.IsNullOrWhiteSpace(DestinationTextBox.Text))
            {
                foreach (var message in _allMessages)
                {
                    message.IsExported = false;
                }
                return;
            }

            var packName = string.IsNullOrWhiteSpace(PackNameTextBox.Text) ? "MySpotterPack" : PackNameTextBox.Text.Trim();
            var outputFolder = Path.Combine(DestinationTextBox.Text, packName);
            var options = BuildPackGenerationOptions(outputFolder);
            options.OnlyGenerateChanged = true;

            try
            {
                var pending = new HashSet<SpotterMessage>(PackGenerator.GetPendingMessages(_allMessages, options));
                foreach (var message in _allMessages)
                {
                    message.IsExported = message.Enabled && !string.IsNullOrWhiteSpace(message.Text) && !pending.Contains(message);
                }
            }
            catch (Exception)
            {
                foreach (var message in _allMessages)
                {
                    message.IsExported = false;
                }
            }
        }

        private void PendingSoundsExpander_Expanded(object sender, RoutedEventArgs e)
        {
            RefreshPendingSoundsList();
        }

        private void OnlyChangedCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            RefreshPendingSoundsList();
        }

        /// <summary>
        /// Sets or clears the given per-row checkbox flag (AddSquelchStart,
        /// AddSquelchEnd, or AddRadioEffect) for every message in one go.
        /// The button's Tag encodes "PropertyName:True" or "PropertyName:False".
        /// </summary>
        private void BulkToggleColumnButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string tag } ||
                tag.Split(':') is not [var propertyName, var valueText] ||
                !bool.TryParse(valueText, out var value))
            {
                return;
            }

            foreach (var message in _allMessages)
            {
                switch (propertyName)
                {
                    case nameof(SpotterMessage.AddSquelchStart):
                        message.AddSquelchStart = value;
                        break;
                    case nameof(SpotterMessage.AddSquelchEnd):
                        message.AddSquelchEnd = value;
                        break;
                    case nameof(SpotterMessage.AddRadioEffect):
                        message.AddRadioEffect = value;
                        break;
                }
            }
        }

        /// <summary>
        /// Recomputes and displays the list of sounds that would actually be
        /// (re)generated on the next export, based on the current output
        /// folder/name and the "only changed" checkbox state.
        /// </summary>
        private void RefreshPendingSoundsList()
        {
            if (PendingSoundsExpander is not { IsExpanded: true })
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(DestinationTextBox.Text))
            {
                PendingSoundsListBox.ItemsSource = null;
                return;
            }

            var packName = string.IsNullOrWhiteSpace(PackNameTextBox.Text) ? "MySpotterPack" : PackNameTextBox.Text.Trim();
            var outputFolder = Path.Combine(DestinationTextBox.Text, packName);
            var options = BuildPackGenerationOptions(outputFolder);

            try
            {
                var pending = PackGenerator.GetPendingMessages(_allMessages, options);
                PendingSoundsListBox.ItemsSource = pending
                    .Select(m => $"{m.MsgId}: {m.Text}")
                    .ToList();
            }
            catch (Exception)
            {
                PendingSoundsListBox.ItemsSource = null;
            }
        }

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(DestinationTextBox.Text))
            {
                MessageBox.Show(this, LocalizationManager.GetString("Str_ChooseDestinationFirst"),
                    LocalizationManager.GetString("Str_AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var packName = string.IsNullOrWhiteSpace(PackNameTextBox.Text) ? "MySpotterPack" : PackNameTextBox.Text.Trim();
            var outputFolder = Path.Combine(DestinationTextBox.Text, packName);

            var options = BuildPackGenerationOptions(outputFolder);

            GenerateButton.IsEnabled = false;
            GenerationProgressBar.Value = 0;
            StatusTextBlock.Text = LocalizationManager.GetString("Str_Generating");

            var progress = new Progress<PackGenerationProgress>(p =>
            {
                GenerationProgressBar.Maximum = p.Total;
                GenerationProgressBar.Value = p.Current;
                StatusTextBlock.Text = $"{p.Current}/{p.Total}: {p.CurrentMsgId}";
            });

            try
            {
                await PackGenerator.GenerateAsync(_allMessages, options, progress);
                StatusTextBlock.Text = LocalizationManager.GetString("Str_Done");
                RefreshPendingSoundsList();
                RefreshExportedFlags();
                MessageBox.Show(this, $"{LocalizationManager.GetString("Str_PackGeneratedSuccess")}\n{outputFolder}",
                    LocalizationManager.GetString("Str_AppTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = LocalizationManager.GetString("Str_Failed");
                MessageBox.Show(this, $"{LocalizationManager.GetString("Str_PackGenerationFailed")} {ex.Message}",
                    LocalizationManager.GetString("Str_AppTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                GenerateButton.IsEnabled = true;
            }
        }
    }
}