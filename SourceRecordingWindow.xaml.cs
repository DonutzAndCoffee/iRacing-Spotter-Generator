using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.Wave;
using iRacing_Spotter_Generator.Models;
using iRacing_Spotter_Generator.Services;

namespace iRacing_Spotter_Generator
{
    /// <summary>
    /// Manages long, imported "source recordings" (e.g. full race sessions)
    /// and lets the operator cut out a segment to use as a take for the
    /// message that opened this window (if any). Overlapping usage of a
    /// source recording's time ranges is allowed; already-used regions are
    /// shown for reference only.
    /// </summary>
    public partial class SourceRecordingWindow : Window
    {
        private readonly string? _messageId;
        private readonly string? _messageText;
        private readonly int _sampleRate;
        private readonly int _bitsPerSample;
        private readonly ObservableCollection<SourceRecording> _recordings = new();
        private List<SourceRecording> _catalog;
        private bool _suppressSliderEvents;
        private float[] _waveformPeaks = Array.Empty<float>();
        private double _currentDurationSeconds;

        private WaveOutEvent? _trimPlayer;
        private AudioFileReader? _trimReader;
        private double _trimPlaybackRegionStart;
        private double _trimPlaybackRegionEnd;
        private readonly DispatcherTimer _playbackTimer;

        /// <summary>Path to the cut-out segment, set once the user confirms a cut.</summary>
        public string? CutResultPath { get; private set; }

