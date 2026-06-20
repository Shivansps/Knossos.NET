using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VP.NET;

namespace Knossos.NET.Classes
{
    /// <summary>
    /// Reads an .eff animation manifest and (when needed) rewrites its $Type line.
    ///
    /// An .eff is a plain text manifest:
    ///     $Type:   dds        (frame image format: dds / ktx / png / pcx / tga / jpg / ...)
    ///     $Frames: 90         (frame count)
    ///     $FPS:    30
    /// Frames are named "{effBaseName}_{i:D4}.{type}", e.g. Particle_Wave_Blue_1_0000.dds.
    /// Real files use CRLF and may carry trailing spaces, so everything is trimmed when parsed.
    /// </summary>
    public sealed class EffHelper
    {
        public string Type { get; private set; } = "";          // lower, no leading dot
        public int FrameCount { get; private set; }
        public int Fps { get; private set; }
        public string BaseName { get; private set; } = "";       // original case, no extension

        public bool IsDds => Type.Equals("dds", StringComparison.OrdinalIgnoreCase);

        /// <summary>Theoretical frame file names for this eff using the given extension (no dot), in order.</summary>
        public IEnumerable<string> FrameNames(string ext)
        {
            for (int i = 0; i < FrameCount; i++)
                yield return $"{BaseName}_{i:D4}.{ext}";
        }

        // parsing

        public static EffHelper Parse(string text, string effFileName)
        {
            var eff = new EffHelper();
            using var reader = new StringReader(text);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || !line.StartsWith("$")) continue;
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string key = line.Substring(1, colon - 1).Trim();
                string value = line.Substring(colon + 1).Trim();
                switch (key.ToLowerInvariant())
                {
                    case "type":   eff.Type = value.TrimStart('.').ToLowerInvariant(); break;
                    case "frames": int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var f); eff.FrameCount = f; break;
                    case "fps":    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p); eff.Fps = p; break;
                }
            }

            if (string.IsNullOrEmpty(eff.Type))
                throw new InvalidDataException($"'{effFileName}': missing $Type");
            if (eff.FrameCount <= 0)
                throw new InvalidDataException($"'{effFileName}': invalid $Frames ({eff.FrameCount})");

            eff.BaseName = Path.GetFileNameWithoutExtension(effFileName);
            return eff;
        }

        public static async Task<EffHelper> ParseAsync(Stream stream, string effFileName)
        {
            stream.Position = 0;
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: true);
            var text = await reader.ReadToEndAsync().ConfigureAwait(false);
            return Parse(text, effFileName);
        }

        // $Type patching

        /// <summary>
        /// Returns the eff text with only the $Type value rewritten to <paramref name="newType"/>.
        /// $Frames / $FPS / comments / line endings / indentation are preserved.
        /// Returns null if the eff already has that type (idempotent: safe to run on every pass).
        /// </summary>
        public static string? PatchTypeText(string text, string newType)
        {
            string nl = text.Contains("\r\n") ? "\r\n" : (text.Contains("\n") ? "\n" : Environment.NewLine);
            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            bool changed = false;
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (!trimmed.StartsWith("$")) continue;
                int colon = trimmed.IndexOf(':');
                if (colon < 0) continue;
                var key = trimmed.Substring(1, colon - 1).Trim();
                if (!key.Equals("Type", StringComparison.OrdinalIgnoreCase)) continue;

                var current = trimmed.Substring(colon + 1).Trim().TrimStart('.').ToLowerInvariant();
                if (current == newType.ToLowerInvariant()) return null; // already correct

                int indent = lines[i].Length - lines[i].TrimStart().Length;
                lines[i] = lines[i].Substring(0, indent) + "$Type: " + newType;
                changed = true;
                break;
            }
            return changed ? string.Join(nl, lines) : null;
        }

        /// <summary>Patch a loose eff on disk. Returns true if the file was modified.</summary>
        public static async Task<bool> PatchLooseAsync(string effPath, string newType, CancellationToken token = default)
        {
            var original = await File.ReadAllTextAsync(effPath, token).ConfigureAwait(false);
            var patched = PatchTypeText(original, newType);
            if (patched == null) return false;
            await File.WriteAllTextAsync(effPath, patched, new UTF8Encoding(false), token).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Patch an eff stored inside a vp: replaces the node with a patched copy. The caller is
        /// responsible for saving the vp afterwards. Returns true if a replacement was queued.
        /// A unique temp subfolder is used, and the temp file keeps the node's ORIGINAL-CASE name so
        /// VPFile.AddFile (which matches names case-sensitively) replaces the entry instead of duplicating it.
        /// </summary>
        public static async Task<bool> PatchInVpAsync(VPFile effNode, string newType, string workFolder, CancellationToken token = default)
        {
            if (effNode.parent == null) return false;

            using var ms = new MemoryStream();
            await effNode.ReadToStream(ms).ConfigureAwait(false);
            ms.Position = 0;
            string original;
            using (var reader = new StreamReader(ms, Encoding.UTF8, true, 1024, leaveOpen: true))
                original = await reader.ReadToEndAsync().ConfigureAwait(false);

            var patched = PatchTypeText(original, newType);
            if (patched == null) return false;

            var dir = Path.Combine(workFolder, "effpatch_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var tmp = Path.Combine(dir, effNode.info.name);
            await File.WriteAllTextAsync(tmp, patched, new UTF8Encoding(false), token).ConfigureAwait(false);

            effNode.parent.AddFile(new FileInfo(tmp));
            return true;
        }
    }
}
