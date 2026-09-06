using Avalonia.Threading;
using Knossos.NET.Classes;
using Knossos.NET.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Knossos.NET.Models
{
    public enum FsoExecType
    {
        Unknown,
        Release,
        Debug,
        Fred2,
        Fred2Debug,
        QtFred,
        QtFredDebug,
        Flags
    }

    public enum FsoExecArch
    {
        x86,
        x64,
        x86_avx,
        x64_avx,
        x86_avx2,
        x64_avx2,
        arm32,
        arm64,
        riscv32,
        riscv64,
        other
    }

    public enum FsoExecEnvironment
    {
        Windows,
        Linux,
        MacOSX,
        Android,
        Unknown
    }

    public enum FsoStability
    {
        Stable,
        RC,
        Nightly,
        Custom
    }

    public struct FsoResult
    {
        public bool IsSuccess;
        public string ErrorMessage;

        public FsoResult(WineResult wineResult)
        {
            IsSuccess = wineResult.IsSuccess;
            ErrorMessage = wineResult.ErrorMessage;
        }

        public FsoResult(bool isSuccess, string errorMessage)
        {
            IsSuccess = isSuccess;
            ErrorMessage = errorMessage;
        }

        public FsoResult (bool isSuccess)
        {
            IsSuccess = isSuccess;
            ErrorMessage = string.Empty;
        }
    }

    public class FsoBuild
    {
        public string id;
        public string? title;
        public string? description;
        public string version;
        public FsoStability stability;
        public List<FsoFile> executables = new List<FsoFile>();
        public string folderPath;
        public string? date = string.Empty;
        public string? directExec = null;
        public bool isInstalled = true;
        public bool devMode = false;
        public Mod? modData;
#pragma warning disable CS0414
        private static bool _flagErrorOneWarn = false;
#pragma warning restore CS0414

        /// <summary>
        /// This is a "DirectExec" FsoBuild
        /// This is intended to be used for when the user manually selects a FSO build executable file on mod settings
        /// Version will be attempted to be parsed from the file name, if this fails 99.99.99-CurrentTimestamp will be used
        /// Stability is set to "Custom"
        /// </summary>
        /// <param name="directExecpath"></param>
        public FsoBuild(string directExecpath)
        {
            id = "DirectExec";
            title = "DirectExec";
            try
            {
                var parts = directExecpath.Split(Path.DirectorySeparatorChar);
                if (parts.Length > 1)
                {
                    var filename = parts[parts.Count() - 1];
                    parts = filename.Split('_');
                    if (parts.Length > 5)
                    {
                        version = parts[2] + "." + parts[3] + "." + parts[4] + "-" + KnUtils.GetTimestamp(DateTime.Now);
                    }
                    else
                    {
                        version = "99.99.99-" + KnUtils.GetTimestamp(DateTime.Now);
                    }
                }
                else
                {
                    version = "99.99.99-" + KnUtils.GetTimestamp(DateTime.Now);
                }
            }
            catch
            {
                version = "99.99.99-" + KnUtils.GetTimestamp(DateTime.Now);
            }
            try
            {
                FileInfo fi = new FileInfo(directExecpath);
                if(fi != null && fi.Directory != null)
                    folderPath = fi.Directory.FullName;
                else
                    folderPath = directExecpath;
            }
            catch
            {
                Log.Add(Log.LogSeverity.Error,"FsoBuid()","Unable to determine file folder for "+ directExecpath);
                folderPath = directExecpath;
            }
            stability = FsoStability.Custom;
            date = DateTime.Now.ToString();
            directExec = Path.Combine(Path.GetDirectoryName(directExecpath)!, FsoBuild.GetRealExeName(Path.GetDirectoryName(directExecpath)!, Path.GetFileName(directExecpath)));
        }
        
        /// <summary>
        /// Creates a new FsoBuild object from a Mod Json that is currently installed or it is on Nebula
        /// If the build is installed or it is a nebula build it is determined by the value of modJson.fullPath, if it is empty it is a Nebula build
        /// </summary>
        /// <param name="modJson"></param>
        public FsoBuild(Mod modJson)
        {
            if (modJson.fullPath == string.Empty)
            {
                //This is a nebula build
                isInstalled = false;
            }
            id = modJson.id;
            title = modJson.title;
            devMode = modJson.devMode;
            modData = modJson;
            description = modJson.description;
            version = modJson.version;
            folderPath = modJson.fullPath;
            stability = GetFsoStability(modJson.stability, modJson.id);
            date = modJson.lastUpdate;
            LoadExecutables(modJson);
        }

        /// <summary>
        /// Returns true if this build contains at least one executable
        /// that can be executed on this system.
        /// Note: pkg list must be already loaded
        /// </summary>
        public bool IsValidBuild()
        {
            if(executables.Count > 0 && executables.FirstOrDefault(x=>x.isValid) != null)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Updates Updates Build Json data
        /// This updates everything, including stability and the executables array
        /// </summary>
        /// <param name="modJson"></param>
        public void UpdateBuildData(Mod modJson)
        {
            if (modJson.fullPath == string.Empty)
            {
                //This is a nebula build
                isInstalled = false;
            }
            id = modJson.id;
            title = modJson.title;
            devMode = modJson.devMode;
            modData = modJson;
            description = modJson.description;
            version = modJson.version;
            folderPath = modJson.fullPath;
            stability = GetFsoStability(modJson.stability, modJson.id);
            date = modJson.lastUpdate;
            executables.Clear();
            LoadExecutables(modJson);
        }

        /// <summary>
        /// Run a FSO build
        /// Pass restested executable type, cmdline and a optional working folder and waitforexist before return
        /// </summary>
        /// <param name="executableType"></param>
        /// <param name="cmdline"></param>
        /// <param name="workingFolder"></param>
        /// <param name="waitForExit"></param>
        /// <returns>
        /// A FsoResult structure with IsSuccess = true if the FSO buld was executed fine
        /// or IsSuccess = false and a ErrorMessage with the reason if failed
        /// </returns>
        public async Task<FsoResult> RunFSO(FsoExecType executableType, string cmdline, string? workingFolder = null, bool waitForExit = false)
        {
            try
            {
                var executable = GetExecutable(executableType);
                var execPath = GetExecutablePath(executable);

                if (execPath == null)
                {
                    Log.Add(Log.LogSeverity.Error, "FsoBuild.RunFSO()", "Could not find a executable type for the requested fso build :" + executableType.ToString() + " Requested Type: " + executableType);
                    return new FsoResult(false, "Could not find a executable type for the requested fso build :" + executableType.ToString() + " Requested Type: " + executableType);
                }
#if ANDROID
                AndroidHelper.LaunchFSO(execPath, workingFolder, cmdline);
                await Task.Delay(100);
                return new FsoResult(true);
#else
                if (executable != null && executable.useWine)
                {
                    //We can assume we are in Linux and this is a cpu arch compatible Fred2 Windows executable
                    //Lets TRY to run this with Wine
                    var wineTask = Wine.RunFred2(execPath, cmdline, workingFolder, executable.arch);
                    if (waitForExit)
                    {
                        return new FsoResult(await wineTask);
                    }
                    return new FsoResult(true);
                }
                else
                {
                    //In Linux and Mac make sure it is marked as executable
                    if (KnUtils.IsLinux || KnUtils.IsMacOS)
                    {
                        KnUtils.Chmod(execPath, "+x");
                    }

                    using (var fso = new Process())
                    {
                        if (Knossos.globalSettings.prefixCMD == "")
                        {
                            fso.StartInfo.FileName = execPath;
                            fso.StartInfo.Arguments = cmdline;
                        }
                        else
                        {
                            var prefixCMD = Knossos.globalSettings.prefixCMD.Split(" ", 2);
                            fso.StartInfo.FileName = prefixCMD[0];
                            if (prefixCMD.Length > 1)
                            {
                                fso.StartInfo.Arguments = $"{prefixCMD[1]} \"{execPath}\" {cmdline}";
                            }
                            else
                            {
                                fso.StartInfo.Arguments = $"\"{execPath}\" {cmdline}";
                            }
                        }

                        fso.StartInfo.UseShellExecute = false;
                        if (workingFolder != null)
                            fso.StartInfo.WorkingDirectory = workingFolder;
                        if (Knossos.inPortableMode && Knossos.globalSettings.portableFsoPreferences ||
                            CustomLauncher.IsCustomMode && CustomLauncher.UseCustomFSODataFolder)
                        {
                            var prefPath = KnUtils.GetFSODataFolderPath();
                            if (!prefPath.EndsWith(Path.DirectorySeparatorChar))
                            {
                                prefPath += Path.DirectorySeparatorChar;
                            }
                            Log.Add(Log.LogSeverity.Information, "FsoBuild.RunFSO()", "Used preferences path: " + prefPath);
                            fso.StartInfo.EnvironmentVariables.Add("FSO_PREFERENCES_PATH", prefPath);
                        }
                        if (Knossos.globalSettings.envVars != "")
                        {
                            foreach (var envVar in Knossos.globalSettings.envVars.Split(","))
                            {
                                var envVarComponents = envVar.Split("=");
                                if (envVarComponents.Length != 2)
                                    continue;
                                fso.StartInfo.EnvironmentVariables.Add(envVarComponents[0], envVarComponents[1]);
                            }
                        }
                        fso.Start();
                        if (waitForExit)
                            await fso.WaitForExitAsync();

                        return new FsoResult(true);
                    }
                }
#endif
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "FsoBuild.RunFSO()", ex);
                return new FsoResult(false, ex.ToString());
            }
        }

        /// <summary>
        /// Get FSO flags structure of this build using the JSON format V1
        /// </summary>
        /// <returns>A FlagsJsonV1 structure or null if failed</returns>
        public async Task<FlagsJsonV1?> GetFlagsV1Async()
        {
            var executable = GetExecutable(FsoExecType.Flags);
            var fullpath = GetExecutablePath(executable);
            if (fullpath == null)
            {
                executable = GetExecutable(FsoExecType.Release);
                fullpath = GetExecutablePath(executable);
            }
            if (fullpath == null)
            {
                executable = GetExecutable(FsoExecType.Debug);
                fullpath = GetExecutablePath(executable);
            }
            if (fullpath == null)
            {
                Log.Add(Log.LogSeverity.Error, "FsoBuild.GetFlags()", "Unable to find a valid executable for this build: " + this.ToString());
                return null;
            }

            Log.Add(Log.LogSeverity.Information, "FsoBuild.GetFlags()", "Getting FSO Flags from file: " + fullpath);
            string output = string.Empty;
            string stderr = string.Empty;
            try
            {
#if ANDROID
                output = await AndroidHelper.GetFlagsStringFSOAsync(fullpath!);
                if (output == "") return null;
                return JsonSerializer.Deserialize<FlagsJsonV1>(output);
#else

                if (KnUtils.IsLinux || KnUtils.IsMacOS)
                {
                    KnUtils.Chmod(fullpath!,"+x");
                }

                using (var cmd = new Process())
                {
                    cmd.StartInfo.FileName = fullpath;
                    cmd.StartInfo.Arguments = "-get_flags json_v1";
                    cmd.StartInfo.UseShellExecute = false;
                    cmd.StartInfo.CreateNoWindow = true;
                    cmd.StartInfo.RedirectStandardOutput = true;
                    cmd.StartInfo.RedirectStandardInput = true;
                    cmd.StartInfo.RedirectStandardError = true;
                    cmd.StartInfo.StandardOutputEncoding = new UTF8Encoding(false);
                    cmd.StartInfo.WorkingDirectory = folderPath;
                    if (Knossos.inPortableMode && Knossos.globalSettings.portableFsoPreferences ||
                        CustomLauncher.IsCustomMode && CustomLauncher.UseCustomFSODataFolder)
                    {
                        var prefPath = KnUtils.GetFSODataFolderPath();
                        if (!prefPath.EndsWith(Path.DirectorySeparatorChar))
                        {
                            prefPath += Path.DirectorySeparatorChar;
                        }
                        cmd.StartInfo.EnvironmentVariables.Add("FSO_PREFERENCES_PATH", prefPath);
                    }

                    cmd.Start();
                    // Read stdout and stderr concurrently to prevent pipe deadlock.
                    // Sequential reads deadlock if stderr output exceeds the pipe buffer (~4 KB).
                    var stdoutTask = cmd.StandardOutput.ReadToEndAsync();
                    var stderrTask = cmd.StandardError.ReadToEndAsync();
                    await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
                    string result = stdoutTask.Result;
                    stderr = stderrTask.Result;
                    output = result;
                    await cmd.WaitForExitAsync().ConfigureAwait(false);

                    if (KnUtils.IsLinux && !result.Contains("{"))
                    {
                        //Possible missing dependency libs on linux like libfuse for appimage
                        var errorMsg = $"FSO exited with code {cmd.ExitCode}\n\nStdout:\n{output}\n\nStderr:\n{stderr}";
                        Log.Add(Log.LogSeverity.Error, "FsoBuild.GetFlagsV1()", errorMsg);

                        if (stderr.Contains("fuse", StringComparison.OrdinalIgnoreCase) || output.Contains("fuse", StringComparison.OrdinalIgnoreCase))
                        {
                            var libfuseError = $"libfuse is missing! FSO AppImages needs fuse to run. Install with:\n" +
                                "Ubuntu/Debian: sudo apt install libfuse2\n" +
                                "Fedora: sudo dnf install fuse\n" +
                                "Arch: sudo pacman -S fuse2";
                            Log.Add(Log.LogSeverity.Error, "FsoBuild.GetFlagsV1()", libfuseError);
                            if (!_flagErrorOneWarn)
                            {
                                _flagErrorOneWarn = true;
                                await Dispatcher.UIThread.InvokeAsync(async () => { await MessageBox.Show(MainWindow.instance, libfuseError, "Unable to run FSO", MessageBox.MessageBoxButtons.OK); });
                            }
                        }
                        else
                        {
                            if (!_flagErrorOneWarn)
                            {
                                _flagErrorOneWarn = true;
                            }
                        }
                        return null;
                    }

                    cmd.Dispose();
                    //avoiding the "fso is running in legacy config mode..."
                    if (result.Contains("{"))
                    {
                        result = result.Substring(result.IndexOf('{'));
                    }
                    return JsonSerializer.Deserialize<FlagsJsonV1>(result);
                }
#endif
            }
            catch (JsonException exJson)
            {
                //json failed try to see if it exported a binary
                if(File.Exists(folderPath+Path.DirectorySeparatorChar+"flags.lch"))
                {
                    Log.Add(Log.LogSeverity.Error, "FsoBuild.GetFlagsV1()", "FSO build "+ this +" seems to be below the minimum supported version (3.8.1) and does not support exporting flags as Json.");
                    File.Delete(folderPath + Path.DirectorySeparatorChar + "flags.lch");
                }
                else
                {
                    Log.Add(Log.LogSeverity.Error, "FsoBuild.GetFlags()", exJson);
                    Log.Add(Log.LogSeverity.Error, "FSO EXE OUTPUT ", output);
                }
                return null;
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "FsoBuild.GetFlags()", ex);
                Log.Add(Log.LogSeverity.Error, "FSO EXE OUTPUT ", output);
                return null;
            }
        }

        /// <summary>
        /// Return the best FsoFile/executable that is valid for user system and requested type.
        /// </summary>
        /// <param name="type"></param>
        /// <returns>FsoFile or null if no a valid executable is found</returns>
        public FsoFile? GetExecutable(FsoExecType type)
        {
            if (directExec != null)
            {
                return null;
            }

            var validExecs = executables.Where(b => b.isValid && b.type == type);

            if(validExecs.Any())
            {
                foreach(var exe in validExecs)
                {
                    //Make score adjustments to force SSE2
                    if (Knossos.globalSettings.forceSSE2)
                    {
                        if (exe.arch == FsoExecArch.x64 || exe.arch == FsoExecArch.x86)
                        {
                            if (exe.score < 200)
                                exe.score += 200;
                        }
                    }
                    else
                    {
                        //If SSE2 was previously enabled on this session and its not anymore return the score to normal if it was altered
                        if (exe.arch == FsoExecArch.x64 || exe.arch == FsoExecArch.x86)
                        {
                            if (exe.score > 200)
                                exe.score -= 200;
                        }
                    }
                }
                return validExecs.MaxBy(b => b.score);
            }
            return null;
        }

        /// <summary>
        /// Return the fsofile executable fullpath
        /// </summary>
        /// <param name="fsoFile"></param>
        /// <returns>Fullpath to executable or null</returns>
        public string? GetExecutablePath(FsoFile? fsoFile)
        {
            if (directExec != null)
            {
                return directExec;
            }
            if (fsoFile == null)
                return null;
            return folderPath + fsoFile.filename;
        }

        /// <summary>
        /// Loads the Build executable list
        /// sets the correct cpu arch, os, exec type and fullpath depending if it is devmode path or not
        /// Uses the package enviroment string and completely ignores executable propeties in json
        /// In fact properties are generated here at runtime
        /// </summary>
        /// <param name="modJson"></param>
        private void LoadExecutables(Mod modJson)
        {
            if (modJson.packages != null)
            {
                foreach (var package in modJson.packages)
                {
                    FsoExecEnvironment env = GetExecEnvironment(package.environment);
                    if (package.executables != null)
                    {
                        foreach (var exec in package.executables)
                        {
                            if (exec.file != null)
                            {
                                if(!isInstalled && package.environment != null)
                                {
                                    exec.properties = FillProperties(package.environment);
                                }
                                string filename = exec.file;
                                FsoExecType type = GetExecType(exec.label);
                                FsoExecArch arch = GetExecArch(exec.properties);
                                if(modJson.devMode)
                                    executables.Add(new FsoFile(package.folder+Path.DirectorySeparatorChar+exec.file.Replace(@"./",""), folderPath, type, arch, env));
                                else
                                    executables.Add(new FsoFile(exec.file, folderPath, type, arch, env));
                            }
                            else
                            {
                                Log.Add(Log.LogSeverity.Warning, "FsoBuild.LoadExecutables", "One filename in the packages list was null and ignored " + folderPath);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Complete mod executable properties from the enviroment string
        /// </summary>
        /// <param name="environment"></param>
        /// <returns>ModProperties</returns>
        public static ModProperties FillProperties(string environment)
        {
            var properties = new ModProperties();
            if (environment.ToLower().Contains("arm64"))
            {
                properties.arm64 = true;
                return properties;
            }
            if (environment.ToLower().Contains("arm32"))
            {
                properties.arm32 = true;
                return properties;
            }
            if (environment.ToLower().Contains("riscv64"))
            {
                properties.riscv64 = true;
                return properties;
            }
            if (environment.ToLower().Contains("riscv32"))
            {
                properties.riscv32 = true;
                return properties;
            }
            if (!environment.ToLower().Contains("avx"))
            {
                properties.sse2 = true;
            }
            if (environment.ToLower().Contains("x86_64") || environment.ToLower().Contains("x64"))
            {
                properties.x64 = true;
            }
            if (environment.ToLower().Contains("avx2"))
            {
                properties.avx2 = true;
            }
            else
            {
                if (environment.ToLower().Contains("avx"))
                {
                    properties.avx = true;
                }
            }
            return properties;
        }

        /// <summary>
        /// Determines if the current environment string is valid to download for the current system
        /// Used to determine witch packages to install for each build
        /// </summary>
        /// <param name="enviroment"></param>
        /// <returns>true if valid, false otherwise</returns>
        public static bool IsEnviromentStringValidInstall(string? enviroment)
        {
            if (enviroment == null || enviroment.Trim() == string.Empty)
            {
                return false;
            }

            if (enviroment.ToLower().Contains("windows") && !KnUtils.IsWindows || enviroment.ToLower().Contains("linux") && !KnUtils.IsLinux || enviroment.ToLower().Contains("macosx") && !KnUtils.IsMacOS || enviroment.ToLower().Contains("android") && !KnUtils.IsAndroid)
            {
                return false;
            }

            if (enviroment.ToLower().Contains("avx2") && !KnUtils.CpuAVX2 || enviroment.ToLower().Contains("avx") && !KnUtils.CpuAVX)
            {
                return false;
            }

            if (enviroment.ToLower().Contains("x86_64") && KnUtils.CpuArch == "X64")
            {
                return true;
            }
            if (enviroment.ToLower().Contains("arm64") && KnUtils.CpuArch == "Arm64")
            {
                return true;
            }
            if (enviroment.ToLower().Contains("arm32") && (KnUtils.CpuArch == "Armv6" || KnUtils.CpuArch == "Arm"))
            {
                return true;
            }
            if (enviroment.ToLower().Contains("riscv64") && KnUtils.CpuArch == "RiscV64")
            {
                return true;
            }
            if (enviroment.ToLower().Contains("riscv32") && KnUtils.CpuArch == "RiscV32")
            {
                return true;
            }
            if (KnUtils.CpuArch == "X86" && !enviroment.ToLower().Contains("x86_64") && !enviroment.ToLower().Contains("arm64") && !enviroment.ToLower().Contains("arm32"))
            {
                return true;
            }
            if (KnUtils.CpuArch == "X64" && !enviroment.ToLower().Contains("x86_64") && !enviroment.ToLower().Contains("arm64") && !enviroment.ToLower().Contains("arm32"))
            {
                return true;
            }

            if (KnUtils.CpuArch == "Arm64" && (KnUtils.IsMacOS || KnUtils.IsWindows) && !enviroment.ToLower().Contains("arm32") && !enviroment.ToLower().Contains("avx"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determine the operating system this build file is compiled for, using the enviroment string
        /// </summary>
        /// <param name="enviroment"></param>
        /// <returns>
        /// FsoExecEnvironment
        /// </returns>
        public static FsoExecEnvironment GetExecEnvironment(string? enviroment)
        {
            if (enviroment == null)
            {
                Log.Add(Log.LogSeverity.Warning, "FsoBuild.GetExecEnvironment", "Unable to determine the proper build enviroment. Env: null");
                return FsoExecEnvironment.Unknown;
            }

            if (enviroment.ToLower().Contains("windows"))
                return FsoExecEnvironment.Windows;
            if (enviroment.ToLower().Contains("linux"))
                return FsoExecEnvironment.Linux;
            if (enviroment.ToLower().Contains("mac"))
                return FsoExecEnvironment.MacOSX;
            if (enviroment.ToLower().Contains("android"))
                return FsoExecEnvironment.Android;

            Log.Add(Log.LogSeverity.Information, "FsoBuild.GetExecEnvironment", "Unable to determine the proper build enviroment. Env: " + enviroment);
            return FsoExecEnvironment.Unknown;
        }

        /// <summary>
        /// Determine the CPU arch this build file is compiled for from properties
        /// Note: FsoBuild properties are always generated at runtime from the enviroment string and not read from the json
        /// </summary>
        /// <param name="properties"></param>
        /// <returns>FsoExecArch</returns>
        public static FsoExecArch GetExecArch(ModProperties? properties)
        {
            if (properties == null || properties.other)
                return FsoExecArch.other;

            if (properties.arm32)
                return FsoExecArch.arm32;

            if (properties.arm64)
                return FsoExecArch.arm64;

            if (properties.riscv32)
                return FsoExecArch.riscv32;

            if (properties.riscv64)
                return FsoExecArch.riscv64;

            if (properties.x64)
            {
                if (properties.avx2)
                    return FsoExecArch.x64_avx2;
                else if (properties.avx)
                    return FsoExecArch.x64_avx;
                else
                    return FsoExecArch.x64;
            }
            else
            {
                if (properties.avx2)
                    return FsoExecArch.x86_avx2;
                else if(properties.avx)
                    return FsoExecArch.x86_avx;
                else
                    return FsoExecArch.x86;
            }
        }

        /// <summary>
        /// Gets the right FsoExecType from the label string
        /// Label = null => Release
        /// </summary>
        /// <param name="label"></param>
        /// <returns>FsoExecType</returns>
        public static FsoExecType GetExecType(string? label)
        {
            if (label == null)
                return FsoExecType.Release;

            switch (label.ToLower())
            {
                case "release": return FsoExecType.Release;

                case "debug":
                case "fastdebug":
                case "rollback build":
                case "fast debug": return FsoExecType.Debug;

                case "fred fastdebug":
                case "fred fast debug":
                case "fred debug":
                case "fred2debug":
                case "fred2 debug": return FsoExecType.Fred2Debug;

                case "fred":
                case "fred2": return FsoExecType.Fred2;

                case "qtfred fastdebug":
                case "qtfreddebug":
                case "qtfred debug": return FsoExecType.QtFredDebug;

                case "qtfred": return FsoExecType.QtFred;

                case "flags": return FsoExecType.Flags;

                default:
                    Log.Add(Log.LogSeverity.Warning, "FsoBuild.GetExecType", "Unable to determine FSO Exec Type. Label: " + label);
                    return FsoExecType.Unknown;
            }
        }

        /// <summary>
        /// Converts FsoExecType enum value to the right label string value
        /// </summary>
        /// <param name="fsoExecType"></param>
        /// <returns>label string</returns>
        public static string? GetLabelString(FsoExecType fsoExecType)
        {
            switch (fsoExecType)
            {
                case FsoExecType.Release: return null;
                case FsoExecType.Debug: return "debug";
                case FsoExecType.Fred2: return "fred2";
                case FsoExecType.Fred2Debug: return "fred2 debug";
                case FsoExecType.QtFred: return "qtfred";
                case FsoExecType.QtFredDebug: return "qtfred debug";
                case FsoExecType.Flags: return "flags";
            }
            return null;
        }

        /// <summary>
        /// Official FSO builds from nebula use "FSO" as id, only check stability on those.
        /// For all others the stability is "custom" since we cant really know.
        /// </summary>
        /// <param name="stability"></param>
        /// <param name="modId"></param>
        /// <returns>FsoStability</returns>
        public static FsoStability GetFsoStability(string? stability, string? modId)
        {
            if (modId == null || stability == null)
            {
                Log.Add(Log.LogSeverity.Warning, "FsoBuild.GetFsoStability", "Unable to determine the proper build stability for " + modId);
                return FsoStability.Stable;
            }

            if (modId != "FSO")
                return FsoStability.Custom;

            switch (stability)
            {
                case "stable": return FsoStability.Stable;
                case "rc": return FsoStability.RC;
                case "nightly": return FsoStability.Nightly;

                default:
                    Log.Add(Log.LogSeverity.Warning, "FsoBuild.GetFsoStability", "Unable to determine the proper build stability: " + stability);
                    return FsoStability.Stable;
            }
        }

        /// <summary>
        /// Gets the right enviroment string from exec arch and operative system
        /// </summary>
        /// <param name="arch"></param>
        /// <param name="os"></param>
        /// <returns>Enviroment String</returns>
        public static string GetEnviromentString(FsoExecArch arch, FsoExecEnvironment os)
        {
            string env = os.ToString().ToLower();

            switch(arch)
            {
                case FsoExecArch.x86: env += " && x86"; break;
                case FsoExecArch.x86_avx: env += " && x86 && avx"; break;
                case FsoExecArch.x86_avx2: env += " && x86 && avx2"; break;
                case FsoExecArch.x64: env += " && x86_64"; break;
                case FsoExecArch.x64_avx: env += " && x86_64 && avx"; break;
                case FsoExecArch.x64_avx2: env += " && x86_64 && avx2"; break;
                case FsoExecArch.arm32: env += " && arm32"; break;
                case FsoExecArch.arm64: env += " && arm64"; break;
                case FsoExecArch.riscv32: env += " && riscv32"; break;
                case FsoExecArch.riscv64: env += " && riscv64"; break;
            }

            return env;
        }

        /// <summary>
        /// Get the Title + Version string
        /// or fullpath in case of "DirectExec"
        /// </summary>
        /// <returns>string</returns>
        public override string ToString()
        {
            if(directExec != null)
                return directExec;
            return title + " " + version;
        }

        /// <summary>
        /// To use with the List .Sort()
        /// </summary>
        public static int CompareDatesAsTimestamp(FsoBuild build1, FsoBuild build2)
        {
            if(build1.date == null && build2.date == null)
                return 0;
            if(build1.date == null)
                return -1;
            if(build2.date == null) 
                return 1;
            return string.Compare(build2.date.Replace("-", "").Trim(), build1.date.Replace("-", "").Trim());
        }

        /// <summary>
        /// To use with the List .Sort()
        /// </summary>
        public static int CompareVersion(FsoBuild build1, FsoBuild build2)
        {
            //inverted
            return SemanticVersion.Compare(build2.version, build1.version);
        }

        /// <summary>
        /// Used to fix up the executable name for macOS app bundles. Takes as arguments
        /// the full path to the directory containing the current exe and the name of what
        /// is currently considered the exe.
        /// </summary>
        /// <param name="pathName"></param>
        /// <param name="fileName"></param>
        /// <returns>string</returns>
        public static string GetRealExeName(string pathName, string fileName)
        {
            try {
                if (KnUtils.IsMacOS)
                {
                    var exe = new FileInfo(Path.Combine(pathName, fileName));
                    if (exe != null && ((exe.Attributes & FileAttributes.Directory) == FileAttributes.Directory))
                    {
                        var files = Directory.GetFiles(Path.Combine(exe.FullName, "Contents/MacOS"));
                        foreach(string file in files)
                        {
                            var fi = new FileInfo(file);
                            if (fi != null && (fi.Name.ToLower().Contains("fs2_open") || fi.Name.ToLower().Contains("fred2_open") || fi.Name.ToLower().Contains("qtfred")))
                            {
                                return exe.Name + "/Contents/MacOS/" + fi.Name;
                            }
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "AddUserBuildViewModel.GetExeName()", ex);
            }

            return fileName;
        }

        /// <summary>
        /// Calculate the score, keep in mind in Windows and MAC x86 can run on x64 and X86/X64 can run on ARM64
        /// No support for 32 bits ARM on Windows/Mac, also no support for x86/x64 AVX on Windows ARM
        /// If checkForceSSE2 is true then x86/x64 non AVX builds gets 100 points bonus 
        /// </summary>
        /// <param name="arch"></param>
        /// <returns>score value from 0 to 100 normally, or higher in case of SSE2 builds if they are forced</returns>
        public static int DetermineScoreFromArch(FsoExecArch arch, bool checkForceSSE2 = false)
        {
            int score = 0;
            if (KnUtils.IsWindows || KnUtils.IsMacOS)
            {
                switch (arch)
                {
                    case FsoExecArch.x64_avx2:
                        switch (KnUtils.CpuArch)
                        {
                            case "X64":
                                score += KnUtils.CpuAVX2 ? 100 : 0;
                                break;
                            case "X86":
                                break;
                            case "Arm64":
                                //score += 50;
                                break;
                            case "Arm":
                            case "Armv6":
                                break;
                            case "RiscV32":
                                break;
                            case "RiscV64":
                                break;
                        }
                        break;
                    case FsoExecArch.x64_avx:
                        switch (KnUtils.CpuArch)
                        {
                            case "X64":
                                score += KnUtils.CpuAVX ? 90 : 0;
                                break;
                            case "X86":
                                break;
                            case "Arm64":
                                //score += 45;
                                break;
                            case "Arm":
                            case "Armv6":
                                break;
                            case "RiscV32":
                                break;
                            case "RiscV64":
                                break;
                        }
                        break;
                    case FsoExecArch.x64:
                        switch (KnUtils.CpuArch)
                        {
                            case "X64":
                                score += 80;
                                if (checkForceSSE2 && Knossos.globalSettings.forceSSE2)
                                    score += 100;
                                break;
                            case "X86":
                                break;
                            case "Arm64":
                                score += 60;
                                break;
                            case "Arm":
                            case "Armv6":
                                break;
                            case "RiscV32":
                                break;
                            case "RiscV64":
                                break;
                        }
                        break;
                    case FsoExecArch.x86_avx2:
                        switch (KnUtils.CpuArch)
                        {
                            case "X64":
                                score += KnUtils.CpuAVX2 ? 50 : 0;
                                break;
                            case "X86":
                                score += KnUtils.CpuAVX2 ? 100 : 0;
                                break;
                            case "Arm64":
                                //score += 25;
                                break;
                            case "Arm":
                            case "Armv6":
                                break;
                            case "RiscV32":
                                break;
                            case "RiscV64":
                                break;
                        }
                        break;
                    case FsoExecArch.x86_avx:
                        switch (KnUtils.CpuArch)
                        {
                            case "X64":
                                score += KnUtils.CpuAVX ? 45 : 0;
                                break;
                            case "X86":
                                score += KnUtils.CpuAVX ? 90 : 0;
                                break;
                            case "Arm64":
                                //score += 15;
                                break;
                            case "Arm":
                            case "Armv6":
                                break;
                            case "RiscV32":
                                break;
                            case "RiscV64":
                                break;
                        }
                        break;
                    case FsoExecArch.x86:
                        switch (KnUtils.CpuArch)
                        {
                            case "X64":
                                score += 40;
                                if (checkForceSSE2 && Knossos.globalSettings.forceSSE2)
                                    score += 100;
                                break;
                            case "X86":
                                score += 80;
                                if (checkForceSSE2 && Knossos.globalSettings.forceSSE2)
                                    score += 100;
                                break;
                            case "Arm64":
                                score += 30;
                                break;
                            case "Arm":
                            case "Armv6":
                                break;
                            case "RiscV32":
                                break;
                            case "RiscV64":
                                break;
                        }
                        break;
                    case FsoExecArch.arm64:
                        switch (KnUtils.CpuArch)
                        {
                            case "X64":
                                break;
                            case "X86":
                                break;
                            case "Arm64":
                                score += 100;
                                break;
                            case "Arm":
                            case "Armv6":
                                break;
                            case "RiscV32":
                                break;
                            case "RiscV64":
                                break;
                        }
                        break;
                    case FsoExecArch.arm32:
                        switch (KnUtils.CpuArch)
                        {
                            case "X64":
                                break;
                            case "X86":
                                break;
                            case "Arm64":
                                break;
                            case "Arm":
                            case "Armv6":
                                score += 100;
                                break;
                            case "RiscV32":
                                break;
                            case "RiscV64":
                                break;
                        }
                        break;
                    case FsoExecArch.riscv32:
                        switch (KnUtils.CpuArch)
                        {
                            case "X64":
                                break;
                            case "X86":
                                break;
                            case "RiscV64":
                                break;
                            case "RiscV32":
                                score += 100;
                                break;
                            case "Arm64":
                                break;
                            case "Arm":
                            case "Armv6":
                                break;
                        }
                        break;
                    case FsoExecArch.riscv64:
                        switch (KnUtils.CpuArch)
                        {
                            case "X64":
                                break;
                            case "X86":
                                break;
                            case "RiscV32":
                                break;
                            case "RiscV64":
                                score += 100;
                                break;
                            case "Arm64":
                                break;
                            case "Arm":
                            case "Armv6":
                                break;
                        }
                        break;
                    default:
                        Log.Add(Log.LogSeverity.Error, "FsoFile.DetermineScore", "FsoFile.DetermineScore() is missing the case for: " + arch);
                        break;
                }
            }
            else
            {
                //Linux and Android
                switch (arch)
                {
                    case FsoExecArch.x64_avx2:
                        switch (KnUtils.CpuArch)
                        {
                            case "X64":
                                score += KnUtils.CpuAVX2 ? 100 : 0;
                                break;
                            case "X86":
                                break;
                            case "Arm64":
                                break;
                            case "Arm":
                            case "Armv6":
                                break;
                            case "RiscV32":
                                break;
                            case "RiscV64":
                                break;
                        }
                        break;
                    case FsoExecArch.x64_avx:
                        switch (KnUtils.CpuArch)
                        {
                            case "X64":
                                score += KnUtils.CpuAVX ? 90 : 0;
                                break;
                            case "X86":
                                break;
                            case "Arm64":
                                break;
                            case "Arm":
                            case "Armv6":
                                break;
                            case "RiscV32":
                                break;
                            case "RiscV64":
                                break;
                        }
                        break;
                    case FsoExecArch.x64:
                        switch (KnUtils.CpuArch)
                        {
                            case "X64":
                                score += 80;
                                break;
                            case "X86":
                                break;
                            case "Arm64":
                                break;
                            case "Arm":
                            case "Armv6":
                                break;
                            case "RiscV32":
                                break;
                            case "RiscV64":
                                break;
                        }
                        break;
                    case FsoExecArch.x86_avx2:
                        switch (KnUtils.CpuArch)
                        {
                            case "X64":
                                break;
                            case "X86":
                                score += KnUtils.CpuAVX2 ? 100 : 0;
                                break;
                            case "Arm64":
                                break;
                            case "Arm":
                            case "Armv6":
                                break;
                            case "RiscV32":
                                break;
                            case "RiscV64":
                                break;
                        }
                        break;
                    case FsoExecArch.x86_avx:
                        switch (KnUtils.CpuArch)
                        {
                            case "X64":
                                break;
                            case "X86":
                                score += KnUtils.CpuAVX ? 90 : 0;
                                break;
                            case "Arm64":
                                break;
                            case "Arm":
                            case "Armv6":
                                break;
                            case "RiscV32":
                                break;
                            case "RiscV64":
                                break;
                        }
                        break;
                    case FsoExecArch.x86:
                        switch (KnUtils.CpuArch)
                        {
                            case "X64":
                                break;
                            case "X86":
                                score += 80;
                                break;
                            case "Arm64":
                                break;
                            case "Arm":
                            case "Armv6":
                                break;
                            case "RiscV32":
                                break;
                            case "RiscV64":
                                break;
                        }
                        break;
                    case FsoExecArch.arm64:
                        switch (KnUtils.CpuArch)
                        {
                            case "X64":
                                break;
                            case "X86":
                                break;
                            case "Arm64":
                                score += 100;
                                break;
                            case "Arm":
                            case "Armv6":
                                break;
                            case "RiscV32":
                                break;
                            case "RiscV64":
                                break;
                        }
                        break;
                    case FsoExecArch.arm32:
                        switch (KnUtils.CpuArch)
                        {
                            case "X64":
                                break;
                            case "X86":
                                break;
                            case "Arm64":
                                break;
                            case "Arm":
                            case "Armv6":
                                score += 100;
                                break;
                            case "RiscV32":
                                break;
                            case "RiscV64":
                                break;
                        }
                        break;
                    case FsoExecArch.riscv32:
                        switch (KnUtils.CpuArch)
                        {
                            case "X64":
                                break;
                            case "X86":
                                break;
                            case "Arm64":
                                break;
                            case "Arm":
                            case "Armv6":
                                break;
                            case "RiscV32":
                                score += 100;
                                break;
                            case "RiscV64":
                                break;
                        }
                        break;
                    case FsoExecArch.riscv64:
                        switch (KnUtils.CpuArch)
                        {
                            case "X64":
                                break;
                            case "X86":
                                break;
                            case "Arm64":
                                break;
                            case "Arm":
                            case "Armv6":
                                break;
                            case "RiscV32":
                                break;
                            case "RiscV64":
                                score += 100;
                                break;
                        }
                        break;
                    default:
                        Log.Add(Log.LogSeverity.Error, "FsoFile.DetermineScore", "FsoFile.DetermineScore() is missing the case for: " + arch);
                        break;
                }
            }
            return score;
        }
    }

    public class FsoFile
    {
        public string filename;
        public FsoExecType type;
        public FsoExecArch arch;
        public FsoExecEnvironment env;
        public bool isValid = false;
        public bool useWine = false;
        public int score { get; internal set; } = 0;

        public FsoFile(string filename, string modpath, FsoExecType type, FsoExecArch arch, FsoExecEnvironment env)
        {
            this.filename = filename;
            this.type = type;
            this.arch = arch;
            this.env = env;
            this.score = DetermineScore(modpath);
            if(score > 0)
                isValid = true;
        }

        /// <summary>
        /// Determine FSO File score based on OS and CPU Arch
        /// </summary>
        /// <param name="modpath"></param>
        /// <returns>int score from 0 to 100</returns>
        private int DetermineScore(string modpath)
        {
            int score = 0;
            string filePath = string.Empty;
            if (modpath != string.Empty)
                filePath = Path.Combine(modpath, filename);
            /* First the cases that are an instant 0 */
            if (arch == FsoExecArch.other || env == FsoExecEnvironment.Unknown || type == FsoExecType.Unknown)
            {
                if (modpath != string.Empty)
                    Log.Add(Log.LogSeverity.Warning, "FsoFile.DetermineScore", "File: " + filePath + " has an unknown cpu arch, build or enviroment type in json.");
                return 0;
            }
            // get exe name here for exists check
            if (modpath != string.Empty && !File.Exists(filePath))
            {
                if (modpath != string.Empty)
                    Log.Add(Log.LogSeverity.Warning, "FsoFile.DetermineScore", "File: " + filePath + " does not exist!");
                return 0;
            }

            if ((env == FsoExecEnvironment.Windows && !KnUtils.IsWindows) || (env == FsoExecEnvironment.Linux && !KnUtils.IsLinux) || (env == FsoExecEnvironment.MacOSX && !KnUtils.IsMacOS))
            {
                //Fred2 on Linux over Wine exception
                //Note: this will only be valid if the exec arch matches the host cpu arch, otherwise DetermineScoreFromArch() will return 0 anyway.
                //Example: 32 bits Fred2.exe will get 0 score on a x64 cpu
                if (KnUtils.IsLinux && env == FsoExecEnvironment.Windows && (type == FsoExecType.Fred2 || type == FsoExecType.Fred2Debug))
                {
                    useWine = true;
                }
                else
                {
                    if (modpath != string.Empty)
                        Log.Add(Log.LogSeverity.Warning, "FsoFile.DetermineScore", "File: " + filePath + " is not valid for this OS. Detected: " + env);
                    return 0;
                }
            }
            
            score = FsoBuild.DetermineScoreFromArch(arch);

            return score;
        }
    }
}
