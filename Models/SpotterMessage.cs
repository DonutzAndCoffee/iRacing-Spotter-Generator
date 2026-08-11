using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace iRacing_Spotter_Generator.Models
{
    /// <summary>
    /// Represents a single sample line of the spmsg.ini template
    /// (one msgId can have multiple SpotterMessage rows / variants).
    /// </summary>
    public class SpotterMessage : INotifyPropertyChanged
    {
        private string _text = string.Empty;
        private bool _enabled = true;
        private AudioSourceType _sourceType = AudioSourceType.GoogleAi;
        private string? _googleVoiceName;
        private string? _recordedTakePath;
        private RowStatus _status = RowStatus.ToDo;
        private bool _isExported;

        /// <summary>
        /// Stable identifier for this row, used to keep a persistent per-row
        /// folder of recorded takes (independent of temp files / project
        /// save location) so takes survive across TakesWindow sessions.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string MsgId { get; set; } = string.Empty;

        public string WavFileName { get; set; } = string.Empty;

        /// <summary>
        /// The spoken text for this sample. Setting this to an empty string
        /// or the row being disabled results in "NULL" being written out.
        /// </summary>
        public string Text
        {
            get => _text;
            set => SetField(ref _text, value);
        }

        /// <summary>
        /// Whether this sample should be included when generating the pack.
        /// When false the line is written out as NULL, NULL.
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set => SetField(ref _enabled, value);
        }

        /// <summary>
        /// Where the audio for this row should be produced from.
        /// </summary>
        public AudioSourceType SourceType
        {
            get => _sourceType;
            set => SetField(ref _sourceType, value);
        }

        /// <summary>
        /// Google Cloud TTS voice name (e.g. "en-US-Neural2-D"), used when SourceType is GoogleAi.
        /// </summary>
        public string? GoogleVoiceName
        {
            get => _googleVoiceName;
            set => SetField(ref _googleVoiceName, value);
        }

        /// <summary>
        /// Path to the chosen recorded take's WAV file, used when SourceType is Recording.
        /// </summary>
        public string? RecordedTakePath
        {
            get => _recordedTakePath;
            set => SetField(ref _recordedTakePath, value);
        }

        /// <summary>
        /// User-tracked review status (To Do / Satisfactory / Done), persisted
        /// with the project so authors can keep track of what still needs work.
        /// </summary>
        public RowStatus Status
        {
            get => _status;
            set => SetField(ref _status, value);
        }

        /// <summary>
        /// Transient (not persisted) flag indicating whether this row's wav file
        /// is already up to date in the last export output folder. Recomputed via
        /// <see cref="Services.PackGenerator.GetPendingMessages"/> after generation
        /// and on project load, never saved to the project file.
        /// </summary>
        public bool IsExported
        {
            get => _isExported;
            set => SetField(ref _isExported, value);
        }

        /// <summary>
        /// All recorded/trimmed takes gathered for this row across recording
        /// sessions (not just the currently selected one), each with an
        /// editable name and optional 1-10 quality rating, so they aren't
        /// lost when the recording dialog is closed and can be compared
        /// later when revisiting the message.
        /// </summary>
        public List<TakeInfo> AllTakes { get; set; } = new();

        /// <summary>
        /// True for comment / blank lines that should be preserved verbatim
        /// but never generated or edited.
        /// </summary>
        public bool IsComment { get; set; }

        public string RawLine { get; set; } = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
