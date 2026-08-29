using System.Collections.Generic;

namespace iRacing_Spotter_Generator.Models
{
    /// <summary>
    /// Represents a long, imported recording (e.g. a full race session) that
    /// individual message takes can be cut out from. Overlapping usage is
    /// intentionally allowed, since the same phrase may be reused for
    /// multiple messages; <see cref="UsedRegions"/> is purely informational
    /// so the operator can see which parts have already been cut out.
    /// </summary>
    public class SourceRecording
    {
        public required string Id { get; init; }

        /// <summary>
        /// Absolute path to the normalized WAV file stored under the
        /// SourceRecordings folder (converted from the originally imported
        /// WAV/MP3/MP4 file).
        /// </summary>
        public required string FilePath { get; set; }

        /// <summary>
        /// User-friendly display name (defaults to the original file name).
        /// </summary>
        public string Name { get; set; } = string.Empty;

        public double DurationSeconds { get; set; }

        public List<UsedRegion> UsedRegions { get; init; } = new();
    }

    /// <summary>
    /// Marks a time range within a <see cref="SourceRecording"/> that has
    /// already been cut out and assigned to a message. Purely informational;
    /// regions may overlap since the same audio can be reused for multiple
    /// messages.
    /// </summary>
    public class UsedRegion
    {
        public required string MessageId { get; init; }

        public string MessageText { get; init; } = string.Empty;

        public double StartSeconds { get; init; }

        public double EndSeconds { get; init; }
    }
}
