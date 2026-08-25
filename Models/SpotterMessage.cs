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
        private bool _addSquelchStart = true;
        private bool _addSquelchEnd = true;
        private bool _addRadioEffect = true;
        private bool _addPttStart = true;
        private bool _addPttEnd = true;

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
        /// Whether to add a squelch signal at the start of this message's audio.
        /// Defaults to true for backward compatibility.
        /// </summary>
        public bool AddSquelchStart
        {
            get => _addSquelchStart;
            set => SetField(ref _addSquelchStart, value);
        }

        /// <summary>
        /// Whether to add a squelch signal at the end of this message's audio.
        /// When multiple messages are combined, only the final one(s) should have
        /// this enabled to avoid squelch interruptions between concatenated audio.
        /// Defaults to true for backward compatibility.
        /// </summary>
        public bool AddSquelchEnd
        {
            get => _addSquelchEnd;
            set => SetField(ref _addSquelchEnd, value);
        }

        /// <summary>
        /// Whether the flexible radio effect (bandpass filter + optional
        /// distortion) should be applied to this message's audio, when the
        /// effect is also enabled globally in the settings. Defaults to true
        /// so existing behavior is unchanged unless explicitly opted out.
        /// </summary>
        public bool AddRadioEffect
        {
            get => _addRadioEffect;
            set => SetField(ref _addRadioEffect, value);
        }

        /// <summary>
        /// Whether to add a PTT (push-to-talk) start beep at the start of this
        /// message's audio. Defaults to true for backward compatibility.
        /// </summary>
        public bool AddPttStart
        {
            get => _addPttStart;
            set => SetField(ref _addPttStart, value);
        }

        /// <summary>
        /// Whether to add a PTT (push-to-talk) end beep at the end of this
        /// message's audio. Defaults to true for backward compatibility.
        /// </summary>
        public bool AddPttEnd
        {
            get => _addPttEnd;
            set => SetField(ref _addPttEnd, value);
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
