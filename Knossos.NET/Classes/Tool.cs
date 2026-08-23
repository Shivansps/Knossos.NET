using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Threading.Tasks;

namespace Knossos.NET.Classes
{
    public class Tool
    {
        public string name { get; set; } = string.Empty;
        public string version { get; set; } = string.Empty;
        public string description { get; set; } = string.Empty;
        [JsonPropertyName("last_update")]
        public string lastUpdate { get; set; } = string.Empty;
        public string cmdline { get; set; } = string.Empty;
        public ToolPackage[]? packages { get; set; }
        [JsonPropertyName("favorite")]
        public bool isFavorite { get; set; } = false;
        [JsonIgnore]
        private string folderpath { get; set; } = string.Empty;
        [JsonIgnore]
        private bool scoresSet { get; set; } = false;
        [JsonIgnore]
        public bool isInstalled
        {
            get
            {
                if (folderpath != string.Empty)
                    return true;
                return false;
            }
        }

        public Tool()
        {
        }

        public Tool(string folderpath)
        {
            try
            {
                using(var jsonFile = File.OpenRead(Path.Combine(folderpath, "tool.json")))
                {
                    var temptool = JsonSerializer.Deserialize<Tool>(jsonFile)!;
                    if (temptool != null) 
                    { 
                        name = temptool.name;
                        version = temptool.version;
                        description = temptool.description;
                        lastUpdate = temptool.lastUpdate;
                        cmdline = temptool.cmdline;
                        packages = temptool.packages;
                        isFavorite = temptool.isFavorite;
                    }
                }
                this.folderpath = folderpath;
            }
            catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Tool.Constructor()", ex);
            }
        }

        /// <summary>
        /// Open a tool
        /// </summary>
        /// <param name="workingFolder"></param>
        /// <returns>true or false depending if the process start was successfull</returns>
        public async Task<bool> Open(string? workingFolder = null)
        {
            var bestPkg = GetBestPackage();
            var path = GetExecutableFullPath(bestPkg);
            if (path != null)
            {
                try
                {
                    if (!KnUtils.IsWindows)
                        KnUtils.Chmod(path);

                    if(bestPkg != null && bestPkg.wineSupport && bestPkg.os.ToLower() == "windows" && KnUtils.IsLinux)
                    {
                        Log.Add(Log.LogSeverity.Information, "Tool.Open()", "Executing Wine to run this tool: " + path);
                        //Wine
                        var res = await Wine.RunTool(path,cmdline, workingFolder, bestPkg.cpuArch);
                        return res.IsSuccess;
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Information, "Tool.Open()", "Running tool: " + path);
                        //Normal
                        using (var process = new Process())
                        {
                            process.StartInfo.FileName = path;
                            if (cmdline != string.Empty)
                                process.StartInfo.Arguments = cmdline;
                            if (workingFolder != null)
                                process.StartInfo.WorkingDirectory = workingFolder;
                            process.StartInfo.UseShellExecute = true;
                            process.Start();
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "Tool.Open()", ex);
                }
            }
            return false;
        }

        /// <summary>
        /// Gets full path to the best executable for this tool on this platform
        /// </summary>
        /// <returns>fullpath string or null</returns>
        public string? GetExecutableFullPath(ToolPackage? toolPackage = null)
        {
            var best = toolPackage == null ? GetBestPackage() : toolPackage;
            if (folderpath != string.Empty && best != null)
            {
                return Path.Combine(folderpath, best.executablePath);
            }
            else
            {
                Log.Add(Log.LogSeverity.Error, "Tool.GetExecutableFullPath()", "Invalid platform or folderpath is empty. Tool: " + name);
            }
            return null;
        }

