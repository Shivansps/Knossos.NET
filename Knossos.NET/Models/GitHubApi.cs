using Knossos.NET.Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace Knossos.NET.Models
{
    public static class GitHubApi
    {
        /// <summary>
        /// Gets the latest applicable release from GitHub.
        /// - Modern platforms (Win10+ / macOS 12.2+): uses /releases/latest (current behaviour).
        /// - Legacy platforms: scans /releases, keeps only v1.3.x tags, returns the newest one.
        /// </summary>
        /// <returns>GitHubRelease or null if the API call failed.</returns>
        public static async Task<GitHubRelease?> GetLastRelease()
        {
            if (KnUtils.IsModernOS())
            {
                return await GetLatestRelease();
            }
            else
            {
                return await GetLatestLegacyRelease();
            }
        }

        /// <summary>
        /// Calls /releases/latest — for modern platforms.
        /// </summary>
        private static async Task<GitHubRelease?> GetLatestRelease()
        {
            try
            {
                var client = KnUtils.GetHttpClient();
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("product", "1"));
                using var response = await client.GetAsync(Knossos.GitHubUpdateRepoURL + "/releases/latest");
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<GitHubRelease>(json);
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "GitHubApi.GetLatestRelease()", ex);
                return null;
            }
        }

        /// <summary>
        /// Scans /releases pages looking for v1.3.x tags and returns the newest one.
        /// Stops paginating early once tags leave the 1.3.x range (assumes releases
        /// are returned newest-first by the API).
        /// </summary>
        private static async Task<GitHubRelease?> GetLatestLegacyRelease()
        {
            try
            {
                var client = KnUtils.GetHttpClient();
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("product", "1"));

                var candidates = new List<GitHubRelease>();
                int page = 1;
                const int perPage = 30; // GitHub default; max is 100

                while (true)
                {
                    var url = $"{Knossos.GitHubUpdateRepoURL}/releases?per_page={perPage}&page={page}";
                    using var response = await client.GetAsync(url);
                    var json = await response.Content.ReadAsStringAsync();
                    var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json);

                    if (releases == null || releases.Count == 0)
                        break;

                    foreach (var release in releases)
                    {
                        if (IsLegacyTag(release.tag_name))
                        {
                            candidates.Add(release);
                        }
                    }

                    // If the oldest release on this page is already below 1.3.7, no need to go further
                    var lastTag = releases.Last().tag_name;
                    if (lastTag != null && IsBelowLegacyMinor(lastTag))
                        break;

                    if (releases.Count < perPage)
                        break; // last page

                    page++;
                }

                // Return the candidate with the highest semantic version
                return candidates
                    .Where(r => r.tag_name != null)
                    .MaxBy(r => new SemanticVersion(NormalizeTag(r.tag_name!)));
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "GitHubApi.GetLatestLegacyRelease()", ex);
                return null;
            }
        }

        /// <summary>
        /// Returns true if the tag is a v1.3.x release (e.g. "v1.3.7", "v1.3.10-rc1").
        /// </summary>
        private static bool IsLegacyTag(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return false;
            var version = new SemanticVersion(NormalizeTag(tag));
            // Check major == 1 and minor == 3 by comparing against sentinels
            return SemanticVersion.Compare(NormalizeTag(tag), "1.3.0") >= 0 &&
                   SemanticVersion.Compare(NormalizeTag(tag), "1.4.0") < 0;
        }

        /// <summary>
        /// Returns true if the tag is strictly older than 1.3.7 — used as an early-exit
        /// hint while paginating (GitHub returns releases newest-first).
        /// </summary>
        private static bool IsBelowLegacyMinor(string tag)
        {
            return SemanticVersion.Compare(NormalizeTag(tag), "1.3.7") < 0;
        }

        /// <summary>Strips the leading "v" that GitHub tags typically have.</summary>
        private static string NormalizeTag(string tag) =>
            tag.ToLower().Replace("v", "").Trim();
    }


    public class GitHubRelease
    {
        public string? url { get; set; }
        public string? assets_url { get; set; }
        public string? upload_url { get; set; }
        public string? html_url { get; set; }
        public int id { get; set; }
        public object? author { get; set; }
        public string? node_id { get; set; }
        public string? tag_name { get; set; }
        public string? target_commitish { get; set; }
        public string? name { get; set; }
        public bool draft { get; set; }
        public bool prerelease { get; set; }
        public string? created_at { get; set; }
        public string? published_at { get; set; }
        public GitHubReleaseAsset[]? assets { get; set; }
        public string? tarball_url { get; set; }
        public string? zipball_url { get; set; }
        public string? body { get; set; }
    }

    public class GitHubReleaseAsset
    {
        public string? url { get; set; }
        public int id { get; set; }
        public string? node_id { get; set; }
        public string? name { get; set; }
        public object? label { get; set; }
        public object? uploader { get; set; }
        public string? content_type { get; set; }
        public string? state { get; set; }
        public int size { get; set; }
        public int download_count { get; set; }
        public string? created_at { get; set; }
        public string? updated_at { get; set; }
        public string? browser_download_url { get; set; }
    }

}
