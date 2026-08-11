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
    }
}
