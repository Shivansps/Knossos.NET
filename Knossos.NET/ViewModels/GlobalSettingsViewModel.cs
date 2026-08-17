using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Knossos.NET.ViewModels
{
    /// <summary>
    /// This is the class responsable for the "settings" tab
    /// </summary>
    public partial class GlobalSettingsViewModel : ViewModelBase
    {
        private bool UnCommitedChanges = false;

        [ObservableProperty]
        internal ObservableCollection<string> androidFolderPaths = new ObservableCollection<string>();

        internal int androidFolderPathsSelectedIndex = 0;
        internal int AndroidFolderPathsSelectedIndex
        {
            get { return androidFolderPathsSelectedIndex; }
            set { if (androidFolderPathsSelectedIndex != value) { this.SetProperty(ref androidFolderPathsSelectedIndex, value); UpdateBasePathAndroid(); } }
        }

        internal bool IsAndroid { get; } = KnUtils.IsAndroid;

        /* Limiters definition */
        private const long speedUnlimited = 0;
        private const long speedHalfMB = 850000;
        private const long speed1MB = 18000000;
        private const long speed2MB = 34000000;
        private const long speed3MB = 50000000;
        private const long speed4MB = 68000000;
        private const long speed5MB = 84000000;
        private const long speed6MB = 102000000;
        private const long speed7MB = 120000000;
        private const long speed8MB = 137000000;
        private const long speed9MB = 155000000;
        private const long speed10MB = 170000000;

        /* For display only */
        [ObservableProperty]
        internal string knossosUpdateChannelInfo = "";
        [ObservableProperty]
        internal string knossosUpdateTooltip = "";
        [ObservableProperty]
        internal bool isPortableMode = false;
        [ObservableProperty]
        internal bool flagDataLoaded = false;
        [ObservableProperty]
        internal bool enable16BitColor = false;
        [ObservableProperty]
        internal bool windowsOS = false;
        [ObservableProperty]
        internal string imgCacheSize = "0 MB";
        [ObservableProperty]
        internal string fsoCacheSize = "0 MB";
        [ObservableProperty]
        internal bool fs2RootPack = false;
        [ObservableProperty]
        internal string numberOfMods = string.Empty;
        [ObservableProperty]
        internal string numberOfBuilds = string.Empty;
        [ObservableProperty]
        internal string detectedOS = string.Empty;
        [ObservableProperty]
        internal string cpuArch = string.Empty;
        [ObservableProperty]
        internal string knossosVersion = string.Empty;
        [ObservableProperty]
        internal bool isAVX = false;
        [ObservableProperty]
        internal bool isAVX2 = false;
        [ObservableProperty]
        internal bool displaySettingsWarning = true;
        [ObservableProperty]
        internal bool quickStartButtonVisibility = true;
        [ObservableProperty]
        internal bool expandKnossosSettings = false;

        /* Knossos Settings */
        [ObservableProperty]
        internal string basePath = string.Empty; //When this is changed settings are saved immediately.

        private bool antiStuck = true;
        internal bool AntiStuck
        {
            get { return antiStuck; }
            set { if (antiStuck != value) { this.SetProperty(ref antiStuck, value); UnCommitedChanges = true; } }
        }

        private bool blCfNebula = false;
        internal bool BlCfNebula
        {
            get { return blCfNebula; }
            set { if (blCfNebula != value) { this.SetProperty(ref blCfNebula, value); UnCommitedChanges = true; } }
        }

        private bool blDlNebula = false;
        internal bool BlDlNebula
        {
            get { return blDlNebula; }
            set { if (blDlNebula != value) { this.SetProperty(ref blDlNebula, value); UnCommitedChanges = true; } }
        }

        private bool blHestia = false;
        internal bool BlHestia
        {
            get { return blHestia; }
            set { if (blHestia != value) { this.SetProperty(ref blHestia, value); UnCommitedChanges = true; } }
        }

        private bool enableLogFile = true;
        internal bool EnableLogFile
        {
            get { return enableLogFile; }
            set { if (enableLogFile != value) { this.SetProperty(ref enableLogFile, value); UnCommitedChanges = true; } }
        }

        private int logLevel = 1;
        internal int LogLevel
        {
            get { return logLevel; }
            set { if (logLevel != value) { this.SetProperty(ref logLevel, value); UnCommitedChanges = true; } }
        }

        private bool forceSSE2 = false;
        internal bool ForceSSE2
        {
            get { return forceSSE2; }
            set { if (forceSSE2 != value) { this.SetProperty(ref forceSSE2, value); UnCommitedChanges = true; } }
        }

        private int maxConcurrentSubtasks = 3;
        internal int MaxConcurrentSubtasks
        {
            get { return maxConcurrentSubtasks; }
            set { if (maxConcurrentSubtasks != value) { this.SetProperty(ref maxConcurrentSubtasks, value); UnCommitedChanges = true; } }
        }

        private int maxUploadsRetries = 4;
        internal int MaxUploadsRetries
        {
            get { return maxUploadsRetries; }
            set { if (maxUploadsRetries != value) { this.SetProperty(ref maxUploadsRetries, value); UnCommitedChanges = true; } }
        }

        private long maxDownloadSpeedIndex = 0;
        internal long MaxDownloadSpeedIndex
        {
            get { return maxDownloadSpeedIndex; }
            set { if (maxDownloadSpeedIndex != value) { this.SetProperty(ref maxDownloadSpeedIndex, value); UnCommitedChanges = true; } }
        }

        private CompressionSettings modCompression = CompressionSettings.Manual;
        internal CompressionSettings ModCompression
        {
            get { return modCompression; }
            set { if (modCompression != value) { this.SetProperty(ref modCompression, value); UnCommitedChanges = true; } }
        }

        private int compressionMaxParallelism = 2;
        internal int CompressionMaxParallelism
        {
            get { return compressionMaxParallelism; }
            set { if (compressionMaxParallelism != value) { this.SetProperty(ref compressionMaxParallelism, value); UnCommitedChanges = true; } }
        }

        private bool checkUpdates = true;
        internal bool CheckUpdates
        {
            get { return checkUpdates; }
            set { if (checkUpdates != value) { this.SetProperty(ref checkUpdates, value); UnCommitedChanges = true; } }
        }

        private bool autoUpdate = false;
        internal bool AutoUpdate
        {
            get { return autoUpdate; }
            set { if (autoUpdate != value) { this.SetProperty(ref autoUpdate, value); UnCommitedChanges = true; } }
        }

        private bool deleteUploadedFiles = true;
        internal bool DeleteUploadedFiles
        {
            get { return deleteUploadedFiles; }
            set { if (deleteUploadedFiles != value) { this.SetProperty(ref deleteUploadedFiles, value); UnCommitedChanges = true; } }
        }

        private bool updateNightly = false;
        internal bool UpdateNightly
        {
            get { return updateNightly; }
            set { if (updateNightly != value) { this.SetProperty(ref updateNightly, value); UnCommitedChanges = true; } }
        }

        private bool updateStable = false;
        internal bool UpdateStable
        {
            get { return updateStable; }
            set { if (updateStable != value) { this.SetProperty(ref updateStable, value); UnCommitedChanges = true; } }
        }

        private bool updateRC = false;
        internal bool UpdateRC
        {
            get { return updateRC; }
            set { if (updateRC != value) { this.SetProperty(ref updateRC, value); UnCommitedChanges = true; } }
        }

        private bool deleteOlder = false;
        internal bool DeleteOlder
        {
            get { return deleteOlder; }
            set { if (deleteOlder != value) { this.SetProperty(ref deleteOlder, value); UnCommitedChanges = true; } }
        }

        private bool showDevOptions = false;
        internal bool ShowDevOptions
        {
            get { return showDevOptions; }
            set { if (showDevOptions != value) { this.SetProperty(ref showDevOptions, value); UnCommitedChanges = true; } }
        }

        private bool singleView = true;
        internal bool SingleView
        {
            get { return singleView; }
            set { if (singleView != value) { this.SetProperty(ref singleView, value); UnCommitedChanges = true; } }
        }

        /* This change is applied right away */
        private bool closeToTray = false;
        internal bool CloseToTray
        {
            get { return closeToTray; }
            set { if (closeToTray != value) { this.SetProperty(ref closeToTray, value); Knossos.globalSettings.closeToTray = value ; UnCommitedChanges = true; } }
        }

        /*VIDEO*/
        private int bitsSelectedIndex = 0;
        internal int BitsSelectedIndex
        {
            get { return bitsSelectedIndex; }
            set { if (bitsSelectedIndex != value) { this.SetProperty(ref bitsSelectedIndex, value); UnCommitedChanges = true; } }
        }

        private int resolutionSelectedIndex = 0;
        internal int ResolutionSelectedIndex
        {
            get { return resolutionSelectedIndex; }
            set { if (resolutionSelectedIndex != value) { this.SetProperty(ref resolutionSelectedIndex, value); UnCommitedChanges = true; } }
        }

        private int textureSelectedIndex = 0;
        internal int TextureSelectedIndex
        {
            get { return textureSelectedIndex; }
            set { if (textureSelectedIndex != value) { this.SetProperty(ref textureSelectedIndex, value); UnCommitedChanges = true; } }
        }

        internal ObservableCollection<ComboBoxItem> ResolutionItems { get; set; } = new ObservableCollection<ComboBoxItem>();

        private int shadowQualitySelectedIndex = 0;
        internal int ShadowQualitySelectedIndex
        {
            get { return shadowQualitySelectedIndex; }
            set { if (shadowQualitySelectedIndex != value) { this.SetProperty(ref shadowQualitySelectedIndex, value); UnCommitedChanges = true; } }
        }

        private int aaSelectedIndex = 5;
        internal int AaSelectedIndex
        {
            get { return aaSelectedIndex; }
            set { if (aaSelectedIndex != value) { this.SetProperty(ref aaSelectedIndex, value); UnCommitedChanges = true; } }
        }

        private int msaaSelectedIndex = 0;
        internal int MsaaSelectedIndex
        {
            get { return msaaSelectedIndex; }
            set { if (msaaSelectedIndex != value) { this.SetProperty(ref msaaSelectedIndex, value); UnCommitedChanges = true; } }
        }

        private bool enableSoftParticles = true;
        internal bool EnableSoftParticles
        {
            get { return enableSoftParticles; }
            set { if (enableSoftParticles != value) { this.SetProperty(ref enableSoftParticles, value); UnCommitedChanges = true; } }
        }

        private bool enableDeferredLighting = true;
        internal bool EnableDeferredLighting
        {
            get { return enableDeferredLighting; }
            set { if (enableDeferredLighting != value) { this.SetProperty(ref enableDeferredLighting, value); UnCommitedChanges = true; } }
        }

        private int windowMode = 2;
        internal int WindowMode
        {
            get { return windowMode; }
            set { if (windowMode != value) { this.SetProperty(ref windowMode, value); UnCommitedChanges = true; } }
        }

        private bool vsync = true;
        internal bool Vsync
        {
            get { return vsync; }
            set { if (vsync != value) { this.SetProperty(ref vsync, value); UnCommitedChanges = true; } }
        }

        private bool postProcess = true;
        internal bool PostProcess
        {
            get { return postProcess; }
            set { if (postProcess != value) { this.SetProperty(ref postProcess, value); UnCommitedChanges = true; } }
        }

        private bool noFpsCapping = false;
        internal bool NoFpsCapping
        {
            get { return noFpsCapping; }
            set { if (noFpsCapping != value) { this.SetProperty(ref noFpsCapping, value); UnCommitedChanges = true; } }
        }

        private bool showFps = false;
        internal bool ShowFps
        {
            get { return showFps; }
            set { if (showFps != value) { this.SetProperty(ref showFps, value); UnCommitedChanges = true; } }
        }

        /*AUDIO*/
        private int playbackSelectedIndex = 0;
        internal int PlaybackSelectedIndex
        {
            get { return playbackSelectedIndex; }
            set { if (playbackSelectedIndex != value) { this.SetProperty(ref playbackSelectedIndex, value); UnCommitedChanges = true; } }
        }

        internal ObservableCollection<ComboBoxItem> PlaybackItems { get; set; } = new ObservableCollection<ComboBoxItem>();

        private int captureSelectedIndex = 0;
        internal int CaptureSelectedIndex
        {
            get { return captureSelectedIndex; }
            set { if (captureSelectedIndex != value) { this.SetProperty(ref captureSelectedIndex, value); UnCommitedChanges = true; } }
        }

        internal ObservableCollection<ComboBoxItem> CaptureItems { get; set; } = new ObservableCollection<ComboBoxItem>();

        private int sampleRateSelectedIndex = 0;
        internal int SampleRateSelectedIndex
        {
            get { return sampleRateSelectedIndex; }
            set { if (sampleRateSelectedIndex != value) { this.SetProperty(ref sampleRateSelectedIndex, value); UnCommitedChanges = true; } }
        }

        private bool enableEFX = false;
        internal bool EnableEFX
        {
            get { return enableEFX; }
            set { if (enableEFX != value) { this.SetProperty(ref enableEFX, value); UnCommitedChanges = true; } }
        }

        private bool disableAudio = false;
        internal bool DisableAudio
        {
            get { return disableAudio; }
            set { if (disableAudio != value) { this.SetProperty(ref disableAudio, value); UnCommitedChanges = true; } }
        }

        private bool disableMusic = false;
        internal bool DisableMusic
        {
            get { return disableMusic; }
            set { if (disableMusic != value) { this.SetProperty(ref disableMusic, value); UnCommitedChanges = true; } }
        }

        private bool enableTTS = true;
        internal bool EnableTTS
        {
            get { return enableTTS; }
            set { if (enableTTS != value) { this.SetProperty(ref enableTTS, value); UnCommitedChanges = true; } }
        }

        private int voiceSelectedIndex = 0;
        internal int VoiceSelectedIndex
        {
            get { return voiceSelectedIndex; }
            set { if (voiceSelectedIndex != value) { this.SetProperty(ref voiceSelectedIndex, value); UnCommitedChanges = true; } }
        }

        internal ObservableCollection<ComboBoxItem> VoiceItems { get; set; } = new ObservableCollection<ComboBoxItem>();

        private bool ttsTechroom = true;
        internal bool TtsTechroom
        {
            get { return ttsTechroom; }
            set { if (ttsTechroom != value) { this.SetProperty(ref ttsTechroom, value); UnCommitedChanges = true; } }
        }

        private bool ttsBriefings = true;
        internal bool TtsBriefings
        {
            get { return ttsBriefings; }
            set { if (ttsBriefings != value) { this.SetProperty(ref ttsBriefings, value); UnCommitedChanges = true; } }
        }

        private bool ttsIngame = true;
        internal bool TtsIngame
        {
            get { return ttsIngame; }
            set { if (ttsIngame != value) { this.SetProperty(ref ttsIngame, value); UnCommitedChanges = true; } }
        }

        private bool ttsMulti = true;
        internal bool TtsMulti
        {
            get { return ttsMulti; }
            set { if (ttsMulti != value) { this.SetProperty(ref ttsMulti, value); UnCommitedChanges = true; } }
        }

        private bool ttsDescription = true;
        internal bool TtsDescription
        {
            get { return ttsDescription; }
            set { if (ttsDescription != value) { this.SetProperty(ref ttsDescription, value); UnCommitedChanges = true; } }
        }

        private int ttsVolume = 100;
        internal int TtsVolume
        {
            get { return ttsVolume; }
            set { if (ttsVolume != value) { this.SetProperty(ref ttsVolume, value); UnCommitedChanges = true; } }
        }

        private bool playingTTS = false;
        internal bool PlayingTTS
        {
            get { return playingTTS; }
            set { if (playingTTS != value) { this.SetProperty(ref playingTTS, value); UnCommitedChanges = true; } }
        }

        /*JOYSTICK*/
        internal ObservableCollection<ComboBoxItem> Joystick1Items { get; set; } = new ObservableCollection<ComboBoxItem>();
        internal ObservableCollection<ComboBoxItem> Joystick2Items { get; set; } = new ObservableCollection<ComboBoxItem>();
        internal ObservableCollection<ComboBoxItem> Joystick3Items { get; set; } = new ObservableCollection<ComboBoxItem>();
        internal ObservableCollection<ComboBoxItem> Joystick4Items { get; set; } = new ObservableCollection<ComboBoxItem>();

        private int joy1SelectedIndex = -1;
        internal int Joy1SelectedIndex
        {
            get { return joy1SelectedIndex; }
            set { if (joy1SelectedIndex != value) { this.SetProperty(ref joy1SelectedIndex, value); UnCommitedChanges = true; } }
        }

        private int joy2SelectedIndex = -1;
        internal int Joy2SelectedIndex
        {
            get { return joy2SelectedIndex; }
            set { if (joy2SelectedIndex != value) { this.SetProperty(ref joy2SelectedIndex, value); UnCommitedChanges = true; } }
        }

        private int joy3SelectedIndex = -1;
        internal int Joy3SelectedIndex
        {
            get { return joy3SelectedIndex; }
            set { if (joy3SelectedIndex != value) { this.SetProperty(ref joy3SelectedIndex, value); UnCommitedChanges = true; } }
        }

        private int joy4SelectedIndex = -1;
        internal int Joy4SelectedIndex
        {
            get { return joy4SelectedIndex; }
            set { if (joy4SelectedIndex != value) { this.SetProperty(ref joy4SelectedIndex, value); UnCommitedChanges = true; } }
        }


        /* MOD / FS2 */


        private int fs2LangSelectedIndex = 0; 
        internal int Fs2LangSelectedIndex
        {
            get { return fs2LangSelectedIndex; }
            set { if (fs2LangSelectedIndex != value) { this.SetProperty(ref fs2LangSelectedIndex, value); UnCommitedChanges = true; } }
        }

        private uint multiPort = 7808;
        internal uint MultiPort
        {
            get { return multiPort; }
            set { if (multiPort != value) { this.SetProperty(ref multiPort, value); UnCommitedChanges = true; } }
        }

        private uint mouseSensitivity = 5;
        internal uint MouseSensitivity
        {
            get { return mouseSensitivity; }
            set { if (mouseSensitivity != value) { this.SetProperty(ref mouseSensitivity, value); UnCommitedChanges = true; } }
        }

        private uint joystickSensitivity = 9;
        internal uint JoystickSensitivity
        {
            get { return joystickSensitivity; }
            set { if (joystickSensitivity != value) { this.SetProperty(ref joystickSensitivity, value); UnCommitedChanges = true; } }
        }

        private uint joystickDeadZone = 10;
        internal uint JoystickDeadZone
        {
            get { return joystickDeadZone; }
            set { if (joystickDeadZone != value) { this.SetProperty(ref joystickDeadZone, value); UnCommitedChanges = true; } }
        }


        /* DEVELOPER */
        private bool noSystemCMD = false;
        internal bool NoSystemCMD
        {
            get { return noSystemCMD; }
            set { if (noSystemCMD != value) { this.SetProperty(ref noSystemCMD, value); UnCommitedChanges = true; } }
        }

        private string prefixCMD = string.Empty;
        internal string PrefixCMD
        {
            get { return prefixCMD; }
            set { if (prefixCMD != value) { this.SetProperty(ref prefixCMD, value); UnCommitedChanges = true; } }
        }

        private string envVars = string.Empty;
        internal string EnvVars
        {
            get { return envVars; }
            set { if (envVars != value) { this.SetProperty(ref envVars, value); UnCommitedChanges = true; } }
        }

        /* MISC */
        private bool portableFsoPreferences = true;
        internal bool PortableFsoPreferences
        {
            get { return portableFsoPreferences; }
            set { if (portableFsoPreferences != value) { this.SetProperty(ref portableFsoPreferences, value); UnCommitedChanges = true; } }
        }

        internal string globalCmd = string.Empty;
        // In order to have hidden dev options, we need a setter for globalCMD
        public string GlobalCmd
        {
            get
            {
                return globalCmd;
            }
            set
            {

                if (value.Contains("freespace2.com") && !ShowDevOptions){   
                    ToggleDeveloperOptions();
                }

                SetProperty(ref globalCmd, value.Replace("freespace2.com", ""));
                UnCommitedChanges = true;
            }
        }

        public GlobalSettingsViewModel()
        {
            isPortableMode = Knossos.inPortableMode;
            knossosVersion = Knossos.AppVersion;
            if (!KnUtils.IsModernOS())
            {
                KnossosUpdateChannelInfo = "(Legacy Update Channel)";
                var current = KnUtils.IsWindows ? $"Windows {Environment.OSVersion.Version.Major}" : $"MacOS {Environment.OSVersion.Version}";
                var minimum = KnUtils.IsWindows ? "Windows 10+" : "MacOS 12+";
                KnossosUpdateTooltip = $"Your version of {current} is not compatible with version 1.4 of Knossos.NET and above. To receive new features, you should upgrade to version {minimum}. You will still receive bugfix updates when they apply to version 1.3.";
            }
        }

        public void CheckDisplaySettingsWarning()
        {
            if(Knossos.inSingleTCMode)
            {
                DisplaySettingsWarning = true;
                if(CustomLauncher.CustomCmdlineArray != null && CustomLauncher.CustomCmdlineArray.FirstOrDefault(x => x.ToLower() == "no_ingame_options") != null )
                {
                    DisplaySettingsWarning = false;
                    return;
                }
                var res = MainViewModel.Instance?.CustomHomeVM?.ActiveVersionHasCmdline("no_ingame_options");
                if (res.HasValue) 
                {
                    DisplaySettingsWarning = !res.Value;
                }
            }
        }

        /// <summary>
        /// Check if the settings where changed from the ones in the GlobalSettings instance compared to the ones here
        /// If they did change update data stored in GlobalSettings and save file
        /// </summary>
        public void CommitPendingChanges()
        {
            if(UnCommitedChanges)
            {
                SaveCommand();
            }
        }

        internal async void UpdateBasePathAndroid()
        {
            try
            {
                if (AndroidFolderPathsSelectedIndex == -1 || AndroidFolderPaths.Count() - 1 < AndroidFolderPathsSelectedIndex)
                    return;
                var path = AndroidFolderPaths[AndroidFolderPathsSelectedIndex];
                if (path == BasePath)
                    return;
                // Test if we can write to the new library directory
                Directory.CreateDirectory(path);
                using (StreamWriter writer = new StreamWriter(path + Path.DirectorySeparatorChar + "test.txt"))
                {
                    writer.WriteLine("test");
                }
                File.Delete(Path.Combine(path + Path.DirectorySeparatorChar + "test.txt"));

                Knossos.globalSettings.basePath = path;
                Knossos.globalSettings.Save();
                Knossos.ResetBasePath();
                await LoadDataAsync();
            }
            catch (Exception ex) 
            {
                Log.Add(Log.LogSeverity.Error, "GlobalSettingsViewModel.UpdateBasePathAndroid()", "Index " + AndroidFolderPathsSelectedIndex + " Collection "+AndroidFolderPaths.Count());
                Log.Add(Log.LogSeverity.Error, "GlobalSettingsViewModel.UpdateBasePathAndroid()", ex);
            }
        }

        /// <summary>
        /// Loads data from the GlobalSettings.cs class into this one to display it in the UI
        /// Also loads flag data from a FSO build, if one is installed
        /// </summary>
        public async Task LoadDataAsync()
        {
            var old_path = KnUtils.GetFSODataFolderPath();
            // Fetch flag data on a background thread so the UI thread stays responsive.
            // await resumes on Avalonia's UI synchronization context, so all ObservableProperty
            // assignments below are safe.
            var flagData = await Task.Run(GetFlagDataAsync);

            // reset the ini info if we have gotten an updated preferred path from FSO.
            if (old_path != KnUtils.GetFSODataFolderPath()){
                Knossos.globalSettings.Load();
            }
            /* Knossos Settings */
            if (Knossos.globalSettings.basePath != null)
            {
                BasePath = Knossos.globalSettings.basePath;
            }
            /* Android Folder Paths */
            if (KnUtils.IsAndroid)
            {
                AndroidFolderPaths.Clear();
                AndroidFolderPaths.Add(BasePath);
                AndroidFolderPathsSelectedIndex = 0;
                foreach (var path in AndroidHelper.GetAllExternalAppFilesDirs())
                {
                    if (!AndroidFolderPaths.Contains(path))
                    { AndroidFolderPaths.Add(path); }
                }
                AndroidFolderPathsSelectedIndex = 0;
            }

            EnableLogFile = Knossos.globalSettings.enableLogFile;
            LogLevel= Knossos.globalSettings.logLevel;
            Fs2RootPack = Knossos.retailFs2RootFound;
            NumberOfMods = Knossos.GetInstalledModList(null).Count.ToString();
            QuickStartButtonVisibility = CustomLauncher.IsCustomMode ? CustomLauncher.SettingsDisplayQuickStartButton : true;
            NumberOfBuilds = Knossos.GetInstalledBuildsList(null).Count.ToString();
            if(KnUtils.IsWindows)
            {
                DetectedOS = "Windows";
                WindowsOS = true;
            }
            else
            {
                if(KnUtils.IsLinux)
                {
                    DetectedOS = "Linux";
                }
                else
                {
                    if(KnUtils.IsMacOS)
                    {
                        DetectedOS = "OSX";
                    }
                    else
                    {
                        if (KnUtils.IsAndroid)
                            DetectedOS = "Android";
                    }
                }
            }

            CpuArch = KnUtils.CpuArch;
            IsAVX = KnUtils.CpuAVX;
            IsAVX2 = KnUtils.CpuAVX2;
            ForceSSE2 = Knossos.globalSettings.forceSSE2;
            MaxConcurrentSubtasks = Knossos.globalSettings.maxConcurrentSubtasks - 1;
            switch(Knossos.globalSettings.maxDownloadSpeed)
            {
                case speedUnlimited: MaxDownloadSpeedIndex = 0; break;
                case speedHalfMB: MaxDownloadSpeedIndex = 1; break;
                case speed1MB: MaxDownloadSpeedIndex = 2; break;
                case speed2MB: MaxDownloadSpeedIndex = 3; break;
                case speed3MB: MaxDownloadSpeedIndex = 4; break;
                case speed4MB: MaxDownloadSpeedIndex = 5; break;
                case speed5MB: MaxDownloadSpeedIndex = 6; break;
                case speed6MB: MaxDownloadSpeedIndex = 7; break;
                case speed7MB: MaxDownloadSpeedIndex = 8; break;
                case speed8MB: MaxDownloadSpeedIndex = 9; break;
                case speed9MB: MaxDownloadSpeedIndex = 10; break;
                case speed10MB: MaxDownloadSpeedIndex = 11; break;
                default: MaxDownloadSpeedIndex = 0; break;
            }

            BlDlNebula = false;
            BlCfNebula = false;
            BlHestia = false;
            if (Knossos.globalSettings.mirrorBlacklist != null)
            {
                if (Knossos.globalSettings.mirrorBlacklist.Contains("dl.fsnebula.org"))
                {
                    BlDlNebula = true;
                }
                if (Knossos.globalSettings.mirrorBlacklist.Contains("cf.fsnebula.org"))
                {
                    BlCfNebula = true;
                }
                if (Knossos.globalSettings.mirrorBlacklist.Contains("hestia.feralhosting.com"))
                {
                    BlHestia = true;
                }
            }

            ModCompression = Knossos.globalSettings.modCompression;
            CompressionMaxParallelism = Knossos.globalSettings.compressionMaxParallelism;
            CheckUpdates = Knossos.globalSettings.checkUpdate;
            AutoUpdate = Knossos.globalSettings.autoUpdate;
            DeleteUploadedFiles = Knossos.globalSettings.deleteUploadedFiles;
            UpdateNightly = Knossos.globalSettings.autoUpdateBuilds.UpdateNightly;
            UpdateRC = Knossos.globalSettings.autoUpdateBuilds.UpdateRC;
            UpdateStable = Knossos.globalSettings.autoUpdateBuilds.UpdateStable;
            DeleteOlder = Knossos.globalSettings.autoUpdateBuilds.DeleteOlder;
            NoSystemCMD = Knossos.globalSettings.noSystemCMD;
            PrefixCMD = Knossos.globalSettings.prefixCMD;
            EnvVars = Knossos.globalSettings.envVars;
            ShowDevOptions = Knossos.globalSettings.showDevOptions || NoSystemCMD;
            CloseToTray = Knossos.globalSettings.closeToTray;
            AntiStuck = Knossos.globalSettings.antiStuck;
            MaxUploadsRetries = Knossos.globalSettings.maxUploadRetries;
            SingleView = Knossos.globalSettings.singleViewMode;

            /* VIDEO SETTINGS */
            //RESOLUTION
            ResolutionItems.Clear();
            if (flagData != null && flagData.displays != null)
            {
                foreach(var display in flagData.displays)
                {
                    var item = new ComboBoxItem();
                    item.Content = display.name;
                    item.IsEnabled = false;
                    item.Tag = 0;
                    ResolutionItems.Add(item);
                    if(display.modes != null)
                    {
                        foreach(var mode in display.modes)
                        {
                            var itemMode = new ComboBoxItem();
                            itemMode.Content = mode.x+"x"+mode.y;
                            itemMode.Tag = display.index;
                            ResolutionItems.Add(itemMode);
                            if(mode.bits == 16)
                            {
                                Enable16BitColor = true;
                            }
                        }
                    }

                    if(Knossos.globalSettings.displayResolution == null)
                    {
                        Knossos.globalSettings.displayResolution = display.width + "x" + display.height;
                    }
                }
            }
            var resoItem = ResolutionItems.FirstOrDefault(i => i.Content?.ToString() == Knossos.globalSettings.displayResolution && i.Tag?.ToString() == Knossos.globalSettings.displayIndex.ToString());
            if (resoItem != null)
            {
                var index = ResolutionItems.IndexOf(resoItem);
                if(index != -1)
                {
                    ResolutionSelectedIndex = index;
                }
            }
            else
            {
                ResolutionSelectedIndex = 0;
            }
            //COLOR DEPTH
            if(Knossos.globalSettings.displayColorDepth == 32)
            {
                BitsSelectedIndex = 0;
            }
            else
            {
                BitsSelectedIndex = 1;
            }
            //Texture Filter
            TextureSelectedIndex = Knossos.globalSettings.textureFilter;
            //Shadows
            ShadowQualitySelectedIndex = Knossos.globalSettings.shadowQuality;
            //AA
            AaSelectedIndex = Knossos.globalSettings.aaPreset;
            //MSAA
            MsaaSelectedIndex = Knossos.globalSettings.msaaPreset;
            //SoftParticles
            EnableSoftParticles = Knossos.globalSettings.enableSoftParticles;
            //DeferredLighting
            EnableDeferredLighting = Knossos.globalSettings.enableDeferredLighting;
            //WindowMode
            WindowMode = Knossos.globalSettings.windowMode;
            //VSYNC
            Vsync = Knossos.globalSettings.vsync;
            //No Post Process
            PostProcess = Knossos.globalSettings.postProcess;
            //No FPS Cap
            NoFpsCapping = Knossos.globalSettings.noFpsCapping;
            //FPS
            ShowFps = Knossos.globalSettings.showFps;

            /* AUDIO SETTINGS */
            //Playback Devices
            PlaybackItems.Clear();
            if (flagData != null && flagData.openal != null && flagData.openal.playback_devices != null)
            {
                foreach (var playback in flagData.openal.playback_devices)
                {
                    var item = new ComboBoxItem();
                    item.Content = playback;
                    item.Tag = playback;
                    PlaybackItems.Add(item);

                    if (Knossos.globalSettings.playbackDevice == null)
                    {
                        Knossos.globalSettings.playbackDevice = flagData.openal.default_playback;
                    }
                }
            }
            var pbItem = PlaybackItems.FirstOrDefault(i => i.Tag?.ToString() == Knossos.globalSettings.playbackDevice);
            if (pbItem != null)
            {
                var index = PlaybackItems.IndexOf(pbItem);
                if (index != -1)
                {
                    PlaybackSelectedIndex = index;
                }
            }
            else
            {
                PlaybackSelectedIndex = 0;
            }
            //Capture Devices
            CaptureItems.Clear();
            if (flagData != null && flagData.openal != null && flagData.openal.capture_devices != null)
            {
                foreach (var capture in flagData.openal.capture_devices)
                {
                    var item = new ComboBoxItem();
                    item.Content = capture;
                    item.Tag = capture;
                    CaptureItems.Add(item);

                    if (Knossos.globalSettings.captureDevice == null)
                    {
                        Knossos.globalSettings.captureDevice = flagData.openal.default_capture;
                    }
                }
            }
            var ctItem = CaptureItems.FirstOrDefault(i => i.Tag?.ToString() == Knossos.globalSettings.captureDevice);
            if (ctItem != null)
            {
                var index = CaptureItems.IndexOf(ctItem);
                if (index != -1)
                {
                    CaptureSelectedIndex = index;
                }
            }
            else
            {
                CaptureSelectedIndex = 0;
            }
            //Sample Rate
            switch (Knossos.globalSettings.sampleRate)
            {
                case 44100: SampleRateSelectedIndex = 0; break;
                case 48000: SampleRateSelectedIndex = 1; break;
                case 96000: SampleRateSelectedIndex = 2; break;
                case 192000: SampleRateSelectedIndex = 3; break;
                default: SampleRateSelectedIndex = 0; break;
            }
            //Enable EFX
            EnableEFX = Knossos.globalSettings.enableEfx;
            //Disable Audio
            DisableAudio = Knossos.globalSettings.disableAudio;
            //Disable Music
            DisableAudio = Knossos.globalSettings.disableMusic;
            //TTS Settings
            EnableTTS = Knossos.globalSettings.enableTts;
            TtsBriefings = Knossos.globalSettings.ttsBriefings;
            TtsTechroom = Knossos.globalSettings.ttsTechroom;
            TtsIngame = Knossos.globalSettings.ttsIngame;
            TtsMulti = Knossos.globalSettings.ttsMulti;
            TtsVolume = Knossos.globalSettings.ttsVolume;
            TtsDescription = Knossos.globalSettings.ttsDescription;
            VoiceItems.Clear();
            if (flagData != null && flagData.voices != null)
            {
                foreach (var voice in flagData.voices)
                {
                    var item = new ComboBoxItem();
                    item.Content = voice;
                    item.Tag = voice;
                    VoiceItems.Add(item);
                }
            }
            if (Knossos.globalSettings.ttsVoice == null)
            {
                Knossos.globalSettings.ttsVoice = 0;
            }
            else
            {
                if(Knossos.globalSettings.ttsVoice.Value + 1 <= VoiceItems.Count)
                {
                    VoiceSelectedIndex = Knossos.globalSettings.ttsVoice.Value;
                }
            }

            //Joysticks
            //The reason for this BS is that i cant re-use comboBox items in multiple controls
            Joystick1Items.Clear();
            Joystick2Items.Clear();
            Joystick3Items.Clear();
            Joystick4Items.Clear();
            Joy1SelectedIndex = -1;
            Joy2SelectedIndex = -1;
            Joy3SelectedIndex = -1;
            Joy4SelectedIndex = -1;
            var noJoyItem = new ComboBoxItem();
            noJoyItem.Content = "No Joystick";
            noJoyItem.Tag = null;
            Joystick1Items.Add(noJoyItem);
            var noJoy2Item = new ComboBoxItem();
            noJoy2Item.Content = "No Joystick";
            noJoy2Item.Tag = null;
            Joystick2Items.Add(noJoy2Item);
            var noJoy3Item = new ComboBoxItem();
            noJoy3Item.Content = "No Joystick";
            noJoy3Item.Tag = null;
            Joystick3Items.Add(noJoy3Item);
            var noJoy4Item = new ComboBoxItem();
            noJoy4Item.Content = "No Joystick";
            noJoy4Item.Tag = null;
            Joystick4Items.Add(noJoy4Item);
            Joy1SelectedIndex = 0;
            Joy2SelectedIndex = 0;
            Joy3SelectedIndex = 0;
            Joy4SelectedIndex = 0;

            if (flagData != null && flagData.joysticks != null)
            {
                foreach (var joy in flagData.joysticks)
                {
                    var item = new ComboBoxItem();
                    item.Content = joy.name + " - ID: " + joy.id + "\nGUID: " + joy.guid;
                    item.Tag = joy;
                    var item2 = new ComboBoxItem();
                    item2.Content = joy.name + " - ID: " + joy.id + "\nGUID: " + joy.guid;
                    item2.Tag = joy;
                    var item3 = new ComboBoxItem();
                    item3.Content = joy.name + " - ID: " + joy.id + "\nGUID: " + joy.guid;
                    item3.Tag = joy;
                    var item4 = new ComboBoxItem();
                    item4.Content = joy.name + " - ID: " + joy.id + "\nGUID: " + joy.guid;
                    item4.Tag = joy;
                    Joystick1Items.Add(item);
                    Joystick2Items.Add(item2);
                    Joystick3Items.Add(item3);
                    Joystick4Items.Add(item4);
                }

                // i hate this
                if(Knossos.globalSettings.joystick1 != null)
                {
                    bool found = false;
                    foreach(var item in Joystick1Items)
                    {
                        if(item.Tag != null)
                        {
                            var joystick = (Joystick)item.Tag;
                            if (joystick.guid == Knossos.globalSettings.joystick1.guid && joystick.id == Knossos.globalSettings.joystick1.id)
                            {
                                var index = Joystick1Items.IndexOf(item);
                                if (index != -1)
                                {
                                    Joy1SelectedIndex = index;
                                    found = true;
                                }
                            }
                        }
                    }

                    if(!found)
                    {
                        var missingItem = new ComboBoxItem();
                        missingItem.Content = "(Missing) " + Knossos.globalSettings.joystick1.name + " ID: " + Knossos.globalSettings.joystick1.id + "\nGUID: " + Knossos.globalSettings.joystick1.guid;
                        missingItem.Tag = Knossos.globalSettings.joystick1;
                        Joystick1Items.Add(missingItem);
                        var index = Joystick1Items.IndexOf(missingItem);
                        if (index != -1)
                        {
                            Joy1SelectedIndex = index;
                        }
                    }
                }
                if (Knossos.globalSettings.joystick2 != null)
                {
                    bool found = false;
                    foreach (var item in Joystick2Items)
                    {
                        if (item.Tag != null)
                        {
                            var joystick = (Joystick)item.Tag;
                            if (joystick.guid == Knossos.globalSettings.joystick2.guid && joystick.id == Knossos.globalSettings.joystick2.id)
                            {
                                var index = Joystick2Items.IndexOf(item);
                                if (index != -1)
                                {
                                    Joy2SelectedIndex = index;
                                    found = true;
                                }
                            }
                        }
                    }

                    if (!found)
                    {
                        var missingItem = new ComboBoxItem();
                        missingItem.Content = "(Missing) " + Knossos.globalSettings.joystick2.name + " ID: " + Knossos.globalSettings.joystick2.id + "\nGUID: " + Knossos.globalSettings.joystick2.guid;
                        missingItem.Tag = Knossos.globalSettings.joystick2;
                        Joystick2Items.Add(missingItem);
                        var index = Joystick2Items.IndexOf(missingItem);
                        if (index != -1)
                        {
                            Joy2SelectedIndex = index;
                        }
                    }
                }
                if (Knossos.globalSettings.joystick3 != null)
                {
                    bool found = false;
                    foreach (var item in Joystick3Items)
                    {
                        if (item.Tag != null)
                        {
                            var joystick = (Joystick)item.Tag;
                            if (joystick.guid == Knossos.globalSettings.joystick3.guid && joystick.id == Knossos.globalSettings.joystick3.id)
                            {
                                var index = Joystick3Items.IndexOf(item);
                                if (index != -1)
                                {
                                    Joy3SelectedIndex = index;
                                    found = true;
                                }
                            }
                        }
                    }

                    if (!found)
                    {
                        var missingItem = new ComboBoxItem();
                        missingItem.Content = "(Missing) " + Knossos.globalSettings.joystick3.name + " ID: " + Knossos.globalSettings.joystick3.id + "\nGUID: " + Knossos.globalSettings.joystick3.guid;
                        missingItem.Tag = Knossos.globalSettings.joystick3;
                        Joystick3Items.Add(missingItem);
                        var index = Joystick3Items.IndexOf(missingItem);
                        if (index != -1)
                        {
                            Joy3SelectedIndex = index;
                        }
                    }
                }
                if (Knossos.globalSettings.joystick4 != null)
                {
                    bool found = false;
                    foreach (var item in Joystick4Items)
                    {
                        if (item.Tag != null)
                        {
                            var joystick = (Joystick)item.Tag;
                            if (joystick.guid == Knossos.globalSettings.joystick4.guid && joystick.id == Knossos.globalSettings.joystick4.id)
                            {
                                var index = Joystick4Items.IndexOf(item);
                                if (index != -1)
                                {
                                    Joy4SelectedIndex = index;
                                    found = true;
                                }
                            }
                        }
                    }

                    if (!found)
                    {
                        var missingItem = new ComboBoxItem();
                        missingItem.Content = "(Missing) " + Knossos.globalSettings.joystick4.name + " ID: " + Knossos.globalSettings.joystick4.id + "\nGUID: " + Knossos.globalSettings.joystick4.guid;
                        missingItem.Tag = Knossos.globalSettings.joystick4;
                        Joystick4Items.Add(missingItem);
                        var index = Joystick4Items.IndexOf(missingItem);
                        if (index != -1)
                        {
                            Joy4SelectedIndex = index;
                        }
                    }
                }
            }

            JoystickDeadZone = Knossos.globalSettings.joystickDeadZone;
            JoystickSensitivity = Knossos.globalSettings.joystickSensitivity;
            MouseSensitivity = Knossos.globalSettings.mouseSensitivity;

            /* MOD SETTINGS */

            //GLOBAL CMD
            if(Knossos.globalSettings.globalCmdLine != null)
            {
                GlobalCmd = Knossos.globalSettings.globalCmdLine;
            }

            //FS2 Lang
            switch(Knossos.globalSettings.fs2Lang)
            {
                case "English": Fs2LangSelectedIndex = 0; break;
                case "German": Fs2LangSelectedIndex = 1; break;
                case "French": Fs2LangSelectedIndex = 2; break;
                case "Polish": Fs2LangSelectedIndex = 3; break;
                default: Fs2LangSelectedIndex = 0;  break;
            }

            //Multi Port
            MultiPort = Knossos.globalSettings.multiPort;

            //MISC
            PortableFsoPreferences = Knossos.globalSettings.portableFsoPreferences;

            UnCommitedChanges = false;
        }

        private async Task<FlagsJsonV1?> GetFlagDataAsync()
        {
            FlagDataLoaded = false;
            if (KnUtils.IsAndroid)
                return null;
            var builds = Knossos.GetInstalledBuildsList();
            if (builds.Any())
            {
                //First the stable ones
                var stables = builds.Where(b => b.stability == FsoStability.Stable).ToList();
                if (stables.Any())
                {
                    stables.Sort(FsoBuild.CompareVersion);
                    foreach (var stable in stables)
                    {
                        var flags = await stable.GetFlagsV1Async().ConfigureAwait(false);
                        if (flags != null)
                        {
                            FlagDataLoaded = true;
                            Knossos.flagDataLoaded = true;
                            KnUtils.SetFSODataFolderPath(flags.pref_path);
                            return flags;
                        }
                    }
                }

                //If we are still here try all others
                var others = builds.Where(b => b.stability != FsoStability.Stable);
                if (others.Any())
                {
                    foreach (var other in others)
                    {
                        var flags = await other.GetFlagsV1Async().ConfigureAwait(false);
                        if (flags != null)
                        {
                            FlagDataLoaded = true;
                            Knossos.flagDataLoaded = true;
                            KnUtils.SetFSODataFolderPath(flags.pref_path);
                            return flags;
                        }
                    }
                }
            }

            Log.Add(Log.LogSeverity.Warning, "GlobalSettingsViewModel.GetFlagData()", "Unable to find a valid build to get flag data for global settings.");
            return null;
        }

        /* UI Buttons */
        /// <summary>
        /// Changes the knossos library path, reloads settings and nebula repo
        /// </summary>
        internal async void BrowseFolderCommand()
        {
            FolderPickerOpenOptions options = new FolderPickerOpenOptions(); 
            if (BasePath != string.Empty)
            {
                try
                {
                    options.SuggestedStartLocation = await KnUtils.GetTopLevel().StorageProvider.TryGetFolderFromPathAsync(BasePath);
                }
                catch { }
            }
            options.AllowMultiple = false;

            var result = await KnUtils.GetTopLevel().StorageProvider.OpenFolderPickerAsync(options);

            try 
            {
                if (result != null && result.Count > 0)
                {
                      // Test if we can write to the new library directory
                      using (StreamWriter writer = new StreamWriter(result[0].Path.LocalPath.ToString() + Path.DirectorySeparatorChar + "test.txt"))
                      {
                          writer.WriteLine("test");
                      }
                      File.Delete(Path.Combine(result[0].Path.LocalPath.ToString() + Path.DirectorySeparatorChar + "test.txt"));

                      Knossos.globalSettings.basePath = result[0].Path.LocalPath.ToString();
                      Knossos.globalSettings.Save();
                      Knossos.ResetBasePath();
                      await LoadDataAsync();
                } 
            } 
            catch (Exception ex) 
            {
                Log.Add(Log.LogSeverity.Error, "GlobalSettings.BrowseFolderCommand() - test read/write was not successful: ", ex);
                await Dispatcher.UIThread.Invoke(async () => {
                    await MessageBox.Show(null, "KnossosNET was not able to write to this folder.  Please select another library folder.", "Cannot Select Folder", MessageBox.MessageBoxButtons.OK);
                }).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reload data from json
        /// </summary>
        internal async void ResetCommand()
        {
            var pxoUser = Knossos.globalSettings.pxoLogin;
            var pxoPassword = Knossos.globalSettings.pxoPassword;
            Knossos.globalSettings = new GlobalSettings();
            await LoadDataAsync();
            Knossos.globalSettings.pxoPassword = pxoPassword;
            Knossos.globalSettings.pxoLogin = pxoUser;
            SaveCommand();
        }

        /// <summary>
        /// Copies data from the UI into the GlobalSettings.cs class and saves it into the json
        /// </summary>
        internal void SaveCommand()
        {
            /* Knossos Settings */
            if (BasePath != string.Empty)
            {
                Knossos.globalSettings.basePath = BasePath;
            }
            Knossos.globalSettings.enableLogFile = EnableLogFile;
            Knossos.globalSettings.logLevel = LogLevel;
            Knossos.globalSettings.forceSSE2 = ForceSSE2;
            Knossos.globalSettings.maxConcurrentSubtasks = MaxConcurrentSubtasks+1;
            switch (MaxDownloadSpeedIndex)
            {
                case 0: Knossos.globalSettings.maxDownloadSpeed = speedUnlimited; break;
                case 1: Knossos.globalSettings.maxDownloadSpeed = speedHalfMB; break;
                case 2: Knossos.globalSettings.maxDownloadSpeed = speed1MB; break;
                case 3: Knossos.globalSettings.maxDownloadSpeed = speed2MB; break;
                case 4: Knossos.globalSettings.maxDownloadSpeed = speed3MB; break;
                case 5: Knossos.globalSettings.maxDownloadSpeed = speed4MB; break;
                case 6: Knossos.globalSettings.maxDownloadSpeed = speed5MB; break;
                case 7: Knossos.globalSettings.maxDownloadSpeed = speed6MB; break;
                case 8: Knossos.globalSettings.maxDownloadSpeed = speed7MB; break;
                case 9: Knossos.globalSettings.maxDownloadSpeed = speed8MB; break;
                case 10: Knossos.globalSettings.maxDownloadSpeed = speed9MB; break;
                case 11: Knossos.globalSettings.maxDownloadSpeed = speed10MB; break;
            }

            List<string> blMirrors = new List<string>();
            if (BlDlNebula)
            {
                blMirrors.Add("dl.fsnebula.org");
            }
            if (BlCfNebula)
            {
                blMirrors.Add("cf.fsnebula.org");
            }
            if (BlHestia)
            {
                blMirrors.Add("hestia.feralhosting.com");
            }
            if (blMirrors.Any() && blMirrors.Count() != 3 /*Invalid!*/)
            {
                Knossos.globalSettings.mirrorBlacklist = blMirrors.ToArray();
            }
            else
            {
                Knossos.globalSettings.mirrorBlacklist = null;
                BlDlNebula = false;
                BlCfNebula = false;
                BlHestia = false;
            }

            Knossos.globalSettings.modCompression = ModCompression;
            Knossos.globalSettings.compressionMaxParallelism = CompressionMaxParallelism;
            Knossos.globalSettings.checkUpdate = CheckUpdates;
            Knossos.globalSettings.deleteUploadedFiles = DeleteUploadedFiles;
            if(!CheckUpdates)
            {
                AutoUpdate = false;
            }
            Knossos.globalSettings.autoUpdate = AutoUpdate;
            Knossos.globalSettings.autoUpdateBuilds = new GlobalSettings.AutoUpdateFsoBuilds(UpdateStable, UpdateRC, UpdateNightly, DeleteOlder);
            Knossos.globalSettings.noSystemCMD = NoSystemCMD;
            Knossos.globalSettings.prefixCMD = PrefixCMD;
            Knossos.globalSettings.envVars = EnvVars;
            Knossos.globalSettings.showDevOptions = ShowDevOptions;
            Knossos.globalSettings.closeToTray = CloseToTray;
            Knossos.globalSettings.antiStuck = AntiStuck;
            Knossos.globalSettings.maxUploadRetries = MaxUploadsRetries;
            Knossos.globalSettings.singleViewMode = SingleView;

            /* VIDEO */
            //Resolution
            if (ResolutionSelectedIndex + 1 <= ResolutionItems.Count)
            {
                Knossos.globalSettings.displayResolution = ResolutionItems[ResolutionSelectedIndex].Content?.ToString();
                var displayIndex = ResolutionItems[ResolutionSelectedIndex].Tag;
                if ( displayIndex != null)
                { 
                    Knossos.globalSettings.displayIndex = (int)displayIndex;
                }
            }
            //Color Depth
            if(BitsSelectedIndex == 0)
            {
                Knossos.globalSettings.displayColorDepth = 32;
            }
            else
            {
                Knossos.globalSettings.displayColorDepth = 16;
            }
            //Texture Filter
            Knossos.globalSettings.textureFilter = TextureSelectedIndex;
            //Shadows
            Knossos.globalSettings.shadowQuality = ShadowQualitySelectedIndex;
            //AA
            Knossos.globalSettings.aaPreset = AaSelectedIndex;
            //MSAA
            Knossos.globalSettings.msaaPreset = MsaaSelectedIndex;
            //SoftParticles
            Knossos.globalSettings.enableSoftParticles = EnableSoftParticles;
            //DeferredLighting
            Knossos.globalSettings.enableDeferredLighting = EnableDeferredLighting;
            //WindowMode
            Knossos.globalSettings.windowMode = WindowMode;
            //VSYNC
            Knossos.globalSettings.vsync = Vsync;
            //No Post Process
            Knossos.globalSettings.postProcess = PostProcess;
            //No FPS Cap
            Knossos.globalSettings.noFpsCapping = NoFpsCapping;
            //FPS
            Knossos.globalSettings.showFps = ShowFps;

            /* AUDIO SETTINGS */
            //Playback
            if (PlaybackSelectedIndex + 1 <= PlaybackItems.Count)
            {
                Knossos.globalSettings.playbackDevice = PlaybackItems[PlaybackSelectedIndex].Tag?.ToString();
            }
            //Capture
            if (CaptureSelectedIndex + 1 <= CaptureItems.Count)
            {
                Knossos.globalSettings.captureDevice = CaptureItems[CaptureSelectedIndex].Tag?.ToString();
            }
            //Sample Rate
            switch (SampleRateSelectedIndex)
            {
                case 0: Knossos.globalSettings.sampleRate = 44100; break;
                case 1: Knossos.globalSettings.sampleRate = 48000; break;
                case 2: Knossos.globalSettings.sampleRate = 96000; break;
                case 3: Knossos.globalSettings.sampleRate = 192000; break;
            }
            //Enable EFX
            Knossos.globalSettings.enableEfx = EnableEFX;
            //Disable Audio
            Knossos.globalSettings.disableAudio = DisableAudio;
            //Disable Music
            Knossos.globalSettings.disableMusic = DisableAudio;
            //TTS Settings
            Knossos.globalSettings.enableTts = EnableTTS;
            Knossos.globalSettings.ttsBriefings = TtsBriefings;
            Knossos.globalSettings.ttsTechroom = TtsTechroom;
            Knossos.globalSettings.ttsIngame = TtsIngame;
            Knossos.globalSettings.ttsMulti = TtsMulti;
            Knossos.globalSettings.ttsDescription = TtsDescription;
            Knossos.globalSettings.ttsVoice = VoiceSelectedIndex;
            Knossos.globalSettings.ttsVolume = TtsVolume;
            if (VoiceSelectedIndex >= 0 && VoiceItems.Count() > VoiceSelectedIndex && VoiceItems[VoiceSelectedIndex].Tag != null)
            {
                Knossos.globalSettings.ttsVoiceName = VoiceItems[VoiceSelectedIndex].Tag!.ToString();
            }

            /* JOYSTICKS */
            if (Joy1SelectedIndex + 1 <= Joystick1Items.Count && Joy1SelectedIndex != -1)
            {
                Knossos.globalSettings.joystick1 = (Joystick?)Joystick1Items[Joy1SelectedIndex].Tag;
            }
            else
            {
                Knossos.globalSettings.joystick1 = null;
            }
            if (Joy2SelectedIndex + 1 <= Joystick2Items.Count && Joy2SelectedIndex != -1 )
            {
                Knossos.globalSettings.joystick2 = (Joystick?)Joystick2Items[Joy2SelectedIndex].Tag;
            }
            else
            {
                Knossos.globalSettings.joystick2 = null;
            }
            if (Joy3SelectedIndex + 1 <= Joystick3Items.Count && Joy3SelectedIndex != -1)
            {
                Knossos.globalSettings.joystick3 = (Joystick?)Joystick3Items[Joy3SelectedIndex].Tag;
            }
            else
            {
                Knossos.globalSettings.joystick3 = null;
            }
            if (Joy4SelectedIndex + 1 <= Joystick4Items.Count && Joy4SelectedIndex != -1)
            {
                Knossos.globalSettings.joystick4 = (Joystick?)Joystick4Items[Joy4SelectedIndex].Tag;
            }
            else
            {
                Knossos.globalSettings.joystick4 = null;
            }

            Knossos.globalSettings.joystickDeadZone = JoystickDeadZone;
            Knossos.globalSettings.joystickSensitivity = JoystickSensitivity;
            Knossos.globalSettings.mouseSensitivity = MouseSensitivity;

            /* MOD SETTINGS */

            //GLOBAL CMD
            if (GlobalCmd.Trim().Length > 0)
            {
                Knossos.globalSettings.globalCmdLine = GlobalCmd;
            }
            else
            {
                Knossos.globalSettings.globalCmdLine = null;
            }

            //FS2 Lang
            switch (Fs2LangSelectedIndex)
            {
                case 0: Knossos.globalSettings.fs2Lang = "English"; break;
                case 1: Knossos.globalSettings.fs2Lang = "German"; break;
                case 2: Knossos.globalSettings.fs2Lang = "French"; break;
                case 3: Knossos.globalSettings.fs2Lang = "Polish"; break;
            }

            //Multi port
            Knossos.globalSettings.multiPort = MultiPort;

            //MISC
            Knossos.globalSettings.portableFsoPreferences = PortableFsoPreferences;

            Knossos.globalSettings.Save();
            UnCommitedChanges = false;
        }

        /// <summary>
        /// Start TTS Voice Test with selected voice
        /// </summary>
        internal void TestVoiceCommand()
        {
            if (VoiceSelectedIndex != -1)
            {
                string? voice_name = null;
                if (VoiceSelectedIndex >= 0 && VoiceItems.Count() > VoiceSelectedIndex && VoiceItems[VoiceSelectedIndex].Tag != null)
                {
                    voice_name = VoiceItems[VoiceSelectedIndex].Tag!.ToString();
                }
                PlayingTTS = true;
                Knossos.Tts("Developed in a joint operation by the Vasudan and Terran governments, the GTF Ulysses is an excellent all-around fighter. It offers superior maneuverability and a high top speed.", VoiceSelectedIndex, voice_name, TtsVolume, TTSCompletedCallback);
            }
        }

        /// <summary>
        /// Stop TTS
        /// </summary>
        internal void StopTTS()
        {
            Knossos.Tts(string.Empty);
        }

        /// <summary>
        /// When TTS test is over, change the button
        /// </summary>
        /// <returns></returns>
        private bool TTSCompletedCallback()
        {
            PlayingTTS = false;
            return true;
        }

        /// <summary>
        /// Opens the hard light wiki CMDline reference help
        /// </summary>
        internal void GlobalCmdHelp()
        {
            KnUtils.OpenBrowserURL("https://wiki.hard-light.net/index.php/Command-Line_Reference");
        }

        /// <summary>
        /// Reloads configuration and FSO flag data
        /// </summary>
        internal async void ReloadFlagData()
        {
            Knossos.globalSettings.Load();
            await LoadDataAsync();
        }

        /// <summary>
        /// Opens performance help window
        /// </summary>
        internal async void OpenPerformanceHelp()
        {
            var dialog = new PerformanceHelpView();
            await dialog.ShowDialog<PerformanceHelpView?>(MainWindow.instance);
        }

        /// <summary>
        /// Open the GetSapiVoices window
        /// </summary>
        internal async void OpenGetVoices()
        {
            if (MainWindow.instance != null)
            {
                var dialog = new AddSapiVoicesView();
                dialog.DataContext = new AddSapiVoicesViewModel();

                await dialog.ShowDialog<AddSapiVoicesView?>(MainWindow.instance);
            }
        }

        /// <summary>
        /// Open the retails FS2 installer window
        /// </summary>
        internal async void InstallFS2Command()
        {
            var dialog = new Fs2InstallerView();
            dialog.DataContext = new Fs2InstallerViewModel(dialog);
            await dialog.ShowDialog<Fs2InstallerView?>(MainWindow.instance);
        }

        /// <summary>
        /// Opens the quick setup guide window
        /// </summary>
        internal void QuickSetupCommand()
        {
            Knossos.OpenQuickSetup();
        }
        
        /// <summary>
        /// Opens the library cleaner window
        /// </summary>
        internal async void CleanupLibraryCommand()
        {
            var dialog = new CleanupKnossosLibraryView();

            await dialog.ShowDialog<CleanupKnossosLibraryView?>(MainWindow.instance);
        }

        /// <summary>
        /// Clears the knet image cache folder
        /// </summary>
        internal async void ClearImageCache()
        {
            await Task.Run(() => {
                try
                {
                    var path = KnUtils.GetCachePath();
                    Directory.Delete(path, true);
                    UpdateImgCacheSize();
                
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error,"GlobalSettingsViewModel.ClearImageCache()",ex);
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Update the size of the Knet image cache folder into UI
        /// </summary>
        public void UpdateImgCacheSize()
        {
            Task.Run(async () => {
                try
                {
                    var path = KnUtils.GetCachePath();
                    if (Directory.Exists(path))
                    {
                        var sizeInBytes = await KnUtils.GetSizeOfFolderInBytes(path).ConfigureAwait(false);
                        Dispatcher.UIThread.Invoke(()=>{ 
                            ImgCacheSize = KnUtils.FormatBytes(sizeInBytes);
                        });
                    }
                    else
                    {
                        Dispatcher.UIThread.Invoke(() => {
                            ImgCacheSize = "0 MB";
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "GlobalSettingsViewModel.ClearImageCache()", ex);
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Clears the knet image cache folder
        /// </summary>
        internal async void ClearFSOCache()
        {
            await Task.Run(() => {
                try
                {
                    var path = Path.Combine(KnUtils.GetFSODataFolderPath(), "data", "cache");
                    if (Directory.Exists(path))
                        Directory.Delete(path, true);
                    UpdateFSOCacheSize();

                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "GlobalSettingsViewModel.ClearFSOCache()", ex);
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Update the size of the Knet image cache folder into UI
        /// </summary>
        public void UpdateFSOCacheSize()
        {
            Task.Run(async () => {
                try
                {
                    var path = Path.Combine(KnUtils.GetFSODataFolderPath(),"data", "cache");
                    if (Directory.Exists(path))
                    {
                        var sizeInBytes = await KnUtils.GetSizeOfFolderInBytes(path).ConfigureAwait(false);
                        Dispatcher.UIThread.Invoke(() => {
                            FsoCacheSize = KnUtils.FormatBytes(sizeInBytes);
                        });
                    }
                    else
                    {
                        Dispatcher.UIThread.Invoke(() => {
                            FsoCacheSize = "0 MB";
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "GlobalSettingsViewModel.UpdateFSOCacheSize()", ex);
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Open Debug Filter Dialog
        /// </summary>
        internal async void OpenDebugFilterView()
        {
            var dialog = new Views.DebugFiltersView();
            dialog.DataContext = new DebugFiltersViewModel();
            await dialog.ShowDialog<DebugFiltersView?>(MainWindow.instance!);
        }

        internal void ToggleDeveloperOptions()
        {
            ShowDevOptions = !ShowDevOptions;

            // if we are turning off dev options, we need to actually restore to default
            if (!ShowDevOptions){
                NoSystemCMD = false;
            }
        }

        /// <summary>
        /// Expand Knossos settings section
        /// </summary>
        public void ExpandKnossosSection()
        {
            ExpandKnossosSettings = true;
        }

    }
}
