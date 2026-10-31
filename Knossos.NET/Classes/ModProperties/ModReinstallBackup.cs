using System;
using System.IO;
using System.Linq;

namespace Knossos.NET.Classes
{
    /// <summary>Restores the previous installation unless its replacement completes successfully.</summary>
    public sealed class ModReinstallBackup : IDisposable
    {
        private readonly string library;
        private readonly string target;
        private readonly string backup;
        private bool committed;
        public string BackupPath => backup;
        public ModReinstallBackup(string libraryPath, string expectedPath, string installedPath)
        {
            library = Path.GetFullPath(libraryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            target = Path.GetFullPath(expectedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var installed = Path.GetFullPath(installedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!target.Equals(installed, comparison)) throw new IOException("The installed mod path does not match the requested version.");
            Validate(target);
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new IOException("Cannot reinstall a linked mod directory.");
            backup = target + ".reinstall-" + Guid.NewGuid().ToString("N");
            Validate(backup);
            Directory.Move(target, backup);
            try
            {
                Directory.CreateDirectory(target);
                foreach (var name in new[] { "mod.json", "mod_settings.json" })
                    if (File.Exists(Path.Combine(backup, name))) File.Copy(Path.Combine(backup, name), Path.Combine(target, name));
                var images = Path.Combine(backup, "kn_images");
                if (Directory.Exists(images)) CopyImages(images, Path.Combine(target, "kn_images"));
            }
            catch { Dispose(); throw; }
        }
        private void Validate(string value)
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!Path.GetFullPath(value).StartsWith(library, comparison)) throw new IOException("Reinstall path is outside the library.");
        }
        private static void CopyImages(string source, string destination)
        {
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) return;
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source).Where(f => (File.GetAttributes(f) & FileAttributes.ReparsePoint) == 0))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            foreach (var directory in Directory.EnumerateDirectories(source)) CopyImages(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
        public void Commit()
        {
            committed = true;
            Validate(backup);
            try { Directory.Delete(backup, true); }
            catch (Exception ex) { Log.Add(Log.LogSeverity.Warning, "ModReinstallBackup", "The replacement succeeded; its previous files remain at " + backup + ": " + ex.Message); }
        }
        public void Dispose()
        {
            if (committed || !Directory.Exists(backup)) return;
            Validate(target);
            Validate(backup);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(backup, target);
            committed = true;
        }
    }
}