        public SourceRecordingWindow(string? messageId, string? messageText, int sampleRate = 5512, int bitsPerSample = 8)
        {
            InitializeComponent();

            _messageId = messageId;
            _messageText = messageText;
            _sampleRate = sampleRate;
            _bitsPerSample = bitsPerSample;

            _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _playbackTimer.Tick += PlaybackTimer_Tick;

            PlayPauseTrimButton.Content = LocalizationManager.GetString("Str_PlayTrimSegment");

            // Only useful in "cut for a message" mode; when just managing the
            // catalog there is nothing to hand back to a caller.
            UseCutButton.IsEnabled = !string.IsNullOrEmpty(_messageId);

            Closed += (_, _) => StopTrimPlayback();

            _catalog = SourceRecordingService.Load();
            foreach (var recording in _catalog)
            {
                _recordings.Add(recording);
            }

            RecordingsListBox.ItemsSource = _recordings;
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = LocalizationManager.GetString("Str_ImportSourceRecording"),
                Filter = "Audio/Video files (*.wav;*.mp3;*.mp4)|*.wav;*.mp3;*.mp4|All files (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            foreach (var sourcePath in dialog.FileNames)
            {
                try
                {
                    StatusTextBlock.Text = string.Format(LocalizationManager.GetString("Str_ImportingSourceRecording"), Path.GetFileName(sourcePath));

                    var recording = SourceRecordingService.Import(sourcePath, _sampleRate, _bitsPerSample);
                    _catalog.Add(recording);
                    _recordings.Add(recording);
                    RecordingsListBox.SelectedItem = recording;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(LocalizationManager.GetString("Str_ImportSourceRecordingFailed"), ex.Message),
                        LocalizationManager.GetString("Str_SourceRecordingsWindowTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            StatusTextBlock.Text = string.Empty;
            SourceRecordingService.Save(_catalog);
        }

        private void DeleteRecordingButton_Click(object sender, RoutedEventArgs e)
        {
            if (RecordingsListBox.SelectedItem is not SourceRecording recording)
            {
                return;
            }

            var result = MessageBox.Show(this, LocalizationManager.GetString("Str_ConfirmDeleteSourceRecording"),
                LocalizationManager.GetString("Str_SourceRecordingsWindowTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            StopTrimPlayback();
            SourceRecordingService.DeleteFiles(recording);
            _catalog.Remove(recording);
            _recordings.Remove(recording);
            SourceRecordingService.Save(_catalog);
        }

        private void RecordingsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            StopTrimPlayback();
            UsedRegionsListBox.ItemsSource = null;

            if (RecordingsListBox.SelectedItem is not SourceRecording recording || !File.Exists(recording.FilePath))
            {
                TrimStartSlider.IsEnabled = false;
                TrimEndSlider.IsEnabled = false;
                TrimDurationTextBlock.Text = string.Empty;
                _waveformPeaks = Array.Empty<float>();
                WaveformPolyline.Points.Clear();
                return;
            }

            try
            {
                var duration = AudioTrimHelper.GetDuration(recording.FilePath);
                _currentDurationSeconds = duration.TotalSeconds;

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

                LoadWaveform(recording);
                UsedRegionsListBox.ItemsSource = recording.UsedRegions;
            }
            catch (Exception ex)
            {
                TrimStartSlider.IsEnabled = false;
                TrimEndSlider.IsEnabled = false;
                TrimDurationTextBlock.Text = string.Format(LocalizationManager.GetString("Str_FileReadFailed"), ex.Message);
                _waveformPeaks = Array.Empty<float>();
                WaveformPolyline.Points.Clear();
            }
        }

        private void LoadWaveform(SourceRecording recording)
        {
            // Long (up to ~60 minute) source recordings must not be
            // re-decoded sample-by-sample every time the waveform is shown,
            // so the full-resolution peaks are cached on disk and only
            // downsampled here to match the canvas width.
            var cachedPeaks = WaveformPeakCache.GetOrComputePeaks(recording.FilePath);
            var bucketCount = Math.Max(50, (int)WaveformCanvas.ActualWidth);
            _waveformPeaks = WaveformPeakCache.Downsample(cachedPeaks, bucketCount);
            DrawWaveform();
            UpdateTrimOverlay();
            UpdateUsedRegionsOverlay(recording);
        }

        private void WaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (RecordingsListBox.SelectedItem is SourceRecording recording && File.Exists(recording.FilePath))
            {
                try
                {
                    var cachedPeaks = WaveformPeakCache.GetOrComputePeaks(recording.FilePath);
                    var bucketCount = Math.Max(50, (int)WaveformCanvas.ActualWidth);
                    _waveformPeaks = WaveformPeakCache.Downsample(cachedPeaks, bucketCount);
                }
                catch (IOException)
                {
                    // Keep the previous peaks if the file can't be read right now.
                }
            }

            DrawWaveform();
            UpdateTrimOverlay();

            if (RecordingsListBox.SelectedItem is SourceRecording current)
            {
                UpdateUsedRegionsOverlay(current);
            }
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

        /// <summary>
        /// Draws a semi-transparent marker over each already-used region so
        /// the operator can see at a glance which parts of the recording
        /// have been cut out before (purely informational; overlaps allowed).
        /// </summary>
        private void UpdateUsedRegionsOverlay(SourceRecording recording)
        {
            foreach (var child in WaveformCanvas.Children.OfType<System.Windows.Shapes.Rectangle>()
                         .Where(r => Equals(r.Tag, "UsedRegion")).ToList())
            {
                WaveformCanvas.Children.Remove(child);
            }

            var width = WaveformCanvas.ActualWidth;
            var height = WaveformCanvas.ActualHeight;

            if (_currentDurationSeconds <= 0 || width <= 0 || height <= 0)
            {
                return;
            }

            foreach (var region in recording.UsedRegions)
            {
                var startX = width * region.StartSeconds / _currentDurationSeconds;
                var endX = width * region.EndSeconds / _currentDurationSeconds;

                var rect = new System.Windows.Shapes.Rectangle
                {
                    Tag = "UsedRegion",
                    Fill = new SolidColorBrush(Color.FromArgb(90, 30, 144, 255)),
                    Width = Math.Max(1, endX - startX),
                    Height = height,
                    ToolTip = string.IsNullOrWhiteSpace(region.MessageText)
                        ? region.MessageId
                        : $"{region.MessageId}: {region.MessageText}"
                };
                Canvas.SetLeft(rect, startX);
                Canvas.SetTop(rect, 0);
                WaveformCanvas.Children.Insert(0, rect);
            }
        }

        private void UpdateTrimOverlay()
        {
            var width = WaveformCanvas.ActualWidth;
            var height = WaveformCanvas.ActualHeight;

            if (_currentDurationSeconds <= 0 || width <= 0 || height <= 0)
            {
                TrimStartOverlay.Width = 0;
                TrimEndOverlay.Width = 0;
                return;
            }

            var startX = width * TrimStartSlider.Value / _currentDurationSeconds;
            var endX = width * TrimEndSlider.Value / _currentDurationSeconds;

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
            TrimDurationTextBlock.Text = string.Format(
                LocalizationManager.GetString("Str_TrimDurationText"),
                TrimStartSlider.Value, TrimEndSlider.Value, resultSeconds);
        }

        private void PlayPauseTrimButton_Click(object sender, RoutedEventArgs e)
        {
            if (_trimPlayer is { PlaybackState: PlaybackState.Playing })
            {
                _trimPlayer.Pause();
                _playbackTimer.Stop();
                PlayPauseTrimButton.Content = LocalizationManager.GetString("Str_PlayTrimSegment");
                return;
            }

            if (_trimPlayer is { PlaybackState: PlaybackState.Paused } && _trimReader is not null)
            {
                _trimPlayer.Play();
                _playbackTimer.Start();
                PlayPauseTrimButton.Content = LocalizationManager.GetString("Str_PausePlayback");
                return;
            }

            StartTrimPlayback(TrimStartSlider.Value);
        }

        private void StartTrimPlayback(double startSeconds)
        {
            if (RecordingsListBox.SelectedItem is not SourceRecording recording || !File.Exists(recording.FilePath))
            {
                return;
            }

            StopTrimPlayback();

            try
            {
                _trimPlaybackRegionStart = TrimStartSlider.Value;
                _trimPlaybackRegionEnd = TrimEndSlider.Value;

                _trimReader = new AudioFileReader(recording.FilePath);
                _trimReader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(startSeconds, _trimPlaybackRegionStart, _trimPlaybackRegionEnd));

                _trimPlayer = new WaveOutEvent();
                _trimPlayer.Init(_trimReader);
                _trimPlayer.PlaybackStopped += TrimPlayer_PlaybackStopped;
                _trimPlayer.Play();

                PlayPauseTrimButton.Content = LocalizationManager.GetString("Str_PausePlayback");
                PlaybackPositionLine.Visibility = Visibility.Visible;
                _playbackTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(LocalizationManager.GetString("Str_PlaybackFailed"), ex.Message),
                    LocalizationManager.GetString("Str_SourceRecordingsWindowTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                StopTrimPlayback();
            }
        }

        private void TrimPlayer_PlaybackStopped(object? sender, StoppedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _playbackTimer.Stop();
                PlayPauseTrimButton.Content = LocalizationManager.GetString("Str_PlayTrimSegment");
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

            PlayPauseTrimButton.Content = LocalizationManager.GetString("Str_PlayTrimSegment");
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

            if (_currentDurationSeconds <= 0 || width <= 0 || height <= 0)
            {
                return;
            }

            var x = width * positionSeconds / _currentDurationSeconds;
            PlaybackPositionLine.X1 = x;
            PlaybackPositionLine.X2 = x;
            PlaybackPositionLine.Y2 = height;
        }

        private void WaveformCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (RecordingsListBox.SelectedItem is not SourceRecording || _currentDurationSeconds <= 0)
            {
                return;
            }

            var width = WaveformCanvas.ActualWidth;
            if (width <= 0)
            {
                return;
            }

            var clickX = e.GetPosition(WaveformCanvas).X;
            var seekSeconds = Math.Clamp(clickX / width * _currentDurationSeconds, 0, _currentDurationSeconds);

            var wasPlaying = _trimPlayer is { PlaybackState: PlaybackState.Playing };
            StartTrimPlayback(seekSeconds);

            if (!wasPlaying)
            {
                UpdatePlaybackPositionLine(seekSeconds);
            }
        }

        private void UseCutButton_Click(object sender, RoutedEventArgs e)
        {
            if (RecordingsListBox.SelectedItem is not SourceRecording recording || !File.Exists(recording.FilePath))
            {
                MessageBox.Show(this, LocalizationManager.GetString("Str_SelectSourceRecordingFirst"),
                    LocalizationManager.GetString("Str_SourceRecordingsWindowTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_messageId))
            {
                return;
            }

            StopTrimPlayback();

            var start = TimeSpan.FromSeconds(TrimStartSlider.Value);
            var end = TimeSpan.FromSeconds(TrimEndSlider.Value);

            var tempPath = Path.Combine(Path.GetTempPath(), $"sourcecut_{Guid.NewGuid():N}.wav");

            try
            {
                AudioTrimHelper.Trim(recording.FilePath, tempPath, start, end);

                // Overlapping usage is allowed by design, so the region is
                // simply recorded for reference without checking for clashes.
                recording.UsedRegions.Add(new UsedRegion
                {
                    MessageId = _messageId,
                    MessageText = _messageText ?? string.Empty,
                    StartSeconds = start.TotalSeconds,
                    EndSeconds = end.TotalSeconds
                });
                SourceRecordingService.Save(_catalog);

                CutResultPath = tempPath;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(LocalizationManager.GetString("Str_TrimFailed"), ex.Message),
                    LocalizationManager.GetString("Str_SourceRecordingsWindowTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
