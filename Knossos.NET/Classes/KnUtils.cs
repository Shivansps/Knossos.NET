using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using Knossos.NET.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WindowsShortcutFactory;

namespace Knossos.NET
{
    /// <summary>
    /// Utility class
    /// Only static methods
    /// </summary>
    public static class KnUtils
    {
        private struct LegacySettings
        {
            public string? base_path { get; set; }
        }

        /// <summary>
        /// Static Constructor to generate the httpfactory service
        /// </summary>
        static KnUtils()
        {
            var serviceCollention = new ServiceCollection();
            serviceCollention.AddHttpClient("generic", options =>
            {
                options.Timeout = TimeSpan.FromSeconds(45);
            }).ConfigurePrimaryHttpMessageHandler(x => new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true
            });

            httpClientFactory = serviceCollention.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        }

        private static readonly bool isAppImage = IsLinux && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIMAGE"));
        private static readonly string cpuArch = RuntimeInformation.OSArchitecture.ToString();
        private static readonly bool cpuAVX = Avx.IsSupported;
        private static readonly bool cpuAVX2 = Avx2.IsSupported;
        private static string fsoPrefPath = string.Empty;
        private static IHttpClientFactory httpClientFactory;

        public static bool IsAndroid => OperatingSystem.IsAndroid();
        public static bool IsBrowser => OperatingSystem.IsBrowser();
        public static bool IsWindows => OperatingSystem.IsWindows();
        public static bool IsLinux => OperatingSystem.IsLinux();
        public static bool IsMacOS => OperatingSystem.IsMacOS();

        /// <summary>
        /// <para>Possible Values:</para>
        /// <para>Arm	  //A 32-bit ARM processor architecture.</para>
        /// <para>Armv6 //A 32-bit ARMv6 processor architecture.</para>
        /// <para>Arm64 //A 64-bit ARM processor architecture.</para>
        /// <para>X64   //An Intel-based 64-bit processor architecture.</para>
        /// <para>X86   //An Intel-based 32-bit processor architecture.</para>
        /// <para>RiscV64 //A 64 bits RISC-V processor</para>
        /// <para>https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.architecture?view=net-9.0</para>
        /// </summary>
        public static string CpuArch => cpuArch;
        public static bool CpuAVX => cpuAVX;
        public static bool CpuAVX2 => cpuAVX2;
        public static bool IsAppImage => isAppImage;
        public static string AppImagePath => appImagePath;

        /// <summary>
        /// Keeps a list of remote resources loaded to cache (or checked) during this session
        /// So we don't constantly query these remote URLs for changes
        /// </summary>
        private static List<string> remoteResourceLoadedToCache = new List<string>();

        /// <summary>
        /// Clear list of remote url resources downloaded to cache (or checked) during this session
        /// So they can be checked and redownloaded again without restarting if they are outdated.
        /// </summary>
        public static void ClearDownloadedRemoteResourceList()
        {
            remoteResourceLoadedToCache.Clear();
        }

        /// <summary>
        /// Full path to AppImage file
        /// </summary>
        private static readonly string appImagePath = Environment.GetEnvironmentVariable("APPIMAGE")!;

        /// <summary>
        /// Return fullpath to current Knet application folder 
        /// </summary>
        public static string? KnetFolderPath
        {
            get
            {
                if (IsAppImage)
                {
                    return Path.GetDirectoryName(KnUtils.AppImagePath);
                }
                else if (IsMacOS && WasInstallerUsed())
                {
                    var execFullPath = Environment.ProcessPath;
                    var cutOff = execFullPath!.IndexOf(".app") + 4;
                    var realName = execFullPath![..cutOff];
                    return Path.GetDirectoryName(realName);
                }
                else
                {
                    if(IsAndroid)
                    {
                        return AndroidHelper.GetDefaultKnetDir();
                    }
                    return AppDomain.CurrentDomain.BaseDirectory;
                }
            }
        }

