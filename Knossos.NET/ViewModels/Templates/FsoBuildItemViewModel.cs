using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace Knossos.NET.ViewModels
{
    public partial class FsoBuildItemViewModel : ViewModelBase
    {
        public FsoBuild? build;
        public CancellationTokenSource? cancellationTokenSource = null;
        private bool buildPkgsLoaded = false;
        [ObservableProperty]
        public string title = string.Empty;
        [ObservableProperty]
        public string? date;
        [ObservableProperty]
        internal string cpuArch = string.Empty;
        [ObservableProperty]
        internal string buildType = string.Empty;
        [ObservableProperty]
        internal bool isValid = false;
        [ObservableProperty]
        internal bool isInstalled = false;
        [ObservableProperty]
        internal bool isDownloading = false;
        [ObservableProperty]
        internal bool isDevMode = false;
        [ObservableProperty]
        internal bool isDetailsButtonVisible = true;
        [ObservableProperty]
        internal ObservableCollection<CheckBox> buildPkgs = new ObservableCollection<CheckBox>();

        public FsoBuildItemViewModel() 
        {
            IsValid = true;
            Title = "Test Build Title";
            Date = "19/12/20";
            CpuArch = "Architecture: x86";
            BuildType = "Builds: Release, Fred2";
        }

        public FsoBuildItemViewModel (FsoBuild build)
        {
            this.build = build;
            title = build.ToString();
            UpdateDisplayData(build);
        }

        private void UpdateDisplayData(FsoBuild build, bool skipInstalledCheck = false)
        {
            Date = build.date;
            IsInstalled = build.isInstalled;
            IsValid = false;
            CpuArch = "Architecture: ";
            BuildType = "Builds: ";
            if (build.id == "FSO")
            {
                //No detail button on official FSO builds
                IsDetailsButtonVisible = false;
            }
            else
            {
                IsDetailsButtonVisible = true;
            }

            IsDevMode = build.devMode;

            if (IsInstalled || skipInstalledCheck)
            {
                foreach (var exe in build.executables)
                {
                    if (exe.isValid)
                    {
                        IsValid = true;
                        switch (exe.arch)
                        {
                            case FsoExecArch.x86: if (!CpuArch.Contains("X86")) CpuArch += "X86   ";
                                                  if (!CpuArch.Contains("SSE2")) CpuArch += "SSE2   "; break;
                            case FsoExecArch.x64: if (!CpuArch.Contains("X64")) CpuArch += "X64   ";
                                                  if (!CpuArch.Contains("SSE2")) CpuArch += "SSE2   "; break;
                            case FsoExecArch.x86_avx2: if (!CpuArch.Contains("X86")) CpuArch += "X86   "; if (!CpuArch.Contains("AVX2 ")) CpuArch += "AVX2   "; break;
                            case FsoExecArch.x64_avx2: if (!CpuArch.Contains("X64")) CpuArch += "X64   "; if (!CpuArch.Contains("AVX2 ")) CpuArch += "AVX2   "; break;
                            case FsoExecArch.x86_avx: if (!CpuArch.Contains("X86")) CpuArch += "X86   "; if (!CpuArch.Contains("AVX ")) CpuArch += "AVX   "; break;
                            case FsoExecArch.x64_avx: if (!CpuArch.Contains("X64")) CpuArch += "X64   "; if (!CpuArch.Contains("AVX ")) CpuArch += "AVX   "; break;
                            case FsoExecArch.arm32: if (!CpuArch.Contains("ARM32")) CpuArch += "ARM32   "; break;
                            case FsoExecArch.arm64: if (!CpuArch.Contains("ARM64")) CpuArch += "ARM64   "; break;
                            case FsoExecArch.riscv32: if (!CpuArch.Contains("RISCV32")) CpuArch += "RISCV32   "; break;
                            case FsoExecArch.riscv64: if (!CpuArch.Contains("RISCV64")) CpuArch += "RISCV64   "; break;
                        }

                        if (!BuildType.Split(" ").Contains(exe.type.ToString()))
                        {
                            if(!exe.useWine)
                                BuildType += exe.type.ToString() + "  ";
                            else
                                BuildType += exe.type.ToString() + " (Wine)  ";
                        }
                    }
                }
            }
            else
            {
                IsValid = true;
            }
        }

        internal async void ViewBuildDetails()
        {
            if (build != null)
            {
                if (build.modData != null)
                {
                    await build.modData.LoadFulLNebulaData().ConfigureAwait(false);
                    await Dispatcher.UIThread.InvokeAsync(async () => {
                        UpdateDisplayData(new FsoBuild(build.modData), true);
                        var dialog = new ModDetailsView();
                        dialog.DataContext = new ModDetailsViewModel(build.modData, dialog);
                        await dialog.ShowDialog<ModDetailsView?>(MainWindow.instance);
                    }).ConfigureAwait(false);
                }
                else
                {
                    await MessageBox.Show(MainWindow.instance, "An error has ocurred while trying to display build details.", "Modjson is null", MessageBox.MessageBoxButtons.OKCancel);
                }
            }
        }

        internal async void DeleteBuildCommand()
        {
            if (build != null)
            {
                var resp = await MessageBox.Show(MainWindow.instance, "Deleting FSO build: " + build.ToString() + ". This can't be undone.", "Delete FSO build", MessageBox.MessageBoxButtons.OKCancel);
                if(resp == MessageBox.MessageBoxResult.OK)
                {
                    Log.Add(Log.LogSeverity.Information, "FsoBuildItemViewModel.DeleteBuildCommand()", "Deleting FSO build " + build.ToString());
                    FsoBuildsViewModel.Instance!.DeleteBuild(build,this);
                    var result = await Nebula.GetModData(build.id, build.version).ConfigureAwait(false);
                    Dispatcher.UIThread.Invoke(() => {
                        if (result != null)
                        {
                            FsoBuildsViewModel.Instance!.AddBuildToUi(new FsoBuild(result));
                        }
                    });
                }
            }
        }

        public bool CompareIdAndVersionToMod(Mod mod)
        {
            if(mod.id == build!.id && mod.version == build.version)
            { 
                return true; 
            }
            return false;
        }

        internal void CancelDownloadCommand()
        {
            cancellationTokenSource?.Cancel();
            IsDownloading = false;
        }

        public async void DownloadBuildExternal(Mod  mod, bool cleanupOldVersions = false)
        {
            if (!IsDownloading && !IsInstalled)
            {
                IsDownloading = true;
                cancellationTokenSource = new CancellationTokenSource();
                await mod.LoadFulLNebulaData().ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(async () => { 
                    FsoBuild? newBuild = await TaskViewModel.Instance?.InstallBuild(build!, this, mod, null, cleanupOldVersions)!;
                    if (newBuild != null)
                    {
                        //Install completed
                        IsInstalled = true;
                        build = newBuild;
                        UpdateDisplayData(newBuild, true);
                    }
                    IsDownloading = false;
                    cancellationTokenSource?.Dispose();
                    cancellationTokenSource = null;
                }).ConfigureAwait(false);
            }
        }

        internal async void DownloadBuildCommand()
        {
            if (build != null)
            {
                var result = await MessageBox.Show(MainWindow.instance, "This will download and install the FSO Build: " + build?.ToString() + ". Do you want to continue?", "Install FSO engine build", MessageBox.MessageBoxButtons.YesNo);
                if (result == MessageBox.MessageBoxResult.Yes)
                {
                    if (build != null)
                    {
                        //Check compatibility
                        if (build.modData != null)
                        {
                            await build.modData.LoadFulLNebulaData().ConfigureAwait(false);
                            var tempBuild = new FsoBuild(build.modData);
                            await Dispatcher.UIThread.InvokeAsync(async() => {
                                UpdateDisplayData(tempBuild, true);
                                if (tempBuild != null)
                                {
                                    if (IsValid)
                                    {
                                        IsDownloading = true;
                                        cancellationTokenSource = new CancellationTokenSource();
                                        FsoBuild? newBuild = await TaskViewModel.Instance?.InstallBuild(build!, this, build.modData)!;
                                        if (newBuild != null)
                                        {
                                            //Install completed
                                            IsInstalled = true;
                                            build = newBuild;
                                            UpdateDisplayData(newBuild);
                                        }
                                        IsDownloading = false;
                                        cancellationTokenSource?.Dispose();
                                        cancellationTokenSource = null;
                                    }
                                    else
                                    {
                                        await MessageBox.Show(MainWindow.instance, "This build does not have any executables compatible with your operating system or CPU arch.", "Build is not valid for this computer", MessageBox.MessageBoxButtons.OK);
                                    }
                                }
                            }).ConfigureAwait(false);
                        }
                        else
                        {
                            await MessageBox.Show(MainWindow.instance, "There was an error while getting build data, check the logs.", "Get modjson failed", MessageBox.MessageBoxButtons.OK);
                        }
                    }
                }
            }
        }

        internal async void LoadPkgData()
        {
            if(!buildPkgsLoaded && IsInstalled && build != null && build.modData != null)
            {
                try
                {
                    build.modData.ReLoadJson();
                    foreach (var pkg in build.modData.packages)
                    {
                        var ckb = new CheckBox();
                        ckb.Content = KnUtils.EscapeUnderscores(pkg.name);
                        ckb.IsChecked = true;
                        ckb.DataContext = pkg;
                        BuildPkgs.Add(ckb);
                    }

                    var nebulaModData = await Nebula.GetModData(build.id, build.version);
                    if (nebulaModData != null)
                    {
                        foreach (var nebulaPkg in nebulaModData.packages)
                        {
                            string pkgName = KnUtils.EscapeUnderscores(nebulaPkg.name);
                            if (BuildPkgs.FirstOrDefault(b => (string)b.Content! == pkgName) == null )
                            {
                                var ckb = new CheckBox();
                                ckb.Content = pkgName;
                                ckb.IsChecked = false;
                                ckb.DataContext = nebulaPkg;
                                if(!FsoBuild.IsEnviromentStringValidInstall(nebulaPkg.environment))
                                {
                                    ckb.IsEnabled = false;
                                    //Fred2 over Wine exception
                                    try
                                    {
                                        if (nebulaPkg.executables != null && KnUtils.IsLinux && nebulaPkg.environment != null && nebulaPkg.environment.ToLower().Contains("windows"))
                                        {
                                            foreach (var exec in nebulaPkg.executables)
                                            {
                                                var label = FsoBuild.GetExecType(exec.label);
                                                if (label == FsoExecType.Fred2 || label == FsoExecType.Fred2Debug)
                                                {
                                                    var props = FsoBuild.FillProperties(nebulaPkg.environment);
                                                    var arch = FsoBuild.GetExecArch(props);
                                                    var score = FsoBuild.DetermineScoreFromArch(arch);
                                                    if (score > 0)
                                                    {
                                                        ToolTip.SetTip(ckb, "This pkg contains Fred2 that can run over Wine.");
                                                        ckb.IsEnabled = true;
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                    } 
                                    catch (Exception ex) 
                                    {
                                        Log.Add(Log.LogSeverity.Error, "FsoBuildItemViewModel.LoadPkgData(Fred2 over wine)", ex);
                                    }
                                }
                                BuildPkgs.Add(ckb);
                            }
                        }
                    }

                    buildPkgsLoaded = true;
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "FsoBuildItemViewModel.LoadPkgData()", ex);
                }
            }
        }

        internal async void ModifyBuild()
        {
            if(BuildPkgs.Any() && IsInstalled && build != null && build.modData != null)
            {
                try
                {
                    //No pkgs enabled, delete
                    if (BuildPkgs.FirstOrDefault(b => b.IsEnabled) == null)
                    {
                        DeleteBuildCommand();
                        return;
                    }

                    List<ModPackage> pkgs = new List<ModPackage>();

                    foreach (var ckb in BuildPkgs)
                    {
                        if (ckb.IsEnabled)
                        {
                            var pkg = ckb.DataContext as ModPackage;

                            if (pkg != null && ckb.IsChecked.HasValue)
                            {
                                var installedPkg = build.modData.packages.FirstOrDefault(p => p.name == pkg.name && p.folder == pkg.folder);
                                if (installedPkg != null)
                                {
                                    if (!ckb.IsChecked.Value)
                                    {
                                        //Installed and unselected
                                        pkg.isSelected = false;
                                        pkgs.Add(pkg);
                                    }
                                }
                                else
                                {
                                    //Not installed and selected
                                    if (ckb.IsChecked.Value)
                                    {
                                        pkg.isSelected = true;
                                        pkgs.Add(pkg);
                                    }
                                }
                            }
                        }
                    }

                    if (pkgs.Any())
                    {
                        IsDownloading = true;
                        cancellationTokenSource = new CancellationTokenSource();
                        FsoBuild? newBuild = await TaskViewModel.Instance?.InstallBuild(build!, this, build!.modData, pkgs)!;
                        if (newBuild != null)
                        {
                            Knossos.RemoveBuild(build!);
                            build = newBuild;
                            UpdateDisplayData(newBuild);
                            Knossos.AddBuild(newBuild);
                        }
                        IsDownloading = false;
                        cancellationTokenSource?.Dispose();
                        cancellationTokenSource = null;
                    }
                }catch(Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "FsoBuildItemViewModel.ModifyBuild()", ex);
                }
            }
        }

    }
}
