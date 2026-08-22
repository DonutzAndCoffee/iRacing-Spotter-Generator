using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace iRacing_Spotter_Generator.Services
{
    /// <summary>
    /// Result of a successful update check, describing the newer release
    /// found on GitHub.
    /// </summary>
    public record UpdateCheckResult(string Tag, string Url);

    /// <summary>
    /// Checks the GitHub releases API of the project's repository for a
    /// newer release than the one currently running.
    /// </summary>
    public static class UpdateCheckService
    {
        public const string RepositoryUrl = "https://github.com/DonutzAndCoffee/iRacing-Spotter-Generator";
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/DonutzAndCoffee/iRacing-Spotter-Generator/releases/latest";

        /// <summary>
        /// Queries the latest GitHub release and returns update information
        /// if it is newer than <paramref name="currentVersion"/>. Returns
        /// null on any failure (offline, rate limiting, parse errors, etc.)
        /// or when the current version is already up to date.
        /// </summary>
        public static async Task<UpdateCheckResult?> CheckForUpdateAsync(Version currentVersion)
        {
            try
            {
                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(5)
                };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("iRacing-Spotter-Generator-UpdateCheck");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                using var response = await client.GetAsync(LatestReleaseApiUrl).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

                if (!doc.RootElement.TryGetProperty("tag_name", out var tagProperty))
                {
                    return null;
                }

                var tag = tagProperty.GetString();
                if (string.IsNullOrWhiteSpace(tag))
                {
                    return null;
                }

                var versionText = tag.TrimStart('v', 'V');
                if (!Version.TryParse(versionText, out var latestVersion))
                {
                    return null;
                }

                var htmlUrl = doc.RootElement.TryGetProperty("html_url", out var urlProperty)
                    ? urlProperty.GetString() ?? $"{RepositoryUrl}/releases/latest"
                    : $"{RepositoryUrl}/releases/latest";

                // Compare only Major.Minor.Build, since release tags don't carry a revision component.
                var normalizedCurrent = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(currentVersion.Build, 0));
                var normalizedLatest = new Version(latestVersion.Major, latestVersion.Minor, Math.Max(latestVersion.Build, 0));

                return normalizedLatest > normalizedCurrent ? new UpdateCheckResult(tag, htmlUrl) : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