        /// <summary>
        /// The full path to KNET data folder
        /// </summary>
        /// <returns>fullpath as a string</returns>
        public static string GetKnossosDataFolderPath()
        {
            if (!Knossos.inPortableMode)
            {
                if (CustomLauncher.IsCustomMode)
                {
                    //In custom mode store config files inside modid a subfolder
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "KnossosNET", CustomLauncher.ModID!);
                }
                else
                {
                    if(IsAndroid)
                    {
                        return AndroidHelper.GetDefaultKnetDataDir();
                    }
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "KnossosNET");
                }
            }
            else
            {
                return Path.Combine(KnetFolderPath!, "kn_portable", "KnossosNET"); //If inPortableMode = true, KnetFolderPath is not null
            }
        }

        /// <summary>
        /// Gets the full path to FSO data folder
        /// Uses "fsoprefpath" provided that a compatible FSO executable is installed
        /// If an FSO executable is not found this method will try to guess the path to the data folder
        /// In Windows this is correct, but it may not on other OS.
        /// </summary>
        /// <returns>fullpath as string</returns>
        public static string GetFSODataFolderPath()
        {
            var fsoID = "FreeSpaceOpen";
            if(CustomLauncher.IsCustomMode && CustomLauncher.UseCustomFSODataFolder)
            {
                fsoID = CustomLauncher.ModID!;
            }
            if (Knossos.inPortableMode && Knossos.globalSettings.portableFsoPreferences)
            {
                return Path.Combine(KnetFolderPath!, "kn_portable", "HardLightProductions", fsoID); //If inPortableMode = true, KnetFolderPath is not null
            }
            else
            {
                if (!string.IsNullOrEmpty(fsoPrefPath))
                {
                    if (CustomLauncher.IsCustomMode && CustomLauncher.UseCustomFSODataFolder)
                    {
                        return ReplaceLast(fsoPrefPath, "FreeSpaceOpen", fsoID);
                    }
                    else
                    {
                        return fsoPrefPath;
                    }
                }
                else
                {
                    if(KnUtils.IsAndroid)
                    {
                        return AndroidHelper.GetDefaultFSODataDir();
                    }
                    if (KnUtils.IsMacOS)
                    {
                        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "HardLightProductions", fsoID);
                    }
                    if (IsLinux)
                    {
                        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "HardLightProductions", fsoID);
                    }
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "HardLightProductions", fsoID);
                }
            }

        }

        /// <summary>
        /// Replace last ocurrence of a word in a string
        /// </summary>
        /// <param name="source"></param>
        /// <param name="find"></param>
        /// <param name="replace"></param>
        /// <returns></returns>
        public static string ReplaceLast(string source, string find, string replace)
        {
            int index = source.LastIndexOf(find);

            if (index == -1)
                return source;

            return source.Remove(index, find.Length).Insert(index, replace);
        }

        /// <summary>
        /// Saves fsoPrefPath so it can be used by GetFSODataFolderPath()
        /// </summary>
        /// <param name="path"></param>
        public static void SetFSODataFolderPath(string path)
        {
            //FSO responds with this if there is no ini
            if (path != @".\")
                fsoPrefPath = path;
        }

        /// <summary>
        /// Converts a DateTime into a timestamp string
        /// </summary>
        /// <param name="value"></param>
        /// <returns>timestamp string or string empty if fails</returns>
        public static String GetTimestamp(DateTime value)
        {
            try
            {
                return value.ToString("yyyyMMddHHmmssffff");
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtils.GetTimestamp", ex);
                return string.Empty;
            }
        }

        /// <summary>
        /// Try to get library fullpath from old Knossos data folder
        /// </summary>
        /// <returns>Knossos Library path or null if not found</returns>
        public static String? GetBasePathFromKnossosLegacy()
        {
            try
            {
                string jsonPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "knossos", "settings.json");
                if (File.Exists(jsonPath))
                {
                    using FileStream jsonFile = File.OpenRead(jsonPath);
                    var jsonObject = JsonSerializer.Deserialize<LegacySettings>(jsonFile)!;
                    jsonFile.Close();
                    return jsonObject.base_path;
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtils.GetBasePathFromKnossosLegacy", ex);
            }
            return null;
        }

        /// <summary>
        /// Executes a Chmod command in MacOSX and Linux
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="permissions"></param>
        /// <param name="recursive"></param>
        /// <returns>true if successfull, false otherwise</returns>
        public static bool Chmod(string filePath, string permissions = "+x", bool recursive = false)
        {
            if (IsWindows)
                return false;
            string cmd;
            if (recursive)
                cmd = $"chmod -R {permissions} '{filePath}'";
            else
                cmd = $"chmod {permissions} '{filePath}'";

            try
            {
                using (Process proc = Process.Start("/bin/bash", $"-c \"{cmd}\""))
                {
                    proc.WaitForExit();
                    return proc.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Formats a filesize value into a string with the right B/KB/MB/GB/TB suffix
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns>value in a string with a suffix or string.empty if fails</returns>
        public static string FormatBytes(long bytes)
        {
            try
            {
                string[] suffix = { "B", "KB", "MB", "GB", "TB" };
                int i;
                double dblSByte = bytes;
                for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
                {
                    dblSByte = bytes / 1024.0;
                }

                return String.Format("{0:0.##} {1}", dblSByte, suffix[i]);
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtils.FormatBytes()", ex);
                return string.Empty;
            }
        }

        /// <summary>
        /// Return the current OS name in a string format
        /// </summary>
        /// <returns>"Windows", "Linux", "MacOS" or "Unknown"</returns>
        public static string GetOSNameString()
        {
            if (IsWindows)
                return "Windows";
            if (IsLinux)
                return "Linux";
            if (IsMacOS)
                return "MacOS";
            return "Unknown";
        }

        /// <summary>
        /// Open a URL in a external browser
        /// </summary>
        /// <param name="url"></param>
        public static void OpenBrowserURL(string url)
        {
            // Check URL
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                Log.Add(Log.LogSeverity.Warning, "KnUtils.OpenBrowserURL", $"Not opening web browser link due to invalid or unsafe URL: {url}");
                return;
            }

            try
            {
#if ANDROID
                _ = AndroidHelper.OpenUrlAsync(url);
#else
                using (var process = new Process())
                {
                    if (IsWindows)
                    {
                        process.StartInfo.FileName = uri.ToString();
                        process.StartInfo.UseShellExecute = true;
                    }
                    else if (IsLinux)
                    {
                        process.StartInfo.FileName = "xdg-open";
                        process.StartInfo.Arguments = uri.ToString();
                    }
                    else if (IsMacOS)
                    {
                        process.StartInfo.FileName = "open";
                        process.StartInfo.Arguments = uri.ToString();
                    }
                    process.StartInfo.CreateNoWindow = true;
                    process.Start();
                }
#endif
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtils.OpenBrowser()", ex);
            }
        }

        /// <summary>
        /// Opens a folder using the operative system file browser
        /// </summary>
        /// <param name="path"></param>
        public static void OpenFolder(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo()
                {
                    FileName = path,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtils.OpenFolder()", ex);
            }
        }

        /// <summary>
        /// Async directory copy helper method
        /// Support optional recursive copy and progressCallback that informs the name of the current file that is being copied
        /// Supports passing an optional array of extensions to ignore during file copy. 
        /// Files and folders starting with a "." (Unix hidden folder convention) are skipped.
        /// </summary>
        /// <param name="sourceDir"></param>
        /// <param name="destinationDir"></param>
        /// <param name="recursive"></param>
        /// <param name="cancelSource"></param>
        /// <param name="progressCallback"></param>
        /// <param name="ignoreExtensions"></param>
        /// <returns></returns>
        /// <exception cref="DirectoryNotFoundException"></exception>
        /// <exception cref="TaskCanceledException"></exception>
        public static async Task<bool> CopyDirectoryAsync(string sourceDir, string destinationDir, bool recursive, CancellationTokenSource cancelSource,
            Action<string>? progressCallback = null, string[]? ignoreExtensions = null)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    var dir = new DirectoryInfo(sourceDir);

                    if (!dir.Exists)
                        throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

                    var dirs = dir.GetDirectories();

                    Directory.CreateDirectory(destinationDir);

                    foreach (var file in dir.GetFiles())
                    {
                        if (cancelSource.IsCancellationRequested)
                        {
                            throw new TaskCanceledException();
                        }
                        //Skip Unix convention hidden files
                        if (file.Name.StartsWith("."))
                            continue;
                        if (ignoreExtensions == null || !ignoreExtensions.Contains(file.Extension.ToLower()))
                        {
                            var targetFilePath = Path.Combine(destinationDir, file.Name);
                            if (progressCallback != null)
                            {
                                progressCallback(file.Name);
                            }
                            file.CopyTo(targetFilePath);
                        }
                    }

                    if (recursive)
                    {
                        foreach (var subDir in dirs)
                        {
                            if (cancelSource.IsCancellationRequested)
                            {
                                throw new TaskCanceledException();
                            }
                            //Skip Unix convention hidden folder too
                            if (subDir.Name.StartsWith("."))
                                continue;
                            var newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                            await CopyDirectoryAsync(subDir.FullName, newDestinationDir, true, cancelSource, progressCallback, ignoreExtensions);
                        }
                    }

                    Directory.SetCreationTime(destinationDir, Directory.GetCreationTime(sourceDir));
                    Directory.SetLastAccessTime(destinationDir, Directory.GetLastAccessTime(sourceDir));
                    Directory.SetLastWriteTime(destinationDir, Directory.GetLastWriteTime(sourceDir));
                }catch(Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "KnUtils.CopyDirectoryAsync()", ex);
                    return false;
                }
                return true;
            });
        }

        /// <summary>
        /// Basic string encryption
        /// Encodes the string intro a base64 string,
        /// Usefull if you dont want to display or write a string in a readeable format
        /// </summary>
        /// <param name="unencryptedString"></param>
        /// <returns>base64 string or same string as input if fails</returns>
        public static string StringEncryption(string unencryptedString)
        {
            try
            {
                var stringBytes = Encoding.UTF8.GetBytes(unencryptedString);
                return Convert.ToBase64String(stringBytes);
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtils.DIYStringEncryption()", ex);
                return unencryptedString;
            }
        }

        /// <summary>
        /// Basic string Decryption
        /// Decodes a base64 string intro a normal, readable string
        /// </summary>
        /// <param name="encryptedString"></param>
        /// <returns>decoded string or or same as input if fails</returns>
        public static string StringDecryption(string encryptedString)
        {
            try
            {
                var base64Bytes = Convert.FromBase64String(encryptedString);
                return Encoding.UTF8.GetString(base64Bytes);
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtils.DIYStringDecryption()", ex);
                return encryptedString;
            }
        }

        /// <summary>
        /// Basic check to see that the input string is a valid email format
        /// Does not checks if the emails actually works
        /// </summary>
        /// <param name="email"></param>
        /// <returns>true if valid, false otherwise</returns>
        public static bool IsValidEmail(string email)
        {
            var trimmedEmail = email.Trim();

            if (trimmedEmail.EndsWith("."))
            {
                return false;
            }
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == trimmedEmail;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns a complete file hash string in a format compatible with Nebula
        /// (sha256, lowercase)
        /// </summary>
        /// <param name="fullPath"></param>
        /// <returns>hash string or null if failed</returns>
        public static async Task<string?> GetFileHash(string fullPath)
        {
            try
            {
                using (FileStream? file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    using (SHA256 checksum = SHA256.Create())
                    {
                        return BitConverter.ToString(await checksum.ComputeHashAsync(file)).Replace("-", String.Empty).ToLower();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtils.GetFileHash()", ex);
            }
            return null;
        }

        /// <summary>
        /// Extension method to add ForEach to ObservableCollection
        /// Performs the action on each element
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="enumerable"></param>
        /// <param name="action"></param>
        public static void ForEach<T>(this IEnumerable<T> enumerable, Action<T> action)
        {
            foreach (var cur in enumerable)
            {
                action(cur);
            }
        }

        /// <summary>
        /// Splits a cmdline string into individual flag arguments. Splits on a '-' that is
        /// preceded by start-of-string or whitespace, so hyphens inside flag values (e.g. paths)
        /// are preserved.
        /// </summary>
        /// <param name="cmdline"></param>
        /// <returns>array of flag arguments (without the leading '-'), or null if input is null</returns>
        public static string[]? SplitCmdLineFlags(string? cmdline)
        {
            if (cmdline == null) return null;
            return Regex.Split(cmdline, @"(?<=^|\s)-");
        }

        /// <summary>
        /// Adds arguments to a cmdline string, only if they arent already present
        /// </summary>
        /// <param name="cmdline"></param>
        /// <param name="args"></param>
        /// <returns>cmdline string</returns>
        public static string CmdLineBuilder(string cmdline, string[]? args)
        {
            try
            {
                if (args != null && args.Any())
                {
                    var addedArgs = new List<string>();
                    foreach (var arg in SplitCmdLineFlags(cmdline.ToLower()) ?? Array.Empty<string>())
                    {
                        addedArgs.Add(arg.Split(' ')[0].Trim());
                    }
                    foreach (var arg in args)
                    {
                        var argName = arg.Trim().Split(' ')[0];
                        if (!addedArgs.Contains(argName.ToLower()))
                        {
                            if (arg.Trim().Length > 0)
                            {
                                cmdline += " -" + arg.Trim();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtils.CmdLineBuilder()", ex);
            }
            return cmdline;
        }

        /// <summary>
        /// Returns true if candidatePath resolves to a location within basePath.
        /// Rejects absolute paths, null bytes, and path traversal sequences such as "..".
        /// </summary>
        public static bool IsSubPath(string basePath, string candidatePath)
        {
            if (string.IsNullOrEmpty(candidatePath))
                return true;
            if (Path.IsPathRooted(candidatePath))
                return false;
            try
            {
                var fullBase = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var fullTarget = Path.GetFullPath(Path.Combine(basePath, candidatePath));
                //Windows is uniformly case-insensitive; Linux/macOS filesystems CAN be case-sensitive
                //(always on Linux, optional on APFS), so use Ordinal there to avoid false-positives on
                //traversal attempts that exploit case-only differences with sibling directories.
                var comparison = IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                return fullTarget.StartsWith(fullBase, comparison);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// True if a string is safe to use as a single filesystem path component (no separators,
        /// no traversal sequences, no null bytes, no drive letters, no NTFS-stripped trailing
        /// whitespace or dots, not "." or ".."). Used to validate JSON-supplied fields like
        /// mod.id / mod.version that get concatenated into install paths.
        /// </summary>
        public static bool IsSafePathComponent(string? component)
        {
            if (string.IsNullOrEmpty(component))
                return false;
            if (component == "." || component == "..")
                return false;
            if (component.Contains('\0'))
                return false;
            if (component.Contains('/') || component.Contains('\\'))
                return false;
            if (component.Length >= 2 && component[1] == ':')
                return false;
            //NTFS silently strips trailing dots/spaces — reject so "..  " or ".." don't round-trip.
            if (component.Trim() != component)
                return false;
            if (component.EndsWith('.'))
                return false;
            return true;
        }

        /// <summary>
        /// Gets the complete size of all files in a folder and subdirectories in bytes
        /// </summary>
        /// <param name="folderPath"></param>
        /// <param name="recursive"></param>
        /// <returns>size in bytes or 0 if failed</returns>
        public static async Task<long> GetSizeOfFolderInBytes(string folderPath, bool recursive = true)
        {
            return await Task<long>.Run(() =>
            {
                try
                {
                    if (Directory.Exists(folderPath))
                    {
                        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                        return Directory.EnumerateFiles(folderPath, "*", searchOption).Sum(fileInfo => new FileInfo(fileInfo).Length);
                    }
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Warning, "KnUtils.GetSizeOfFolderInBytes", ex);
                }
                return 0;
            });
        }

        /// <summary>
        /// Gets the fullpath to image storage cache
        /// </summary>
        /// <returns></returns>
        public static string GetCachePath()
        {
            try
            {
                return Path.Combine(GetKnossosDataFolderPath(), "cache");
            }
            catch { return string.Empty; }
        }

        /// <summary>
        /// Downloads a file from a URL, stores it in cache and serves the filestream
        /// If the file is already in cache, a check is done to make sure it has no changed.
        /// If it has changed it is re-downloaded
        /// </summary>
        /// <param name="resourceURL"></param>
        /// <returns>Cached filestream or null if failed</returns>
        public static async Task<FileStream?> GetRemoteResourceStream(string resourceURL)
        {
            try
            {
                var localFile = await GetRemoteResource(resourceURL);
                if (localFile != null)
                {
                    var fileStream = new FileStream(localFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (fileStream.Length == 0)
                    { 
                        fileStream.Dispose();
                        return null;
                    }
                    return fileStream;
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtils.GetRemoteResourceStream()", ex);
            }
            return null;
        }

        /// <summary>
        /// Downloads a file from a URL, stores it in cache and returns the local path
        /// If the file is already in cache, a check is done to make sure it has no changed.
        /// If it has changed it is updated
        /// </summary>
        /// <param name="resourceURL"></param>
        /// <param name="attempt"></param>
        /// <returns>string path or null</returns>
        public static async Task<string?> GetRemoteResource(string resourceURL, int attempt = 1)
        {
            string fileInCachePath = string.Empty;
            bool cacheFileIsValid = false;
            try
            {
                Directory.CreateDirectory(GetCachePath()); //make sure the cache dir exists
                var fileName = Path.GetFileName(resourceURL);
                fileInCachePath = Path.Combine(GetCachePath(), fileName);
                var fileInCacheEtagPath = fileInCachePath + ".etag";
                string? remoteEtag = null;
                bool cacheFileExists = File.Exists(fileInCachePath);
                bool cachedOrCheckedDuringThisSession = remoteResourceLoadedToCache.Contains(resourceURL);
                Uri uri = new Uri(resourceURL);
                bool isNebulaFile = Nebula.nebulaMirrors.Contains(uri.Host.ToLower());
                cacheFileIsValid = cacheFileExists && new FileInfo(fileInCachePath).Length > 0 ? true : false;

                //file exists in cache? check it
                if (cacheFileIsValid && cacheFileExists)
                {
                    bool cacheFileEtagExists = File.Exists(fileInCacheEtagPath);
                    if (isNebulaFile || cachedOrCheckedDuringThisSession)
                    {
                        //This is a nebula file, nebula files are stored by their checksum so they never update
                        //Or we already downloaded or checked this file during this session, lets not do it again
                        return fileInCachePath;
                    }
                    else if (cacheFileEtagExists)
                    {
                        //etag info exist, check it
                        var cachedEtag = await File.ReadAllTextAsync(fileInCacheEtagPath);
                        remoteEtag = await GetUrlFileEtag(resourceURL);
                        if (cachedEtag != null && cachedEtag == remoteEtag)
                        {
                            //cache is up to date
                            remoteResourceLoadedToCache.Add(resourceURL);
                            return fileInCachePath;
                        }
                        else
                        {
                            Log.Add(Log.LogSeverity.Information, "KnUtils.GetRemoteResource()", "File: "+ fileName + " from cache is outdated, re-download. Cache etag: " + cachedEtag + " Remove etag: " + remoteEtag);
                        }
                    }
                    //not etag info or it has changed, re-download
                }

                Log.Add(Log.LogSeverity.Information, "KnUtils.GetRemoteResource()", "Downloading: " + resourceURL + " to local cache.");
                //download to cache
                using (var imageStream = await GetHttpClient().GetStreamAsync(resourceURL))
                {
                    using (var fileStream = new FileStream(fileInCachePath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
                    {
                        await imageStream.CopyToAsync(fileStream);
                    }
                }
                //save etag
                if (!isNebulaFile)
                {
                    try
                    {
                        if (remoteEtag == null)
                        {
                            remoteEtag = await GetUrlFileEtag(resourceURL);
                        }
                        if (remoteEtag != null)
                        {
                            File.WriteAllText(fileInCacheEtagPath, remoteEtag, Encoding.UTF8);
                        }
                        else
                        {
                            Log.Add(Log.LogSeverity.Error, "KnUtils.GetRemoteResource()", "Could not save etag information for file " + resourceURL + " remoteEtag value was null.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Add(Log.LogSeverity.Error, "KnUtils.GetRemoteResource()", ex);
                    }
                }
                remoteResourceLoadedToCache.Add(resourceURL);
                return fileInCachePath;
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtils.GetImagePath()", ex);
                if (attempt < 3)
                {
                    await Task.Delay(1000);
                    return await GetRemoteResource(resourceURL, attempt + 1);
                }
                else
                {
                    //If the download somehow fails, but we have a valid local version of this file, pass it, no matter if it is outdated
                    if (cacheFileIsValid)
                    {
                        return fileInCachePath;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Reads etag data from a url file
        /// </summary>
        /// <returns>etag string or null</returns>
        public static async Task<string?> GetUrlFileEtag(string url)
        {
            try
            {
                string? newEtag = null;
                Log.Add(Log.LogSeverity.Information, "KnUtils.GetUrlFileEtag()", "Getting " + url + " etag.");

                using var result = await KnUtils.GetHttpClient().GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                newEtag = result.Headers?.ETag?.ToString().Replace("\"", "");
                try
                {
                    //workaround because it was not always working on some urls
                    if (newEtag == null && result.Headers != null)
                    {
                        var etagHeader = result.Headers.FirstOrDefault(x => x.Key != null && x.Key.ToLower() == "etag");
                        newEtag = etagHeader.Value.FirstOrDefault();
                    }
                }
                catch { }
                Log.Add(Log.LogSeverity.Information, "KnUtils.GetUrlFileEtag()", Path.GetFileName(url) + " etag: " + newEtag);
                return newEtag;
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtils.GetUrlFileEtag()", ex);
                return null;
            }
        }

        /// <summary>
        /// Check FreeSpace available on the disk/partion of path
        /// </summary>
        /// <param name="path"></param>
        /// <returns>Size in bytes</returns>
        public static long CheckDiskSpace(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                var isDirectory = (fi.Attributes & FileAttributes.Directory) == FileAttributes.Directory;

                var directoryPath = isDirectory ? fi.FullName : fi.Directory?.FullName;
                if (directoryPath != null)
                {
                    var drive = new DriveInfo(directoryPath);
                    if (drive.IsReady)
                    {
                        return drive.AvailableFreeSpace;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtils.CheckDiskSpace", ex);
            }
            return 0;
        }

        /// <summary>
        /// Gets an instance of HttpClient from the IHttpClientFactory
        /// Dispose() should do nothing here, so no need to dispose them
        /// </summary>
        /// <returns>HttpClient</returns>
        public static HttpClient GetHttpClient()
        {
            return httpClientFactory.CreateClient("generic");
        }

        /// <summary>
        /// Decompress a Zip, 7z, or tar.gz file to a folder
        /// If not Decompressor method is specified the one saved in the global settings will be used
        /// Decompression Method:
        /// Auto(0) = First it tries with sharpcompress, and if it fails it then uses the SevepZip console utility
        /// Sharpcompress(1) = Decompress only using Sharpcompress
        /// SevenZip(2) = Decompress only using SevenZip cmdline utility
        /// </summary>
        /// <param name="compressedFilePath"></param>
        /// <param name="destFolderPath"></param>
        /// <param name="cancellationTokenSource"></param>
        /// <param name="extractFullPath"></param>
        /// <param name="progressCallback"></param>
        /// <param name="decompressor"></param>
        /// <returns> true if decompression was successfull, false otherwise</returns>
        public async static Task<bool> DecompressFile(string compressedFilePath, string destFolderPath, CancellationTokenSource? cancellationTokenSource, bool extractFullPath = true, Action<int>? progressCallback = null, Decompressor? decompressor = null)
        {
            if (!File.Exists(compressedFilePath))
            {
                Log.Add(Log.LogSeverity.Error, "KnUtil.DecompressFile()", "File " + compressedFilePath + " does not exist!");
                return false;
            }
            if (decompressor == null)
            {
                decompressor = Knossos.globalSettings.decompressor;
            }

            return await Task.Run(async () =>
            {
                try
                {
                    if (cancellationTokenSource == null)
                        cancellationTokenSource = new CancellationTokenSource();

                    progressCallback?.Invoke(0);

                    bool result = false;

                    switch (decompressor)
                    {
                        case Decompressor.Auto:
                            try
                            {
                                result = DecompressFileSharpCompress(compressedFilePath, destFolderPath, cancellationTokenSource, extractFullPath, progressCallback);
                            }catch (Exception ex) 
                            {
                                Log.Add(Log.LogSeverity.Warning, "KnUtils.DecompressFile()", "Sharpcompress triggered an exception: " + ex.Message + ". Using SevenZip console utility instead.");
                            }
                            if (!result)
                            {
                                result = await DecompressFileSevenZip(compressedFilePath, destFolderPath, cancellationTokenSource, extractFullPath, progressCallback).ConfigureAwait(false);
                            }
                            break;
                        case Decompressor.SharpCompress:
                            result = DecompressFileSharpCompress(compressedFilePath, destFolderPath, cancellationTokenSource, extractFullPath, progressCallback);
                            break;
                        case Decompressor.SevenZip:
                            result = await DecompressFileSevenZip(compressedFilePath, destFolderPath, cancellationTokenSource, extractFullPath, progressCallback).ConfigureAwait(false);
                            break;
                    }

                    if (!result)
                        cancellationTokenSource?.Cancel();
                    else
                        progressCallback?.Invoke(100);

                    return result;
                }
                catch(Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "KnUtils.DecompressFile()", ex);
                    return false; 
                }
            });
        }

        /// <summary>
        /// Decompress a Zip, 7z, or tar.gz file using SevenZip cmdline utility
        /// </summary>
        /// <param name="compressedFilePath"></param>
        /// <param name="destFolderPath"></param>
        /// <param name="cancellationTokenSource"></param>
        /// <param name="extractFullPath"></param>
        /// <param name="progressCallback"></param>
        /// <returns> true if decompression was successfull, false otherwise</returns>
        private async static Task<bool> DecompressFileSevenZip(string compressedFilePath, string destFolderPath, CancellationTokenSource cancellationTokenSource, bool extractFullPath = true, Action<int>? progressCallback = null)
        {
            try
            {
                using (var sevenZip = new SevenZipConsoleWrapper(progressCallback, cancellationTokenSource))
                {
                    return await sevenZip.DecompressFile(compressedFilePath, destFolderPath, extractFullPath);
                }
            }
            catch (TaskCanceledException)
            {
                Log.Add(Log.LogSeverity.Warning, "KnUtil.DecompressFileSevenZip()", "Decompression of file " + compressedFilePath + " was cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtil.DecompressFileSevenZip()", ex);
                return false;
            }
        }

        /// <summary>
        /// Decompress a Zip, 7z, or tar.gz file using SharpCompress lib
        /// </summary>
        /// <param name="compressedFilePath"></param>
        /// <param name="destFolderPath"></param>
        /// <param name="cancellationTokenSource"></param>
        /// <param name="extractFullPath"></param>
        /// <param name="progressCallback"></param>
        /// <returns> true if decompression was successfull, false otherwise</returns>
        private static bool DecompressFileSharpCompress(string compressedFilePath, string destFolderPath, CancellationTokenSource cancellationTokenSource, bool extractFullPath = true, Action<int>? progressCallback = null)
        {
            try
            {
                using (var fileStream = new ProgressStream(File.Open(compressedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read), progressCallback))
                {
                    //tar.gz
                    if (compressedFilePath!.ToLower().Contains(".tar") || compressedFilePath.ToLower().Contains(".gz"))
                    {
                        using (var reader = ReaderFactory.OpenReader(fileStream))
                        {
                            while (reader.MoveToNextEntry())
                            {
                                if (!reader.Entry.IsDirectory)
                                {
                                    var entryKey = reader.Entry.Key?.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
                                    if (entryKey != null && extractFullPath && !IsSubPath(destFolderPath, entryKey))
                                    {
                                        Log.Add(Log.LogSeverity.Warning, "KnUtils.DecompressFileSharpCompress()", $"Skipping potentially dangerous archive entry: {reader.Entry.Key}");
                                        continue;
                                    }

                                    if (reader.Entry.Key == null)
                                    {
                                        continue;
                                    }

                                    var destinationPath = Path.Combine(destFolderPath, reader.Entry.Key);

                                    // Detect SymbLink
                                    if (reader.Entry.LinkTarget != null)
                                    {
                                        try
                                        {
                                            var resolvedTarget = Path.IsPathRooted(reader.Entry.LinkTarget)
                                                ? reader.Entry.LinkTarget
                                                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(destinationPath)!, reader.Entry.LinkTarget));
                                            if (!IsSubPath(destFolderPath, Path.GetRelativePath(destFolderPath, resolvedTarget)))
                                            {
                                                Log.Add(Log.LogSeverity.Warning, "KnUtils.DecompressFileSharpCompress()", $"Skipping symlink escaping destination: {destinationPath} -> {reader.Entry.LinkTarget}");
                                                continue;
                                            }

                                            // Make sure destination parent dir exist
                                            var symlinkParentDir = Path.GetDirectoryName(destinationPath);
                                            if (symlinkParentDir != null && !Directory.Exists(symlinkParentDir))
                                                Directory.CreateDirectory(symlinkParentDir);

                                            File.CreateSymbolicLink(destinationPath, reader.Entry.LinkTarget);
                                        }
                                        catch (Exception ex)
                                        {
                                            Log.Add(Log.LogSeverity.Error, "KnUtils.DecompressFileSharpCompress()", ex);
                                        }
                                    }
                                    else
                                    {
                                        reader.WriteEntryToFile(destinationPath, new ExtractionOptions
                                        {
                                            ExtractFullPath = extractFullPath,
                                            Overwrite = true
                                        });
                                    }
                                }
                                if (cancellationTokenSource!.IsCancellationRequested)
                                {
                                    throw new TaskCanceledException();
                                }
                            }
                        }
                    }
                    else
                    {
                        //zip, 7z
                        using (var archive = ArchiveFactory.OpenArchive(fileStream))
                        {
                            var reader = archive.ExtractAllEntries();
                            while (reader.MoveToNextEntry())
                            {
                                if (!reader.Entry.IsDirectory)
                                {
                                    var entryKey = reader.Entry.Key?.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
                                    if (entryKey != null && extractFullPath && !IsSubPath(destFolderPath, entryKey))
                                    {
                                        Log.Add(Log.LogSeverity.Warning, "KnUtils.DecompressFileSharpCompress()", $"Skipping potentially dangerous archive entry: {reader.Entry.Key}");
                                        continue;
                                    }
                                    reader.WriteEntryToDirectory(destFolderPath, new ExtractionOptions() { ExtractFullPath = extractFullPath, Overwrite = true });
                                }
                                if (cancellationTokenSource!.IsCancellationRequested)
                                {
                                    throw new TaskCanceledException();
                                }
                            }
                        }
                    }
                    fileStream.Close();
                    return true;
                }
            }
            catch (TaskCanceledException)
            {
                Log.Add(Log.LogSeverity.Warning, "KnUtil.DecompressFileSharpCompress()", "Decompression of file " + compressedFilePath + " was cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtil.DecompressFileSharpCompress()", ex);
                return false;
            }
        }

        /// <summary>
        /// Get a color by resource key specified on AppStyles.axaml
        /// </summary>
        /// <param name="colorKeyName"></param>
        /// <returns>Color IBrush or transparent if error</returns>
        public static IBrush GetResourceColor(string colorKeyName)
        {
            try
            {
                var e = Application.Current!.TryGetResource(colorKeyName, Application.Current.ActualThemeVariant, out var color);
                return (IBrush)color!;
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtils.GetColor()", ex);
                return Brushes.Transparent;
            }
        }

        /// <summary>
        /// Detects whether or not the app used an installer or not
        /// </summary>
        /// <returns>true if an installer was used, false otherwise</returns>
        public static bool WasInstallerUsed()
        {
            try
            {
                // NOTE: Using "OperatingSystem" here because it hides the api warning on non-windows
                if (OperatingSystem.IsWindows())
                {
                    // The installer writes the install path to the registry. So check that, and if the
                    // value matches the directory of the currently running knet instance then it's a
                    // safe bet that the installer was used
                    var key = Registry.CurrentUser.OpenSubKey(@"Software\KnossosNET\Knossos.NET");
                    if (key?.GetValue("InstallPath") is string installPath)
                    {
                        return installPath == Path.GetDirectoryName(Environment.ProcessPath);
                    }
                }
                else if (IsMacOS)
                {
                    return Environment.ProcessPath!.Contains(".app/Contents/");
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtils.WasInstallerUsed()", ex);
            }

            return false;
        }

        /// <summary>
        /// Try to get an Enviroment Variable, checks process, user and machine global levels
        /// </summary>
        /// <param name="variableName"></param>
        /// <returns>string value or null if not found or exception</returns>
        public static string? GetEnvironmentVariable(string variableName)
        {
            //Check all 3 targets
            try
            {
                string? returnValue = Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.Process);

                if (returnValue != null)
                {
                    return returnValue;
                }
                else
                {
                    returnValue = Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.User);

                    if (returnValue != null)
                    {
                        return returnValue;
                    }
                    else
                    {
                        return Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.Machine);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtils.GetEnvironmentVariable()", ex);
            }
            return null;
        }

        /// <summary>
        /// Sets an Enviroment Variable
        /// Default target is process level, optional user and machine(global) levels
        /// Note: User and machine levels only works on Windows.
        /// </summary>
        /// <param name="variableName"></param>
        /// <param name="value"></param>
        /// <param name="target"></param>
        /// <returns>true if successful or false otherwise</returns>
        public static bool SetEnvironmentVariable(string variableName, string? value, EnvironmentVariableTarget target = EnvironmentVariableTarget.Process)
        {
            try
            {
                Environment.SetEnvironmentVariable(variableName, value, target);
                return true;
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtils.SetEnvironmentVariable()", ex);
            }
            return false;
        }

        /// <summary>
        /// Escapes underscores so that they will show in UI controls
        /// </summary>
        /// <param name="text"></param>
        /// <returns>string with "text" underscores escaped</returns>
        public static string EscapeUnderscores(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Replace("_", "__");
        }

        /// <summary>
        /// Gets rid of 'A', 'An', and 'The' at the beginning of a string, case-insensitively
        /// </summary>
        /// <param name="title">A string</param>
        /// <returns>The string with any articles removed from the beginning</returns>
        public static string RemoveArticles(string title)
        {
            var match = Regex.Match(title, "((A)|(An)|(The))\\s+", RegexOptions.IgnoreCase);
            if (match.Index == 0 && match.Length > 0)
                return title.Substring(match.Length);
            else
                return title;
        }

        /// <summary>
        /// Checks if a file is currently in use
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns>true or false</returns>
        public static bool IsFileInUse(string filePath)
        {
            try
            {
                using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    stream.Close();
                }
            }
            catch (IOException)
            {
                return true;
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtils.IsFileInUse()", ex);
            }
            return false;
        }

        /// <summary>
        /// Deletes a file checking if it exists first and then waits for the file to be closed. 
        /// </summary>
        public static async Task DeleteFileSafe(string filePath, CancellationTokenSource? cancellationToken = null)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    while (IsFileInUse(filePath))
                    {
                        Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.PrepareModPkg()", "Waiting for file to be closed to delete it: " + filePath);
                        await Task.Delay(100);
                        if (cancellationToken != null && cancellationToken.IsCancellationRequested)
                            throw new TaskCanceledException();
                    }
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtils.DeleteFileSafe()", ex);
            }
        }

        /// <summary>
        /// Creates a shortcut into the user desktop
        /// optional diferent icon path, if null is passed the destfilepath icon will be used
        /// Only Windows
        /// </summary>
        /// <param name="shortcutName"></param>
        /// <param name="destFileFullPath"></param>
        /// <param name="iconFilePath"></param>
        public static void CreateDesktopShortcut(string shortcutName, string destFileFullPath, string arguments = "", string? iconFilePath = null)
        {
            try
            {
                if (IsWindows)
                {
                    using var shortcut = new WindowsShortcut
                    {
                        Path = @destFileFullPath,
                        Description = "KnossosNET quicklaunch to " + shortcutName,
                        IconLocation = iconFilePath == null ? destFileFullPath : iconFilePath,
                        Arguments = arguments
                    };

                    shortcut.Save(@Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) + Path.DirectorySeparatorChar + shortcutName + ".lnk");
                }
            }catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "KnUtils.CreateDesktopShortcut()", ex);
            }
        }

        /// <summary>
        /// Resolves TopLevel Window or View, usually to load a filepicker
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static TopLevel GetTopLevel()
        {
            if(MainWindow.instance != null)
                return MainWindow.instance;
            if (MainView.instance != null)
                return TopLevel.GetTopLevel(MainView.instance)!;
            throw new InvalidOperationException("Unable to resolve TopLevel/owner.");
        }

        /// <summary>
        /// Opens any file on the right application in the operative system
        /// mimetype is only used on Android
        /// </summary>
        /// <param name="fullpath"></param>
        /// <param name="mimetype"></param>
        public static void OpenFileInOS(string fullpath, string mimetype = "text/plain")
        {
            if (File.Exists(fullpath))
            {
                try
                {
#if ANDROID
                    _ = AndroidHelper.ShareFileAsync(fullpath, mimetype);
#else
                    var cmd = new Process();
                    cmd.StartInfo.FileName = fullpath;
                    cmd.StartInfo.UseShellExecute = true;
                    cmd.Start();
                    cmd.Dispose();
#endif
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "KnUtils.OpenFileInOS", ex);
                }
            }
            else
            {
                Log.Add(Log.LogSeverity.Warning, "KnUtils.OpenFileInOS", "File does not exist:  " + fullpath);
            }
        }
      
        /// Determines if the current OS meets the requirements for .NET 10 / "latest" channel updates.
        /// Windows 10+ or macOS 12.0+ qualify.
        /// </summary>
        public static bool IsModernOS()
        {
            if (IsWindows)
            {
                // Windows build 10240 = Windows 10 RTM
                return Environment.OSVersion.Version.Major >= 10;
            }

            if (IsMacOS)
            {
                var ver = Environment.OSVersion.Version;
                // macOS 12.0
                return ver.Major >= 12;
            }

            // Linux and anything else: treat as modern (no restriction planned)
            return true;
        }
    }
}
