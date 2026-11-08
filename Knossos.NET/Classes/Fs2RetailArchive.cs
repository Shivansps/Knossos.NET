using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Knossos.NET.Classes
{
    /// <summary>
    /// Finds retail files at any depth in a SharpCompress archive. Only known file
    /// names are passed to the installer; archive paths and links are never used
    /// as installation destinations.
    /// </summary>
    internal static class Fs2RetailArchive
    {
        internal static HashSet<string> ReadFiles(Stream source, IEnumerable<string> names,
            Action<string, Stream>? extract = null)
        {
            var wanted = names.ToDictionary(name => name, StringComparer.OrdinalIgnoreCase);
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string? Match(IEntry entry)
            {
                if (entry.IsDirectory || entry.LinkTarget != null || entry.Key == null)
                    return null;
                var name = entry.Key.Replace('\\', '/').Split('/').Last();
                return wanted.TryGetValue(name, out var canonical) && found.Add(canonical)
                    ? canonical : null;
            }

            void ReadEntries(IReader reader)
            {
                while (reader.MoveToNextEntry())
                {
                    var name = Match(reader.Entry);
                    if (name != null && extract != null)
                    {
                        using var data = reader.OpenEntryStream();
                        extract(name, data);
                    }
                }
            }

            // 7z requires the Archive API. ZIP/RAR can also list their entries
            // without decompressing the data during source validation.
            var start = source.Position;
            ArchiveFactory.IsArchive(source, out var type);
            source.Position = start;
            if (type == ArchiveType.SevenZip || type == ArchiveType.Zip || type == ArchiveType.Rar)
            {
                using var archive = ArchiveFactory.OpenArchive(source);
                if (extract == null || (!archive.IsSolid && type != ArchiveType.SevenZip))
                {
                    foreach (var entry in archive.Entries)
                    {
                        var name = Match(entry);
                        if (name != null && extract != null)
                        {
                            using var data = entry.OpenEntryStream();
                            extract(name, data);
                        }
                    }
                }
                else
                {
                    // One sequential pass also supports solid RAR and 7z archives.
                    using var reader = archive.ExtractAllEntries();
                    ReadEntries(reader);
                }
            }
            else
            {
                // Auto-detection handles TAR and its compression wrappers, plus
                // the formats exposed only through SharpCompress's Reader API.
                using var reader = ReaderFactory.OpenReader(source);
                ReadEntries(reader);
            }
            return found;
        }
    }
}
