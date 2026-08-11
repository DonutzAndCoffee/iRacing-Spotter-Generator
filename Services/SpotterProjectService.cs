using System.IO;
using System.Text.Json;
using iRacing_Spotter_Generator.Models;

namespace iRacing_Spotter_Generator.Services
{
    /// <summary>
    /// Saves and loads a <see cref="SpotterProject"/> to/from a JSON project file,
    /// so a work-in-progress spotter pack (all messages plus destination
    /// settings) can be persisted and continued later.
    /// </summary>
    public static class SpotterProjectService
    {
        public const string FileFilter = "iRacing Spotter Project (*.spgproj)|*.spgproj|All files (*.*)|*.*";
        public const string DefaultExtension = ".spgproj";

        public static void Save(string filePath, SpotterProject project)
        {
            var json = JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }

        public static SpotterProject Load(string filePath)
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<SpotterProject>(json)
                ?? throw new InvalidOperationException("The project file could not be read.");
        }
    }
}
