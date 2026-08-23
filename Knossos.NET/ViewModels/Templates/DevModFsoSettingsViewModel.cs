using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System.Linq;
using System.Threading.Tasks;

namespace Knossos.NET.ViewModels
{
    public partial class DevModFsoSettingsViewModel : ViewModelBase
    {
        private DevModEditorViewModel? editor;
        [ObservableProperty]
        internal bool configureBuildOpen = false;
        [ObservableProperty]
        internal FsoBuildPickerViewModel? fsoPicker;
        [ObservableProperty]
        internal FsoFlagsViewModel? fsoFlags = null;
        [ObservableProperty]
        internal string? cmdLine = string.Empty;

        public DevModFsoSettingsViewModel()
        {

        }

        public DevModFsoSettingsViewModel(DevModEditorViewModel devModEditorViewModel)
        {
            editor = devModEditorViewModel;
            LoadFsoPicker();
        }

        private void LoadFsoPicker()
        {
            if (editor != null)
            {
                if (editor.ActiveVersion.modSettings.customBuildId == null)
                {
                    FsoPicker = new FsoBuildPickerViewModel(null,null);
                }
                else
                {
                    if (editor.ActiveVersion.modSettings.customBuildExec != null)
                    {
                        FsoPicker = new FsoBuildPickerViewModel(new FsoBuild(editor.ActiveVersion.modSettings.customBuildExec), null);
                    }
                    else
                    {
                        var matchingBuilds = Knossos.GetInstalledBuildsList(editor.ActiveVersion.modSettings.customBuildId);
                        if (matchingBuilds.Any())
                        {
                            var theBuild = matchingBuilds.FirstOrDefault(build => build.version == editor.ActiveVersion.modSettings.customBuildVersion);
                            if (theBuild != null)
                            {
                                FsoPicker = new FsoBuildPickerViewModel(theBuild, null);
                            }
                            else
                            {
                                Log.Add(Log.LogSeverity.Warning, "DevModFsoSettingsViewModel.Constructor()", "Missing user-saved build version for mod: " + editor.ActiveVersion.tile + " - " + editor.ActiveVersion.version + " requested build id: " + editor.ActiveVersion.modSettings.customBuildId + " and version: " + editor.ActiveVersion.modSettings.customBuildVersion);
                                FsoPicker = new FsoBuildPickerViewModel(null, null);
                            }
                        }
                        else
                        {
                            Log.Add(Log.LogSeverity.Warning, "DevModFsoSettingsViewModel.Constructor()", "Missing user-saved build id for mod: " + editor.ActiveVersion.tile + " - " + editor.ActiveVersion.version + " requested build id: " + editor.ActiveVersion.modSettings.customBuildId);
                            FsoPicker = new FsoBuildPickerViewModel(null, null);
                        }
                    }
                }
                CmdLine = editor.ActiveVersion.cmdline;
            }
        }

        internal async void ConfigureBuild()
        {
            if (editor == null || FsoPicker == null)
                return;
            ConfigureBuildOpen = true;
            FsoBuild? fsoBuild = FsoPicker.GetSelectedFsoBuild();

            /* If set to "mod select" pick the one from the current dependency list displayed in this dialog */
            if (fsoBuild == null)
            {
                foreach (var pkg in editor.ActiveVersion.packages)
                {
                    if (pkg.dependencies != null)
                    {
                        foreach (var dep in pkg.dependencies)
                        {
                            if (dep != null && Knossos.GetInstalledBuildsList(dep.id).Any())
                            {
                                fsoBuild = dep.SelectBuild();
                                break;
                            }
                        }
                        if (fsoBuild != null)
                        {
                            break;
                        }
                    }
                }
            }

            if(fsoBuild != null)
            {
                var flagsV1 = await Task.Run(fsoBuild.GetFlagsV1Async);
                if(flagsV1 != null)
                {
                    FsoFlags = new FsoFlagsViewModel(flagsV1, CmdLine);
                }
                else
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await MessageBox.Show(MainWindow.instance, "Unable to get flag data from build " + fsoBuild + " It might be below the minimal version supported (3.8.1) or some other error ocurred.", "Invalid flag data", MessageBox.MessageBoxButtons.OK);
                    });

                }
            }
            else
            {
                /* No valid build found, send message */
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await MessageBox.Show(MainWindow.instance, "Unable to resolve FSO build dependency, download the correct one or manually select a FSO version. ", "Not engine build found", MessageBox.MessageBoxButtons.OK);
                });
            }
        }

        internal void SaveSettingsCommand()
        {
            if (editor != null && FsoPicker != null)
            {
                //FSO Build
                var customBuild = FsoPicker.GetSelectedFsoBuild();
                if (customBuild != null)
                {
                    editor.ActiveVersion.modSettings.customBuildId = customBuild.id;
                    editor.ActiveVersion.modSettings.customBuildVersion = customBuild.version;
                    editor.ActiveVersion.modSettings.customBuildExec = customBuild.directExec;
                }
                else
                {
                    editor.ActiveVersion.modSettings.customBuildId = null;
                    editor.ActiveVersion.modSettings.customBuildVersion = null;
                    editor.ActiveVersion.modSettings.customBuildExec = null;
                }

                //FSO Flags
                if (FsoFlags != null)
                {
                    CmdLine = FsoFlags.GetCmdLine();
                    editor.ActiveVersion.modSettings.customCmdLine = null;
                }
                editor.ActiveVersion.cmdline = CmdLine;
                editor.ActiveVersion.modSettings.Save();
                editor.ActiveVersion.SaveJson();
            }
        }

        /// <summary>
        /// Updates the FSO picker combobox to display changes to installed fso builds
        /// Note: it actually destroys the current fso picker and it replaces it with a new one.
        /// </summary>
        public void UpdateFsoPicker()
        {
            //Run from UI thread
            Dispatcher.UIThread.Invoke(()=> { LoadFsoPicker(); });
        }
    }
}