        /// <summary>
        /// Deletes a tool, it deletes the physical file and removes it from the knossos installed tool list
        /// It is availble to re download after this
        /// </summary>
        public void Delete()
        {
            if (folderpath != string.Empty)
            {
                try
                {
                    Directory.Delete(folderpath, true);
                    Knossos.RemoveTool(this);
                    folderpath = string.Empty;
                }
                catch(Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "Tool.Delete()", ex);
                }
            }
        }

        /// <summary>
        /// Saves tool Json
        /// If path is passed it is updated internally before saving the json
        /// That it is used when we are installing a tool, since tools that arent installed does not have a folderpath
        /// </summary>
        /// <param name="path"></param>
        public void SaveJson(string? path = null)
        {
            try
            {
                if (path != null)
                    folderpath = path;
                if (folderpath != null)
                {
                    var options = new JsonSerializerOptions
                    {
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                        WriteIndented = true
                    };
                    var json = JsonSerializer.Serialize(this, options);
                    File.WriteAllText(Path.Combine(folderpath, "tool.json"), json, new UTF8Encoding(false));
                    Log.Add(Log.LogSeverity.Information, "Tool.SaveJson", "tool.json has been saved to " + Path.Combine(folderpath, "tool.json"));
                }
                else
                {
                    Log.Add(Log.LogSeverity.Error, "Tool.SaveJson", "A tool " + name + " tried to save tool.json to a null folderpath.");
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Tool.SaveJson", ex);
            }
        }

        /// <summary>
        /// Get string for the download URL of the best package for this system
        /// </summary>
        /// <returns>url for download or null if not valid package is found</returns>
        public string? GetDownloadURL()
        {
            if(IsValidPlatform())
            {
                return GetBestPackage()?.downloadUrl;
            }
            return null;
        }

        /// <summary>
        /// Gets the best package for this system OS and cpu arch
        /// </summary>
        /// <returns>Toolpackage or null if none was found</returns>
        public ToolPackage? GetBestPackage()
        {
            try
            {
                SetPkgScores();
                if (packages != null && packages.Any())
                {
                    return packages.MaxBy(x => x.score);
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Determine if this tool can be installed and run on this system
        /// </summary>
        /// <returns>true or false</returns>
        public bool IsValidPlatform()
        {
            try
            {
                SetPkgScores();
                if (packages != null && packages.Any())
                {
                    return packages.FirstOrDefault(x => x.score > 0) != null;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Sets the packages scores by OS and cpu arch
        /// </summary>
        private void SetPkgScores()
        {
            if (scoresSet)
                return;
            try
            {
                if (packages != null && packages.Any())
                {
                    foreach (var pkg in packages)
                    {
                        pkg.score = 0;
                        pkg.os = pkg.os.ToLower().Replace(" ", "");
                        var archs = pkg.cpuArch.ToLower().Replace(" ", "").Split(",");
                        if (pkg.os == "windows" && KnUtils.IsWindows || pkg.os == "linux" && KnUtils.IsLinux || pkg.os == "macosx" && KnUtils.IsMacOS)
                        {
                            foreach(var arch in archs)
                            {
                                if(arch == "x64" && KnUtils.CpuArch == "X64" ||
                                   arch == "x86" && KnUtils.CpuArch == "X86" ||
                                   arch == "arm64" && KnUtils.CpuArch == "Arm64" ||
                                   arch == "arm32" && KnUtils.CpuArch != "Arm64"  && (KnUtils.CpuArch == "Arm" || KnUtils.CpuArch == "Armv6") ||
                                   arch == "riscv64" && KnUtils.CpuArch == "RiscV64" ||
                                   arch == "riscv32" && KnUtils.CpuArch == "RiscV32")
                                {
                                    pkg.score = 100;
                                    continue;
                                }
                                if(KnUtils.IsWindows || KnUtils.IsMacOS)
                                {
                                    if (arch == "x86" && KnUtils.CpuArch == "X64")
                                    {
                                        pkg.score = 50;
                                        continue;
                                    }
                                    if (arch == "x86" && (KnUtils.CpuArch == "Arm64" || KnUtils.CpuArch == "RiscV64"))
                                    {
                                        pkg.score = 20;
                                        continue;
                                    }
                                    if (arch == "x64" && (KnUtils.CpuArch == "Arm64" || KnUtils.CpuArch == "RiscV64"))
                                    {
                                        pkg.score = 70;
                                        continue;
                                    }
                                }
                            }
                        }
                        else
                        {
                            //Wine
                            if(pkg.wineSupport && pkg.os.ToLower() == "windows" && KnUtils.IsLinux)
                            {
                                foreach (var arch in archs)
                                {
                                    if (arch == "x64" && KnUtils.CpuArch == "X64" ||
                                       arch == "x86" && KnUtils.CpuArch == "X86" ||
                                       arch == "arm64" && KnUtils.CpuArch == "Arm64" ||
                                       arch == "arm32" && KnUtils.CpuArch != "Arm64" && (KnUtils.CpuArch == "Arm" || KnUtils.CpuArch == "Armv6") ||
                                       arch == "riscv64" && KnUtils.CpuArch == "RiscV64" ||
                                       arch == "riscv32" && KnUtils.CpuArch == "RiscV32")
                                    {
                                        pkg.score = 5;
                                        continue;
                                    }
                                }
                            }
                        }
                    }
                    scoresSet = true;
                }
            }
            catch { }
        }
    }

    public class ToolPackage
    {
        [JsonPropertyName("download_url")]
        public string downloadUrl { get; set; } = string.Empty;
        public string os { get; set; } = string.Empty;
        [JsonPropertyName("cpu_arch")]
        public string cpuArch { get; set; } = string.Empty;
        [JsonPropertyName("executable_path")]
        public string executablePath { get; set; } = string.Empty;
        [JsonIgnore]
        public int score { get; set; } = 0;
        [JsonPropertyName("wine_support")]
        public bool wineSupport { get; set; } = false;
    }
}
