namespace iRacing_Spotter_Generator.Models
{
    /// <summary>
    /// A single recorded/trimmed take file plus user-editable metadata
    /// (a friendly name and an optional 1-10 quality rating), so takes can
    /// be told apart and ranked when revisiting a message later.
    /// </summary>
    public class TakeInfo
    {
        public string FilePath { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Optional quality ranking from 1 (worst) to 10 (best). Null means
        /// "not rated yet".
        /// </summary>
        public int? Rating { get; set; }
    }
}
