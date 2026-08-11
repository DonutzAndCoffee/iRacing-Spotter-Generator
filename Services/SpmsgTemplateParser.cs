using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using iRacing_Spotter_Generator.Models;

namespace iRacing_Spotter_Generator.Services
{
    /// <summary>
    /// Parses and serializes the iRacing spmsg.ini sample template format:
    /// MSGID, WAVFILE.WAV, "text"
    /// Comments start with ';' and blank lines are preserved as-is when re-serializing
    /// the full file, but only data rows are exposed as editable SpotterMessage items.
    /// </summary>
    public static partial class SpmsgTemplateParser
    {
        private static readonly Regex LineRegex = new(
            @"^\s*(?<id>[A-Za-z0-9_]+)\s*,\s*(?<wav>[^,]+?)\s*,\s*(?:""(?<text>(?:[^""]|"""""")*)""|(?<textNull>NULL))\s*$",
            RegexOptions.Compiled);

        public static List<SpotterMessage> Parse(string filePath)
        {
            var lines = File.ReadAllLines(filePath);
            return ParseLines(lines);
        }

        public static List<SpotterMessage> ParseLines(IEnumerable<string> lines)
        {
            var messages = new List<SpotterMessage>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (trimmed.Length == 0 || trimmed.StartsWith(';'))
                {
                    continue;
                }

                var match = LineRegex.Match(trimmed);
                if (!match.Success)
                {
                    continue;
                }

                var wav = match.Groups["wav"].Value.Trim();
                var isNull = match.Groups["textNull"].Success;
                var text = isNull ? string.Empty : match.Groups["text"].Value.Replace("\"\"", "\"");

                messages.Add(new SpotterMessage
                {
                    MsgId = match.Groups["id"].Value,
                    WavFileName = wav,
                    Text = text,
                    Enabled = !isNull && !string.Equals(wav, "NULL", StringComparison.OrdinalIgnoreCase),
                    RawLine = line
                });
            }

            return messages;
        }

        /// <summary>
        /// Serializes the given messages back into spmsg.ini file content.
        /// Messages are grouped by MsgId: enabled rows with text are written as-is,
        /// while a MsgId with no enabled/texted row is written once as NULL, NULL
        /// (iRacing requires every MsgId to be present, even if disabled).
        /// Any additional <paramref name="requiredMsgIds"/> not covered by
        /// <paramref name="messages"/> at all (e.g. because all their rows were
        /// removed) are also appended as NULL, NULL.
        /// </summary>
        public static string Serialize(IEnumerable<SpotterMessage> messages, IEnumerable<string>? requiredMsgIds = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("spmsg02");
            sb.AppendLine();

            var coveredMsgIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in messages.GroupBy(m => m.MsgId, StringComparer.OrdinalIgnoreCase))
            {
                var enabledLines = group.Where(m => m.Enabled && !string.IsNullOrWhiteSpace(m.Text)).ToList();

                if (enabledLines.Count > 0)
                {
                    foreach (var message in enabledLines)
                    {
                        var escapedText = message.Text.Replace("\"", "\"\"");
                        var wavName = string.IsNullOrWhiteSpace(message.WavFileName)
                            ? message.MsgId + ".wav"
                            : message.WavFileName;

                        sb.AppendLine($"{message.MsgId}, {wavName}, \"{escapedText}\"");
                    }
                }
                else
                {
                    sb.AppendLine($"{group.Key}, NULL, NULL");
                }

                coveredMsgIds.Add(group.Key);
            }

            if (requiredMsgIds is not null)
            {
                foreach (var msgId in requiredMsgIds)
                {
                    if (coveredMsgIds.Add(msgId))
                    {
                        sb.AppendLine($"{msgId}, NULL, NULL");
                    }
                }
            }

            return sb.ToString();
        }
    }
}
