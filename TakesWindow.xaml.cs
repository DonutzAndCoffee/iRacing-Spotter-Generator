using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.Wave;
using iRacing_Spotter_Generator.Models;
using iRacing_Spotter_Generator.Services;

namespace iRacing_Spotter_Generator
{
    public class TakeItem : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private int? _rating;

        public required string FilePath { get; init; }

        /// <summary>
        /// User-editable, friendly name for this take (e.g. "Take 2" or a
        /// custom label), shown and editable in the takes list.
        /// </summary>
        public string Name
        {
            get => _name;
            set
            {
                if (_name == value)
                {
                    return;
                }

                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        /// <summary>
        /// Optional quality ranking from 1 (worst) to 10 (best), so takes can
        /// be compared later when revisiting a message.
        /// </summary>
        public int? Rating
        {
            get => _rating;
            set
            {
                if (_rating == value)
                {
                    return;
                }

                _rating = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Rating)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RatingText)));
            }
        }

        /// <summary>
        /// String-based view of <see cref="Rating"/> for simple ComboBox
        /// binding (empty string means "not rated").
        /// </summary>
        public string RatingText
        {
            get => Rating?.ToString() ?? string.Empty;
            set => Rating = int.TryParse(value, out var parsed) ? parsed : null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class TakesWindow : Window
    {
        private readonly string _tempFolder;
        private readonly ObservableCollection<TakeItem> _takes = new();
        private readonly int _sampleRate;
        private readonly int _bitsPerSample;
        private AudioRecorder? _recorder;
        private int _takeCounter;
        private bool _suppressSliderEvents;
        private float[] _waveformPeaks = Array.Empty<float>();
        private double _currentTakeDurationSeconds;

        private WaveOutEvent? _trimPlayer;
        private AudioFileReader? _trimReader;
        private string? _trimPlaybackPath;
        private double _trimPlaybackRegionStart;
        private double _trimPlaybackRegionEnd;
        private readonly DispatcherTimer _playbackTimer;

        public string? SelectedTakePath { get; private set; }

        /// <summary>
        /// All takes present when the dialog was closed (recorded and/or
        /// trimmed), including their names and ratings, so the caller can
        /// persist the full set rather than only the currently selected take.
        /// </summary>
        public IReadOnlyList<TakeInfo> AllTakes => _takes
            .Select(t => new TakeInfo { FilePath = t.FilePath, Name = t.Name, Rating = t.Rating })
            .ToList();

        public TakesWindow(string storageKey, string msgId, string messageText, IEnumerable<TakeInfo>? existingTakes, string? selectedTakePath, int sampleRate = 5512, int bitsPerSample = 8)
        {
            InitializeComponent();

            _sampleRate = sampleRate;
            _bitsPerSample = bitsPerSample;

            _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _playbackTimer.Tick += PlaybackTimer_Tick;

            MessageTextBlock.Text = $"{msgId}: \"{messageText}\"";
            QualityTextBlock.Text = $"Aufnahme in hoher Qualität, Export in Zielqualität: {sampleRate} Hz / {bitsPerSample} Bit (wie iRacing)";
            TakesListBox.ItemsSource = _takes;

            // Use a stable, per-row folder (keyed by the message's own id)
            // instead of a random folder each time, so takes recorded across
            // multiple TakesWindow sessions are not orphaned/lost. Stored under
            // %AppData% (like AppSettingsService) rather than the OS temp
            // folder, since temp files can be cleaned up at any time and would
            // silently delete recorded takes.
            _tempFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "iRacingSpotterGenerator", "Takes", storageKey);
            Directory.CreateDirectory(_tempFolder);

            Closed += (_, _) => StopTrimPlayback();

            TakeItem? initialSelection = null;
            var takeIndex = 0;
            if (existingTakes is not null)
            {
                foreach (var existingTake in existingTakes)
                {
                    if (string.IsNullOrWhiteSpace(existingTake.FilePath) || !File.Exists(existingTake.FilePath))
                    {
                        continue;
                    }

                    takeIndex++;
                    var take = new TakeItem
                    {
                        FilePath = existingTake.FilePath,
                        Name = string.IsNullOrWhiteSpace(existingTake.Name) ? $"Take {takeIndex}" : existingTake.Name,
                        Rating = existingTake.Rating
                    };
                    _takes.Add(take);

                    if (initialSelection is null || string.Equals(existingTake.FilePath, selectedTakePath, StringComparison.OrdinalIgnoreCase))
                    {
                        initialSelection = take;
                    }
                }
            }

            if (initialSelection is not null)
            {
                TakesListBox.SelectedItem = initialSelection;
            }

            _takeCounter = takeIndex;
        }

        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (_recorder is { IsRecording: true })
            {
                _recorder.Stop();
                return;
            }

            _takeCounter++;
            var filePath = Path.Combine(_tempFolder, $"take_{_takeCounter}.wav");

            _recorder = new AudioRecorder();
            _recorder.RecordingStopped += (_, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    var take = new TakeItem { FilePath = filePath, Name = $"Take {_takeCounter}" };
                    _takes.Add(take);
                    TakesListBox.SelectedItem = take;

                    RecordButton.Content = "● Aufnahme starten";
                    RecordingStatusTextBlock.Text = string.Empty;
                });
            };

            try
            {
                // Always record at the recorder's own high-quality default so
                // the raw take doesn't lose quality; downsampling to the
                // target quality happens only on preview/export.
                _recorder.Start(filePath);
                RecordButton.Content = "■ Aufnahme stoppen";
                RecordingStatusTextBlock.Text = "Aufnahme läuft...";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Aufnahme konnte nicht gestartet werden: {ex.Message}",
                    "Aufnahme", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (TakesListBox.SelectedItem is not TakeItem take || !File.Exists(take.FilePath))
            {
                return;
            }

            try
            {
                using var player = new SoundPlayer(take.FilePath);
                player.PlaySync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Wiedergabe fehlgeschlagen: {ex.Message}",
                    "Aufnahme", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TakesListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            StopTrimPlayback();

            if (TakesListBox.SelectedItem is not TakeItem take || !File.Exists(take.FilePath))
            {
                TrimStartSlider.IsEnabled = false;
                TrimEndSlider.IsEnabled = false;
                TrimDurationTextBlock.Text = string.Empty;
                return;
            }

            try
            {
                var duration = AudioTrimHelper.GetDuration(take.FilePath);
                _currentTakeDurationSeconds = duration.TotalSeconds;

                _suppressSliderEvents = true;
                TrimStartSlider.Minimum = 0;
                TrimStartSlider.Maximum = duration.TotalSeconds;
                TrimStartSlider.Value = 0;
                TrimEndSlider.Minimum = 0;
                TrimEndSlider.Maximum = duration.TotalSeconds;
                TrimEndSlider.Value = duration.TotalSeconds;
                _suppressSliderEvents = false;

                TrimStartSlider.IsEnabled = true;
                TrimEndSlider.IsEnabled = true;

                UpdateTrimDurationText(duration.TotalSeconds);

                var bucketCount = Math.Max(50, (int)WaveformCanvas.ActualWidth);
                _waveformPeaks = AudioWaveformHelper.GetPeaks(take.FilePath, bucketCount);
                DrawWaveform();
                UpdateTrimOverlay();
            }
            catch (Exception ex)
            {
                TrimStartSlider.IsEnabled = false;
                TrimEndSlider.IsEnabled = false;
                TrimDurationTextBlock.Text = $"Datei konnte nicht gelesen werden: {ex.Message}";
                _waveformPeaks = Array.Empty<float>();
                WaveformPolyline.Points.Clear();
            }
        }

        private void WaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (TakesListBox.SelectedItem is TakeItem take && File.Exists(take.FilePath))
            {
                var bucketCount = Math.Max(50, (int)WaveformCanvas.ActualWidth);
                try
                {
                    _waveformPeaks = AudioWaveformHelper.GetPeaks(take.FilePath, bucketCount);
                }
                catch (IOException)
                {
                    // Keep the previous peaks if the file can't be read right now.
                }
            }

            DrawWaveform();
            UpdateTrimOverlay();
        }

        private void DrawWaveform()
        {
            WaveformPolyline.Points.Clear();

            var width = WaveformCanvas.ActualWidth;
            var height = WaveformCanvas.ActualHeight;

            if (_waveformPeaks.Length == 0 || width <= 0 || height <= 0)
            {
                return;
            }

            var midY = height / 2;
            var points = new PointCollection();
            var denominator = Math.Max(1, _waveformPeaks.Length - 1);

            for (var i = 0; i < _waveformPeaks.Length; i++)
            {
                var x = width * i / (double)denominator;
                var amplitude = _waveformPeaks[i] * midY;
                points.Add(new Point(x, midY - amplitude));
            }

            for (var i = _waveformPeaks.Length - 1; i >= 0; i--)
            {
                var x = width * i / (double)denominator;
                var amplitude = _waveformPeaks[i] * midY;
                points.Add(new Point(x, midY + amplitude));
            }

            WaveformPolyline.Points = points;
        }

        private void UpdateTrimOverlay()
        {
            var width = WaveformCanvas.ActualWidth;
            var height = WaveformCanvas.ActualHeight;

            if (_currentTakeDurationSeconds <= 0 || width <= 0 || height <= 0)
            {
                TrimStartOverlay.Width = 0;
                TrimEndOverlay.Width = 0;
                return;
            }

            var startX = width * TrimStartSlider.Value / _currentTakeDurationSeconds;
            var endX = width * TrimEndSlider.Value / _currentTakeDurationSeconds;

            TrimStartOverlay.Width = Math.Max(0, startX);
            TrimStartOverlay.Height = height;
            Canvas.SetLeft(TrimStartOverlay, 0);
            Canvas.SetTop(TrimStartOverlay, 0);

            TrimEndOverlay.Width = Math.Max(0, width - endX);
            TrimEndOverlay.Height = height;
            Canvas.SetLeft(TrimEndOverlay, endX);
            Canvas.SetTop(TrimEndOverlay, 0);

            TrimStartLine.X1 = startX;
            TrimStartLine.X2 = startX;
            TrimStartLine.Y2 = height;

            TrimEndLine.X1 = endX;
            TrimEndLine.X2 = endX;
            TrimEndLine.Y2 = height;
        }

        private void TrimSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressSliderEvents)
            {
                return;
            }

            if (TrimStartSlider.Value >= TrimEndSlider.Value)
            {
                _suppressSliderEvents = true;
                if (sender == TrimStartSlider)
                {
                    TrimStartSlider.Value = Math.Max(0, TrimEndSlider.Value - 0.05);
                }
                else
                {
                    TrimEndSlider.Value = Math.Min(TrimEndSlider.Maximum, TrimStartSlider.Value + 0.05);
                }
                _suppressSliderEvents = false;
            }

            UpdateTrimDurationText(TrimEndSlider.Value - TrimStartSlider.Value);
            UpdateTrimOverlay();

            if (_trimPlayer is not null)
            {
                _trimPlaybackRegionStart = TrimStartSlider.Value;
                _trimPlaybackRegionEnd = TrimEndSlider.Value;
            }
        }

        private void UpdateTrimDurationText(double resultSeconds)
        {
            TrimDurationTextBlock.Text =
                $"Start: {TrimStartSlider.Value:0.00}s, Ende: {TrimEndSlider.Value:0.00}s, L\u00E4nge nach Schnitt: {resultSeconds:0.00}s";
        }

        private void PlayPauseTrimButton_Click(object sender, RoutedEventArgs e)
        {
            if (_trimPlayer is { PlaybackState: PlaybackState.Playing })
            {
                _trimPlayer.Pause();
                _playbackTimer.Stop();
                PlayPauseTrimButton.Content = "▶ Ausschnitt abspielen";
                return;
            }

            if (_trimPlayer is { PlaybackState: PlaybackState.Paused } && _trimReader is not null)
            {
                // Resume, but stop automatically once we reach the trim end.
                _trimPlayer.Play();
                _playbackTimer.Start();
                PlayPauseTrimButton.Content = "⏸ Pause";
                return;
            }

            StartTrimPlayback(TrimStartSlider.Value);
        }

        private void StartTrimPlayback(double startSeconds)
        {
            if (TakesListBox.SelectedItem is not TakeItem take || !File.Exists(take.FilePath))
            {
                return;
            }

            StopTrimPlayback();

            try
            {
                _trimPlaybackPath = take.FilePath;
                _trimPlaybackRegionStart = TrimStartSlider.Value;
                _trimPlaybackRegionEnd = TrimEndSlider.Value;

                var playbackPath = take.FilePath;
                _trimReader = new AudioFileReader(playbackPath);
                _trimReader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(startSeconds, _trimPlaybackRegionStart, _trimPlaybackRegionEnd));

                _trimPlayer = new WaveOutEvent();
                _trimPlayer.Init(_trimReader);
                _trimPlayer.PlaybackStopped += TrimPlayer_PlaybackStopped;
                _trimPlayer.Play();

                PlayPauseTrimButton.Content = "⏸ Pause";
                PlaybackPositionLine.Visibility = Visibility.Visible;
                _playbackTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Wiedergabe fehlgeschlagen: {ex.Message}",
                    "Schnitt-Tool", MessageBoxButton.OK, MessageBoxImage.Error);
                StopTrimPlayback();
            }
        }

        private void TrimPlayer_PlaybackStopped(object? sender, StoppedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _playbackTimer.Stop();
                PlayPauseTrimButton.Content = "▶ Ausschnitt abspielen";
                PlaybackPositionLine.Visibility = Visibility.Collapsed;
            });
        }

        private void StopTrimPlayback()
        {
            _playbackTimer.Stop();

            if (_trimPlayer is not null)
            {
                _trimPlayer.PlaybackStopped -= TrimPlayer_PlaybackStopped;
                _trimPlayer.Stop();
                _trimPlayer.Dispose();
                _trimPlayer = null;
            }

            _trimReader?.Dispose();
            _trimReader = null;
            _trimPlaybackPath = null;

            PlayPauseTrimButton.Content = "▶ Ausschnitt abspielen";
            PlaybackPositionLine.Visibility = Visibility.Collapsed;
        }

        private void PlaybackTimer_Tick(object? sender, EventArgs e)
        {
            if (_trimReader is null || _trimPlayer is null)
            {
                return;
            }

            var currentSeconds = _trimReader.CurrentTime.TotalSeconds;

            if (currentSeconds >= _trimPlaybackRegionEnd)
            {
                StopTrimPlayback();
                return;
            }

            UpdatePlaybackPositionLine(currentSeconds);
        }

        private void UpdatePlaybackPositionLine(double positionSeconds)
        {
            var width = WaveformCanvas.ActualWidth;
            var height = WaveformCanvas.ActualHeight;

            if (_currentTakeDurationSeconds <= 0 || width <= 0 || height <= 0)
            {
                return;
            }

            var x = width * positionSeconds / _currentTakeDurationSeconds;
            PlaybackPositionLine.X1 = x;
            PlaybackPositionLine.X2 = x;
            PlaybackPositionLine.Y2 = height;
        }

        private void WaveformCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (TakesListBox.SelectedItem is not TakeItem || _currentTakeDurationSeconds <= 0)
            {
                return;
            }

            var width = WaveformCanvas.ActualWidth;
            if (width <= 0)
            {
                return;
            }

            var clickX = e.GetPosition(WaveformCanvas).X;
            var seekSeconds = Math.Clamp(clickX / width * _currentTakeDurationSeconds, 0, _currentTakeDurationSeconds);

            // Jumping within the waveform always plays from that position,
            // clamped to the current trim start/end so the region stays meaningful.
            var wasPlaying = _trimPlayer is { PlaybackState: PlaybackState.Playing };
            StartTrimPlayback(seekSeconds);

            if (!wasPlaying)
            {
                UpdatePlaybackPositionLine(seekSeconds);
            }
        }

        private void ApplyTrimButton_Click(object sender, RoutedEventArgs e)
        {
            if (TakesListBox.SelectedItem is not TakeItem take || !File.Exists(take.FilePath))
            {
                MessageBox.Show(this, "Bitte zuerst einen Take auswählen.",
                    "Schnitt-Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StopTrimPlayback();

            var start = TimeSpan.FromSeconds(TrimStartSlider.Value);
            var end = TimeSpan.FromSeconds(TrimEndSlider.Value);

            _takeCounter++;
            var trimmedPath = Path.Combine(_tempFolder, $"take_{_takeCounter}_trimmed.wav");

            try
            {
                AudioTrimHelper.Trim(take.FilePath, trimmedPath, start, end);

                var trimmedTake = new TakeItem { FilePath = trimmedPath, Name = $"{take.Name} (geschnitten)" };
                _takes.Add(trimmedTake);
                TakesListBox.SelectedItem = trimmedTake;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Zuschneiden fehlgeschlagen: {ex.Message}",
                    "Schnitt-Tool", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (TakesListBox.SelectedItem is not TakeItem take)
            {
                return;
            }

            StopTrimPlayback();
            _takes.Remove(take);

            try
            {
                if (File.Exists(take.FilePath))
                {
                    File.Delete(take.FilePath);
                }
            }
            catch (IOException)
            {
                // Ignore delete failures for temp files.
            }
        }

        private void UseTakeButton_Click(object sender, RoutedEventArgs e)
        {
            if (TakesListBox.SelectedItem is not TakeItem take)
            {
                MessageBox.Show(this, "Bitte zuerst einen Take auswählen oder aufnehmen.",
                    "Aufnahme", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StopTrimPlayback();
            SelectedTakePath = take.FilePath;
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
