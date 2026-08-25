using System.Collections.Generic;

namespace iRacing_Spotter_Generator.Models
{
    /// <summary>
    /// A saveable/loadable authoring project: the full editable state of a
    /// spotter pack (all messages plus destination/output settings), so long
    /// message lists don't have to be recreated from scratch every session.
    /// </summary>
    public class SpotterProject
    {
        /// <summary>
        /// Destination folder chosen for pack generation (last used value).
        /// </summary>
        public string DestinationFolder { get; set; } = string.Empty;

        /// <summary>
        /// Pack/subfolder name used for pack generation.
        /// </summary>
        public string PackName { get; set; } = "MySpotterPack";

        /// <summary>
        /// All spotter message rows, in their current edited state.
        /// </summary>
        public List<SpotterProjectMessage> Messages { get; set; } = new();

        /// <summary>
        /// Sample rate (Hz) used when recording/generating own takes for this
        /// project. Null for projects saved before this setting became
        /// project-specific, in which case the global default applies.
        /// </summary>
        public int? RecordingSampleRate { get; set; }

        /// <summary>
        /// Bits per sample used when recording/generating own takes for this
        /// project. Null for projects saved before this setting became
        /// project-specific, in which case the global default applies.
        /// </summary>
        public int? RecordingBitsPerSample { get; set; }

        /// <summary>
        /// Whether a short radio-style noise/click burst is automatically added
        /// at the start and end of every generated sample for this project.
        /// Null for projects saved before this setting became project-specific.
        /// </summary>
        public bool? SquelchEnabled { get; set; }

        /// <summary>
        /// Duration (in milliseconds) of the noise/click burst for this project.
        /// Null for projects saved before this setting became project-specific.
        /// </summary>
        public int? SquelchDurationMs { get; set; }

        /// <summary>
        /// Volume (0.0 - 1.0) of the noise/click burst for this project.
        /// Null for projects saved before this setting became project-specific.
        /// </summary>
        public double? SquelchVolume { get; set; }

        /// <summary>
        /// The default Google Cloud TTS voice name used for every message in
        /// this project that doesn't have an explicit voice selected. Null
        /// for projects saved before this setting became project-specific,
        /// in which case the global default applies.
        /// </summary>
        public string? DefaultGoogleVoiceName { get; set; }
    }

    /// <summary>
    /// Plain-data (serializable) copy of a <see cref="SpotterMessage"/> row.
    /// </summary>
    public class SpotterProjectMessage
    {
        public string MsgId { get; set; } = string.Empty;
        public string WavFileName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public AudioSourceType SourceType { get; set; } = AudioSourceType.GoogleAi;
        public string? GoogleVoiceName { get; set; }
        public string? RecordedTakePath { get; set; }
        public List<TakeInfo> AllTakes { get; set; } = new();
        public bool IsComment { get; set; }
        public string RawLine { get; set; } = string.Empty;
        public RowStatus Status { get; set; } = RowStatus.ToDo;
        public bool AddSquelchStart { get; set; } = true;
        public bool AddSquelchEnd { get; set; } = true;
        public bool AddRadioEffect { get; set; } = true;
        public bool AddPttStart { get; set; } = true;
        public bool AddPttEnd { get; set; } = true;
    }
}
