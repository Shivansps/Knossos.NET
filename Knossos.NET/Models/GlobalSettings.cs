
using Avalonia.Threading;
using IniParser;
using Knossos.NET.Classes;
using Knossos.NET.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Knossos.NET.Models
{
    /// <summary>
    /// Disabled = No option to compress is ever show
    /// Manual = User can select to compress mods manually during install and in mod settings
    /// Always = Always compress all mods during install, no matter what.
    /// ModSupport = Compress only if the mod depends on a FSO verson that is higher or equal than the minimal required (23.2.0)
    /// </summary>
    public enum CompressionSettings
    {
        Disabled,
        Manual,
        Always,
        ModSupport
    }

    /// <summary>
    /// Decompressor Method setting for KnUtils
    /// Auto = First try with sharpcompress and if it fails try with sevenzip
    /// </summary>
    public enum Decompressor
    {
        Auto,
        SharpCompress,
        SevenZip
    }

    /// <summary>
    /// Stores and load the global Knossos.NET configuration
    /// </summary>
    public class GlobalSettings
    {
        /// <summary>
        /// Flags handled by globalsettings, any flag that is present in this array
        /// will be ignored in mod cmdline if present.
        /// Must be in lowercase
        /// </summary>
        public static readonly string[] SystemFlags =
        {
            "enable_shadows", "shadow_quality", "aa",
            "aa_preset", "fxaa", "fxaa_preset",
            "smaa", "smaa_preset", "msaa",
            "soft_particles", "no_deferred", "window",
            "fullscreen_window", "no_vsync", "no_post_process",
            "no_fps_capping", "fps", "nosound",
            "nomusic"
        };

        public struct Etc2Config
        {
            public bool TranscodeMods { get; set; }
            public int Jobs { get; set; }
            public int Quality { get; set; }
            public bool Resize { get; set; }

            public Etc2Config()
            {
                TranscodeMods = false;
                Jobs = 2;
                Quality = 10;
                Resize = true;
            }

            public Etc2Config(bool transcodeMods, int jobs, int quality, bool resize)
            {
                TranscodeMods = transcodeMods;
                Jobs = jobs;
                Quality = quality;
                Resize = resize;
            }

            public static bool operator ==(Etc2Config left, Etc2Config right)
            {
                return left.TranscodeMods == right.TranscodeMods && left.Jobs == right.Jobs && left.Quality == right.Quality && left.Resize == right.Resize;
            }

            public static bool operator !=(Etc2Config left, Etc2Config right)
            {
                return !(left == right);
            }

            public bool Equals(Etc2Config other)
            {
                return this == other;
            }

            public override bool Equals(object? obj)
            {
                return obj is Etc2Config other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Jobs, Quality, TranscodeMods, Resize);
            }
        }

        struct Resolution
        {
            public uint width { get; set; }
            public uint height { get; set; }
        }

        /// <summary>
        /// Struc to save fso autoupdate settings
        /// Note: "UpdateRC = true" should only update RC builds if they are newer than the newest avalible stable
        /// </summary>
        public struct AutoUpdateFsoBuilds
        {
            public bool UpdateStable { get; set; }
            public bool UpdateRC { get; set; }
            public bool UpdateNightly { get; set; }
            public bool DeleteOlder { get; set; }

            public AutoUpdateFsoBuilds(bool stable = false, bool rc = false, bool nightly = false, bool deleteOlder = false)
            {
                UpdateNightly = nightly;
                UpdateStable = stable;
                UpdateRC = rc;
                DeleteOlder = deleteOlder;
            }
        }

        public struct StandaloneServerSettings
        {

            public StandaloneServerSettings(MultiCfg multiCfg, string id, string version, int buildType) : this()
            {
                this.extraOptions = multiCfg.Others;
                this.banList = multiCfg.Ban;
                this.pxoChannel = multiCfg.PXOChannel;
                this.usePxo = multiCfg.UsePXO;
                this.noVoice = multiCfg.NoVoice;
                this.port = multiCfg.Port;
                this.name = multiCfg.Name;
                this.password = multiCfg.Password;
                this.buildType = buildType;
                this.modId = id;
                this.modVersion = version;
            }

            public List<string> extraOptions { get; set; }
            public List<string> banList { get; set; }
            public string? pxoChannel { get; set; }
            public bool usePxo { get; set; }
            public bool noVoice { get; set; }
            public int port { get; set; }
            public string? name { get; set; }
            public string? password { get; set; }
            public int buildType { get; set; }
            public string modId { get; set; }
            public string modVersion { get; set; }
        }

        /* Knossos Settings */
        [JsonPropertyName("base_path")]
        public string? basePath { get; set; } = null;
        [JsonPropertyName("enable_log")]
        public bool enableLogFile { get; set; } = true;
        [JsonPropertyName("single_view_mode")]
        public bool singleViewMode { get; set; } = false;
        [JsonPropertyName("log_level")]
        public int logLevel { get; set; } = 1;
        [JsonPropertyName("global_cmdline")]
        public string? globalCmdLine { get; set; } = null;
        [JsonPropertyName("anti-stuck")]
        public bool antiStuck { get; set; } = true;
        [JsonPropertyName("force_sse2")]
        public bool forceSSE2 { get; set; } = false;
        [JsonPropertyName("max_concurrent_subtasks")]
        public int maxConcurrentSubtasks { get; set; } = 3;
        [JsonPropertyName("max_upload_retries")]
        public int maxUploadRetries { get; set; } = 4;
        [JsonPropertyName("max_download_speed")]
        public long maxDownloadSpeed { get; set; } = 0;
        [JsonPropertyName("mirror_blacklist")]
        public string[]? mirrorBlacklist { get; set; } = null;
        [JsonPropertyName("mod_compression")]
        public CompressionSettings modCompression { get; set; } = CompressionSettings.Manual;
        [JsonPropertyName("compression_max_parallelism")]
        public int compressionMaxParallelism { get; set; } = 2;
        [JsonPropertyName("auto_update")]
        public bool autoUpdate { get; set; } = false;
        [JsonPropertyName("check_updates")]
        public bool checkUpdate { get; set; } = true;
        [JsonPropertyName("delete_uploaded_files")]
        public bool deleteUploadedFiles { get; set; } = true;
        [JsonPropertyName("decompressor")]
        public Decompressor decompressor { get; set; } = Decompressor.Auto;
        [JsonPropertyName("dev_mod_sort")]
        public int devModSort { get; set; } = 0;
        [JsonPropertyName("auto_update_fso_builds")]
        public AutoUpdateFsoBuilds autoUpdateBuilds { get; set; } = new AutoUpdateFsoBuilds();
        [JsonPropertyName("warn_new_settings_system")]
        public bool warnNewSettingsSystem { get; set; } = true;
        [JsonPropertyName("hidden_mod_ids")]
        public List<string> hiddenModIds { get; set; } = new List<string>();
        [JsonIgnore]
        private bool _closeToTray { get; set; } = false;
        [JsonPropertyName("close_to_tray")]
        public bool closeToTray {
            get { return _closeToTray; }
            set { if (_closeToTray != value) {
                    _closeToTray = value;
                    pendingChangesOnAppClose = true;
                }
            }
        }
        [JsonPropertyName("ignored_launcher_updates")]
        public List<string> ignoredLauncherUpdates { get; set; } = new List<string>();

        [JsonPropertyName("standalone_server_settings")]
        public StandaloneServerSettings? standaloneServerSettings { get; set; } = null;

        [JsonPropertyName("mod_filecopy_extension_skip")]
        public List<string>? skipExtensionsModFilecopy { get; set; } = new List<string>() { ".svn", ".github", ".git", ".gitattributes", ".gitignore", ".md" };

        [JsonIgnore]
        private Etc2Config? _modEtc2TranscodeConfig { get; set; } = null;
        [JsonPropertyName("mod_etc2_transcode_config")]
        public Etc2Config? modEtc2TranscodeConfig
        {
            get { return _modEtc2TranscodeConfig; }
            set 
            { 
                if (_modEtc2TranscodeConfig != value) 
                { 
                    _modEtc2TranscodeConfig = value; 
                    pendingChangesOnAppClose = true; 
                } 
            }
        }

        /* 
         * Settings that can wait to be saved at app close so we dont have to call save() all the time
         * use JsonIgnore, private and '_' for the actual variable name
        */
        [JsonIgnore]
        private bool pendingChangesOnAppClose { get; set; } = false;

        [JsonIgnore]
        private bool _mainMenuOpen = true;
        [JsonPropertyName("main_menu_open")]
        public bool mainMenuOpen
        {
            get {  return _mainMenuOpen; }
            set { if ( _mainMenuOpen != value ) { _mainMenuOpen = value; pendingChangesOnAppClose = true; } }
        }

        [JsonIgnore]
        private bool _hideBuildRC = true;
        [JsonPropertyName("hide_build_rc")]
        public bool hideBuildRC
        {
            get { return _hideBuildRC; }
            set { if (_hideBuildRC != value) { _hideBuildRC = value; pendingChangesOnAppClose = true; } }
        }

        [JsonIgnore]
        private bool _hideBuildCustom = true;
        [JsonPropertyName("hide_build_custom")]
        public bool hideBuildCustom
        {
            get { return _hideBuildCustom; }
            set { if (_hideBuildCustom != value) { _hideBuildCustom = value; pendingChangesOnAppClose = true; } }
        }

        [JsonIgnore]
        private bool _hideBuildNightly = true;
        [JsonPropertyName("hide_build_nightly")]
        public bool hideBuildNightly
        {
            get { return _hideBuildNightly; }
            set { if (_hideBuildNightly != value) { _hideBuildNightly = value; pendingChangesOnAppClose = true; } }
        }

        [JsonIgnore]
        private ModSortType _sortType = ModSortType.name;
        [JsonPropertyName("last_sort_type"), JsonConverter(typeof(JsonStringEnumConverter))]
        public ModSortType sortType
        {
            get { return _sortType; }
            set { if (_sortType != value) { _sortType = value; pendingChangesOnAppClose = true; } }
        }

        /* FSO Settings that use the fs2_open.ini are json ignored */

        /* Video Settings */
        [JsonIgnore]
        public string? displayResolution { get; set; } = null;
        [JsonIgnore]
        public int displayIndex { get; set; } = 0;
        [JsonIgnore]
        public int displayColorDepth { get; set; } = 32;
        [JsonIgnore]
        public int textureFilter { get; set; } = 1;
        [JsonIgnore]
        public int shadowQuality { get; set; } = 0;
        [JsonIgnore]
        public int aaPreset { get; set; } = 4;
        [JsonIgnore]
        public int msaaPreset { get; set; } = 0;
        [JsonIgnore]
        public bool enableSoftParticles { get; set; } = true;
        [JsonIgnore]
        public bool enableDeferredLighting { get; set; } = true;
        [JsonIgnore]
        public int windowMode { get; set; } = 0;
        [JsonIgnore]
        public bool vsync { get; set; } = true;
        [JsonIgnore]
        public bool postProcess { get; set; } = true;
        [JsonPropertyName("no_fps_capping")]
        public bool noFpsCapping { get; set; } = false;
        [JsonPropertyName("show_fps")]
        public bool showFps { get; set; } = false;

        /* AUDIO SETTINGS */
        [JsonIgnore]
        public string? playbackDevice { get; set; } = null;
        [JsonIgnore]
        public string? captureDevice { get; set; } = null;
        [JsonPropertyName("disable_audio")]
        public bool disableAudio { get; set; } = false;
        [JsonPropertyName("disable_music")]
        public bool disableMusic { get; set; } = false;
        [JsonIgnore]
        public int sampleRate { get; set; } = 44100;
        [JsonIgnore]
        public bool enableEfx { get; set; } = false;
        [JsonPropertyName("enable_tts")]
        public bool enableTts { get; set; } = true;
        [JsonIgnore]
        public int? ttsVoice { get; set; } = null;
        public string? ttsVoiceName { get; set; } = null;
        [JsonIgnore]
        public bool ttsTechroom { get; set; } = true;
        [JsonIgnore]
        public bool ttsBriefings { get; set; } = true;
        [JsonIgnore]
        public bool ttsIngame { get; set; } = true;
        [JsonIgnore]
        public bool ttsMulti { get; set; } = true;
        [JsonPropertyName("tts_description")]
        public bool ttsDescription { get; set; } = true;
        [JsonIgnore]
        public int ttsVolume { get; set; } = 100;

        /* INPUT */
        [JsonIgnore]
        public Joystick? joystick1 { get; set; } = null;
        [JsonIgnore]
        public Joystick? joystick2 { get; set; } = null;
        [JsonIgnore]
        public Joystick? joystick3 { get; set; } = null;
        [JsonIgnore]
        public Joystick? joystick4 { get; set; } = null;
        [JsonIgnore]
        public uint joystickDeadZone { get; set; } = 10;
        [JsonIgnore]
        public uint mouseSensitivity { get; set; } = 5;
        [JsonIgnore]
        public uint joystickSensitivity { get; set; } = 9;

        /* MISC */
        [JsonIgnore]
        public string fs2Lang { get; set; } = "English";
        [JsonIgnore]
        public uint multiPort { get; set; } = 7808;
        [JsonIgnore]
        public string pxoLogin { get; set; } = "";
        [JsonIgnore]
        public string pxoPassword { get; set; } = "";
        [JsonPropertyName("portable_fso_preferences")]
        public bool portableFsoPreferences { get; set; } = true;

        /* Developer Settings */
        [JsonPropertyName("no_system_cmd")]
        public bool noSystemCMD { get; set; } = false;
        [JsonPropertyName("prefix_cmd")]
        public string prefixCMD { get; set; } = string.Empty;
        [JsonPropertyName("env_vars")]
        public string envVars { get; set; } = string.Empty;
        [JsonPropertyName("show_dev_options")]
        public bool showDevOptions { get; set; } = false;

        /// <summary>
        /// Call this when the app is closing to save settings if we have pending changes
        /// Note: this only applies to Knossos setting and not anything saved on the fs2_open.ini
        /// </summary>
        public void SaveSettingsOnAppClose()
        {
            if(pendingChangesOnAppClose)
            {
                Save(false);
            }
        }

        /// <summary>
        /// Load setting data that saves on the fs2_open.ini
        /// On the ini we save all data that is used by both FSO and KNET
        /// </summary>
        private void ReadFS2IniValues()
        {
            try
            {
                if (!File.Exists(KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "fs2_open.ini"))
                {
                    return;
                }
                var parser = new FileIniDataParser();
                var data = parser.ReadFile(KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "fs2_open.ini");
                data.Configuration.AssigmentSpacer = string.Empty;

                //LEGACY ENTRIES, mostly read only by fso
                /* Default Section */
                var resoItem = data["Default"]["VideocardFs2open"];
                if (!string.IsNullOrEmpty(resoItem))
                {
                    displayResolution = resoItem.Split('(', ')')[1];
                    if (resoItem.Contains("16 bit"))
                    {
                        displayColorDepth = 16;
                    }
                }

                if(!string.IsNullOrEmpty(data["Default"]["TextureFilter"]))
                {
                    textureFilter = int.Parse(data["Default"]["TextureFilter"]);
                }

                //Joysticks!
                if (!string.IsNullOrEmpty(data["Default"]["CurrentJoystickGUID"]) || !string.IsNullOrEmpty(data["Default"]["Joy0GUID"]))
                {
                    joystick1 = new Joystick();
                    joystick1.name = string.Empty;
                    if (!string.IsNullOrEmpty(data["Default"]["Joy0GUID"]) && !string.IsNullOrEmpty(data["Default"]["Joy0ID"]))
                    {
                        joystick1.guid = data["Default"]["Joy0GUID"];
                        joystick1.id = int.Parse(data["Default"]["Joy0ID"]);
                    }
                    else if (!string.IsNullOrEmpty(data["Default"]["CurrentJoystick"]) && !string.IsNullOrEmpty(data["Default"]["CurrentJoystickGUID"]))
                    {
                        joystick1.guid = data["Default"]["CurrentJoystickGUID"];
                        joystick1.id = int.Parse(data["Default"]["CurrentJoystick"]);
                    }
                }
                if (!string.IsNullOrEmpty(data["Default"]["Joy1GUID"]) && !string.IsNullOrEmpty(data["Default"]["Joy1ID"]))
                {
                    joystick2 = new Joystick();
                    joystick2.name = string.Empty;
                    joystick2.guid = data["Default"]["Joy1GUID"];
                    joystick2.id = int.Parse(data["Default"]["Joy1ID"]);
                }
                if (!string.IsNullOrEmpty(data["Default"]["Joy2GUID"]) && !string.IsNullOrEmpty(data["Default"]["Joy2ID"]))
                {
                    joystick3 = new Joystick();
                    joystick3.name = string.Empty;
                    joystick3.guid = data["Default"]["Joy2GUID"];
                    joystick3.id = int.Parse(data["Default"]["Joy2ID"]);
                }
                if (!string.IsNullOrEmpty(data["Default"]["Joy3GUID"]) && !string.IsNullOrEmpty(data["Default"]["Joy3ID"]))
                {
                    joystick4 = new Joystick();
                    joystick4.name = string.Empty;
                    joystick4.guid = data["Default"]["Joy3GUID"];
                    joystick4.id = int.Parse(data["Default"]["Joy3ID"]);
                }

                if (!string.IsNullOrEmpty(data["Default"]["SpeechVolume"]))
                {
                    ttsVolume = int.Parse(data["Default"]["SpeechVolume"]);
                }

                if (!string.IsNullOrEmpty(data["Default"]["SpeechVoice"]))
                {
                    ttsVoice = int.Parse(data["Default"]["SpeechVoice"]);
                }

                if (!string.IsNullOrEmpty(data["Default"]["SpeechTechroom"]))
                {
                    if (int.Parse(data["Default"]["SpeechTechroom"]) == 1)
                    {
                        ttsTechroom = true;
                    }
                    else
                    {
                        ttsTechroom = false;
                    }
                }

                if (!string.IsNullOrEmpty(data["Default"]["SpeechBriefings"]))
                {
                    if (int.Parse(data["Default"]["SpeechBriefings"]) == 1)
                    {
                        ttsBriefings = true;
                    }
                    else
                    {
                        ttsBriefings = false;
                    }
                }

                if (!string.IsNullOrEmpty(data["Default"]["SpeechIngame"]))
                {
                    if (int.Parse(data["Default"]["SpeechIngame"]) == 1)
                    {
                        ttsIngame = true;
                    }
                    else
                    {
                        ttsIngame = false;
                    }
                }

                if (!string.IsNullOrEmpty(data["Default"]["SpeechMulti"]))
                {
                    if (int.Parse(data["Default"]["SpeechMulti"]) == 1)
                    {
                        ttsMulti = true;
                    }
                    else
                    {
                        ttsMulti = false;
                    }
                }

                if (!string.IsNullOrEmpty(data["Default"]["Language"]))
                {
                    fs2Lang = data["Default"]["Language"];
                }

                if (!string.IsNullOrEmpty(data["Default"]["ForcePort"]))
                {
                    multiPort = uint.Parse(data["Default"]["ForcePort"]);
                }

                /* Video Section */
                if (!string.IsNullOrEmpty(data["Video"]["Display"]))
                {
                    displayIndex = int.Parse(data["Video"]["Display"]);
                }
                else
                {
                    displayIndex = 0;
                }

                /* Sound Section */
                captureDevice = data["Sound"]["CaptureDevice"];
                playbackDevice = data["Sound"]["PlaybackDevice"];
                if(!string.IsNullOrEmpty(data["Sound"]["SampleRate"]))
                {
                    sampleRate = int.Parse(data["Sound"]["SampleRate"]);
                }
                if (!string.IsNullOrEmpty(data["Sound"]["EnableEFX"]))
                {
                    if(int.Parse(data["Sound"]["EnableEFX"])==1)
                    {
                        enableEfx = true;
                    }
                    else
                    {
                        enableEfx = false;
                    }
                }

                //SCPUI ENTRIES
                /* Graphics Section */
                var scpUiReso = data["Graphics"]["Resolution"];
                if (!string.IsNullOrEmpty(scpUiReso))
                {
                    var rj=JsonSerializer.Deserialize<Resolution>(scpUiReso)!;
                    displayResolution = rj.width + "x" + rj.height;
                }
                var winMode = data["Graphics"]["WindowMode"];
                if(!string.IsNullOrEmpty(winMode))
                {   
                    windowMode = int.Parse(winMode);
                }
                if (data["Graphics"]["Display"] != null)
                {
                    displayIndex = int.Parse(data["Graphics"]["Display"]);
                }
                else
                {
                    displayIndex = 0;
                }

                if (!string.IsNullOrEmpty(data["Graphics"]["TextureFilter"]))
                {
                    textureFilter = int.Parse(data["Graphics"]["TextureFilter"]);
                }

                if(!string.IsNullOrEmpty(data["Graphics"]["Shadows"]))
                {
                    shadowQuality = int.Parse(data["Graphics"]["Shadows"]);
                }

                if (!string.IsNullOrEmpty(data["Graphics"]["AAMode"]))
                {
                    aaPreset = int.Parse(data["Graphics"]["AAMode"]);
                }

                if (!string.IsNullOrEmpty(data["Graphics"]["MSAASamples"]))
                {
                    // recall MSAASamples is in intervals of 4 (0, 4, 8) so convert to preset level
                    msaaPreset = int.Parse(data["Graphics"]["MSAASamples"]) / 4;
                }

                if (!string.IsNullOrEmpty(data["Graphics"]["SoftParticles"]))
                {
                    enableSoftParticles = bool.Parse(data["Graphics"]["SoftParticles"]);
                }

                if (!string.IsNullOrEmpty(data["Graphics"]["DeferredLighting"]))
                {
                    enableDeferredLighting = bool.Parse(data["Graphics"]["DeferredLighting"]);
                }

                if (!string.IsNullOrEmpty(data["Graphics"]["VSync"]))
                {
                    vsync = bool.Parse(data["Graphics"]["VSync"]);
                }

                if (!string.IsNullOrEmpty(data["Graphics"]["PostProcessing"]))
                {
                    postProcess = bool.Parse(data["Graphics"]["PostProcessing"]);
                }

                /* Input Section */
                if (!string.IsNullOrEmpty(data["Input"]["Joystick"]))
                {
                    joystick1 = JsonSerializer.Deserialize<Joystick>(data["Input"]["Joystick"])!;
                }
                if (!string.IsNullOrEmpty(data["Input"]["Joystick1"]))
                {
                    joystick2 = JsonSerializer.Deserialize<Joystick>(data["Input"]["Joystick1"])!;
                }
                if (!string.IsNullOrEmpty(data["Input"]["Joystick2"]))
                {
                    joystick3 = JsonSerializer.Deserialize<Joystick>(data["Input"]["Joystick2"])!;
                }
                if (!string.IsNullOrEmpty(data["Input"]["Joystick3"]))
                {
                    joystick4 = JsonSerializer.Deserialize<Joystick>(data["Input"]["Joystick3"])!;
                }
                if (!string.IsNullOrEmpty(data["Input"]["JoystickDeadZone"]))
                {
                    joystickDeadZone = uint.Parse(data["Input"]["JoystickDeadZone"]);
                }

                if (!string.IsNullOrEmpty(data["Input"]["JoystickSensitivity"]))
                {
                    joystickSensitivity = uint.Parse(data["Input"]["JoystickSensitivity"]);
                }

                if (!string.IsNullOrEmpty(data["Input"]["MouseSensitivity"]))
                {
                    mouseSensitivity = uint.Parse(data["Input"]["MouseSensitivity"]);
                }

                //PXO
                if (!string.IsNullOrEmpty(data["PXO"]["Login"]))
                {
                    pxoLogin = data["PXO"]["Login"];
                }
                if (!string.IsNullOrEmpty(data["PXO"]["Password"]))
                {
                    pxoPassword = data["PXO"]["Password"];
                }
            }
            catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Information, "GlobalSettings.ReadFS2IniValues()", ex);
            }
        }

        /// <summary>
        /// Load all Knet setting data, both from settings.json and from the fs2_open.ini
        /// Data that is used only by KNET is saved on settings.json
        /// Data that is used by FSO anf KNET is saved on the fs2_open.ini
        /// Any new variable that is added to the json must be added here or it would not be loaded
        /// </summary>
        public void Load()
        {
            try
            {
                if (File.Exists(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "settings.json"))
                {
                    using FileStream jsonFile = File.OpenRead(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "settings.json");
                    var tempSettings = JsonSerializer.Deserialize<GlobalSettings>(jsonFile)!;
                    jsonFile.Close();
                    if (tempSettings != null)
                    {
                        basePath = tempSettings.basePath;
                        enableLogFile = tempSettings.enableLogFile;
                        logLevel = tempSettings.logLevel;
                        globalCmdLine = tempSettings.globalCmdLine;
                        ttsDescription = tempSettings.ttsDescription;
                        enableTts = tempSettings.enableTts;
                        noFpsCapping = tempSettings.noFpsCapping;
                        showFps = tempSettings.showFps;
                        disableAudio= tempSettings.disableAudio;
                        disableMusic= tempSettings.disableMusic;
                        hideBuildCustom = tempSettings.hideBuildCustom;
                        hideBuildNightly = tempSettings.hideBuildNightly;
                        hideBuildRC = tempSettings.hideBuildRC;
                        forceSSE2 = tempSettings.forceSSE2;
                        maxConcurrentSubtasks = tempSettings.maxConcurrentSubtasks;
                        maxDownloadSpeed = tempSettings.maxDownloadSpeed;
                        mirrorBlacklist = tempSettings.mirrorBlacklist;
                        modCompression = tempSettings.modCompression;
                        compressionMaxParallelism = tempSettings.compressionMaxParallelism;
                        autoUpdate = tempSettings.autoUpdate;
                        checkUpdate = tempSettings.checkUpdate;
                        ttsVoiceName = tempSettings.ttsVoiceName;
                        deleteUploadedFiles = tempSettings.deleteUploadedFiles;
                        decompressor = tempSettings.decompressor;
                        devModSort = tempSettings.devModSort;
                        autoUpdateBuilds = tempSettings.autoUpdateBuilds;
                        noSystemCMD = tempSettings.noSystemCMD;
                        prefixCMD = tempSettings.prefixCMD;
                        envVars = tempSettings.envVars;
                        showDevOptions = tempSettings.showDevOptions;
                        warnNewSettingsSystem = tempSettings.warnNewSettingsSystem;
                        mainMenuOpen = tempSettings.mainMenuOpen;
                        sortType = tempSettings.sortType;
                        portableFsoPreferences = tempSettings.portableFsoPreferences;
                        closeToTray = tempSettings.closeToTray;
                        ignoredLauncherUpdates = tempSettings.ignoredLauncherUpdates;
                        hiddenModIds = tempSettings.hiddenModIds;
                        antiStuck = tempSettings.antiStuck;
                        maxUploadRetries = tempSettings.maxUploadRetries;
                        singleViewMode = tempSettings.singleViewMode;
                        modEtc2TranscodeConfig = tempSettings.modEtc2TranscodeConfig;
                        if (hiddenModIds.Any())
                        {
                            foreach (var hiddenMod in hiddenModIds)
                            {
                                ModTags.AddModFilter(hiddenMod, "Hidden");
                            }
                            MainViewModel.Instance?.InstalledModsView?.ResetFilters();
                        }
                        standaloneServerSettings = tempSettings.standaloneServerSettings;
                        skipExtensionsModFilecopy = tempSettings.skipExtensionsModFilecopy;
                        ReadFS2IniValues();
                        Log.Add(Log.LogSeverity.Information, "GlobalSettings.Load()", "Global settings have been loaded");

                        SetCustomModeValues();

                        if (KnUtils.IsAndroid && modEtc2TranscodeConfig == null) // load default
                        {
                            var threads = KnUtils.GetMaxThreads();
                            var gpuSupport = AndroidHelper.GpuSupportsBCnTexturesOpenGL();
                            var enabled = KnUtils.IsAndroid ? !gpuSupport.s3tc || !gpuSupport.bc7 : false;
                            modEtc2TranscodeConfig = new Etc2Config(enabled, threads.recommended, 10, true);
                        }

                        pendingChangesOnAppClose = false;
                    }

                }
                else
                {
                    Log.Add(Log.LogSeverity.Information, "GlobalSettings.Load()", "File settings.json does not exist.");
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "GlobalSettings.Load()", ex);
            }
            if(Knossos.inPortableMode)
            {
                basePath = Path.Combine(KnUtils.KnetFolderPath!, "kn_portable", "Library");
            }
        }

        /// <summary>
        /// Values controlled by CustomLauncher.cs in single tc mode
        /// </summary>
        private void SetCustomModeValues()
        {
            if (CustomLauncher.IsCustomMode)
            {
                checkUpdate = CustomLauncher.AllowLauncherUpdates;
                enableLogFile = CustomLauncher.WriteLogFile;
                autoUpdate = false;
                if (!CustomLauncher.MenuDisplayGlobalSettingsEntry)
                {
                    warnNewSettingsSystem = false;
                }
            }
        }

        /// <summary>
        /// Save setting data to the fs2_open.ini
        /// Stops the ini-watcher if it was enabled and re-enables it to avoid triggering a read
        /// Optional: Specific path to write the .ini to, need to be FULL PATH
        /// </summary>
        /// <param name="customFullPath"></param>
        public void WriteFS2IniValues(string? customFullPath = null)
        {
            try
            {
                var parser = new FileIniDataParser();
                if(!File.Exists(KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "fs2_open.ini"))
                {
                    Directory.CreateDirectory(KnUtils.GetFSODataFolderPath());
                    File.Create(KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "fs2_open.ini").Close();
                }

                var data = parser.ReadFile(KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "fs2_open.ini");
                data.Configuration.AssigmentSpacer = string.Empty;

                /* Default Section */
                if(displayResolution != null)
                { 
                    data["Default"]["VideocardFs2open"]  = "OGL -(" + displayResolution + ")x" + displayColorDepth + " bit";
                }
                data["Default"]["TextureFilter"] = textureFilter.ToString();
                data["Default"]["Language"] = fs2Lang;
                data["Default"]["ForcePort"] = multiPort.ToString();
                data["Default"]["ConnectionSpeed"] = "Fast";
                data["Default"]["NetworkConnection"] = "LAN";
                if (enableTts)
                {
                    data["Default"]["SpeechTechroom"] = Convert.ToInt32(ttsTechroom).ToString();
                    data["Default"]["SpeechBriefings"] = Convert.ToInt32(ttsBriefings).ToString();
                    data["Default"]["SpeechIngame"] = Convert.ToInt32(ttsIngame).ToString();
                    data["Default"]["SpeechMulti"] = Convert.ToInt32(ttsMulti).ToString();
                }
                else
                {
                    data["Default"]["SpeechTechroom"] = "0";
                    data["Default"]["SpeechBriefings"] = "0";
                    data["Default"]["SpeechIngame"] = "0";
                    data["Default"]["SpeechMulti"] = "0";
                }

                data["Default"]["SpeechVoice"] = ttsVoice.ToString();
                data["Default"]["SpeechVolume"] = ttsVolume.ToString();

                if (joystick1 != null)
                {
                    data["Default"]["CurrentJoystickGUID"] = joystick1.guid;
                    data["Default"]["CurrentJoystick"] = joystick1.id.ToString();
                    data["Default"]["Joy0GUID"] = joystick1.guid;
                    data["Default"]["Joy0ID"] = joystick1.id.ToString();
                }
                else
                {
                    data["Default"].RemoveKey("CurrentJoystickGUID");
                    data["Default"].RemoveKey("CurrentJoystick");
                    data["Default"].RemoveKey("Joy0GUID");
                    data["Default"].RemoveKey("Joy0ID");
                }
                if (joystick2 != null)
                {
                    data["Default"]["Joy1GUID"] = joystick2.guid;
                    data["Default"]["Joy1ID"] = joystick2.id.ToString();
                }
                else
                {
                    data["Default"].RemoveKey("Joy1GUID");
                    data["Default"].RemoveKey("Joy1ID");
                }
                if (joystick3 != null)
                {
                    data["Default"]["Joy2GUID"] = joystick3.guid;
                    data["Default"]["Joy2ID"] = joystick3.id.ToString();
                }
                else
                {
                    data["Default"].RemoveKey("Joy2GUID");
                    data["Default"].RemoveKey("Joy2ID");
                }
                if (joystick4 != null)
                {
                    data["Default"]["Joy3GUID"] = joystick4.guid;
                    data["Default"]["Joy3ID"] = joystick4.id.ToString();
                }
                else
                {
                    data["Default"].RemoveKey("Joy3GUID");
                    data["Default"].RemoveKey("Joy3ID");
                }
                data["Default"]["TextureFilter"] = textureFilter.ToString();

                /* Video Section */
                data["Video"]["Display"] = displayIndex.ToString();

                /* Sound Section */
                data["Sound"]["SampleRate"] = sampleRate.ToString();
                data["Sound"]["EnableEFX"] = Convert.ToInt32(enableEfx).ToString();
                data["Sound"]["PlaybackDevice"] = playbackDevice;
                data["Sound"]["CaptureDevice"] = captureDevice;

                /* Graphics Section */
                if (displayResolution != null)
                {
                    data["Graphics"]["Resolution"] = "{\"width\":" + displayResolution.Split("x")[0] +",\"height\":"+ displayResolution.Split("x")[1] + "}";
                }
                data["Graphics"]["Shadows"] = shadowQuality.ToString();
                data["Graphics"]["AAMode"] = aaPreset.ToString();
                data["Graphics"]["MSAASamples"] = (msaaPreset * 4).ToString(); // recall MSAASamples is in intervals of 4 (0, 4, 8) so convert to preset level
                data["Graphics"]["WindowMode"] = windowMode.ToString();
                data["Graphics"]["Display"] = displayIndex.ToString();
                data["Graphics"]["TextureFilter"] = textureFilter.ToString();
                data["Graphics"]["SoftParticles"] = enableSoftParticles.ToString().ToLower();
                data["Graphics"]["DeferredLighting"] = enableDeferredLighting.ToString().ToLower();
                data["Graphics"]["VSync"] = vsync.ToString().ToLower();
                data["Graphics"]["PostProcessing"] = postProcess.ToString().ToLower();

                /* Input Section */
                if (joystick1 != null)
                {
                    data["Input"]["Joystick"] = "{\"guid\":\"" + joystick1.guid + "\",\"id\":" + joystick1.id + "}";
                }
                else
                {
                    data["Input"].RemoveKey("Joystick");
                }
                if (joystick2 != null)
                {
                    data["Input"]["Joystick1"] = "{\"guid\":\"" + joystick2.guid + "\",\"id\":" + joystick2.id + "}";
                }
                else
                {
                    data["Input"].RemoveKey("Joystick1");
                }
                if (joystick3 != null)
                {
                    data["Input"]["Joystick2"] = "{\"guid\":\"" + joystick3.guid + "\",\"id\":" + joystick3.id + "}";
                }
                else
                {
                    data["Input"].RemoveKey("Joystick2");
                }
                if (joystick4 != null)
                {
                    data["Input"]["Joystick3"] = "{\"guid\":\"" + joystick4.guid + "\",\"id\":" + joystick4.id + "}";
                }
                else
                {
                    data["Input"].RemoveKey("Joystick3");
                }

                data["Input"]["JoystickDeadZone"] = joystickDeadZone.ToString();
                data["Input"]["JoystickSensitivity"] = joystickSensitivity.ToString();
                data["Input"]["MouseSensitivity"] = mouseSensitivity.ToString();

                data["PXO"]["Login"] = pxoLogin;
                data["PXO"]["Password"] = pxoPassword;

                if (customFullPath == null)
                {
                    parser.WriteFile(KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "fs2_open.ini", data, new UTF8Encoding(false));
                    Log.Add(Log.LogSeverity.Information, "GlobalSettings.WriteFS2IniValues", "Writen ini: " + KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "fs2_open.ini");
                }
                else
                {

                    parser.WriteFile(customFullPath, data, new UTF8Encoding(false));
                    Log.Add(Log.LogSeverity.Information, "GlobalSettings.WriteFS2IniValues", "Writen ini: " + customFullPath);
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "GlobalSettings.WriteFS2IniValues()", ex);
            }
        }

        /// <summary>
        /// Write values to settings.json and fs2_open.ini
        /// Writing the ini is optional, it should not be done for Knossos-only settings.
        /// </summary>
        /// <param name="writeIni"></param>
        public void Save(bool writeIni = true) 
        {
            SetCustomModeValues();
            if (writeIni)
            {
                WriteFS2IniValues();
            }
            try
            {
                // Quickly update the sort type which is managed elsewhere
                if (MainViewModel.Instance != null){
                    sortType = Knossos.globalSettings.sortType;
                }

                var options = new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "settings.json", json, new UTF8Encoding(false));
                Log.Add(Log.LogSeverity.Information, "GlobalSettings.Save()", "Global settings have been saved.");
                pendingChangesOnAppClose = false;
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "GlobalSettings.Save()", ex);
            }
        }

        /// <summary>
        /// Convert user-selected settings into FSO cmdline arguments
        /// </summary>
        /// <param name="build"></param>
        /// <returns>cmdline string</returns>
        public string GetSystemCMD(FsoBuild? build = null)
        { 
            var cmd = string.Empty;
            if(shadowQuality > 0)
            {
                cmd += " -enable_shadows";
                cmd += " -shadow_quality " + shadowQuality;
            }
            if(aaPreset > 0)
            {
                // recall that the aaPreset value saved in the ini/in-game options goes form 0-7
                // but the flags use 0-6, so account for that difference
                var flag_aaPreset = aaPreset - 1;
                if (build == null || SemanticVersion.Compare(build.version, "21.0.0") >= 0)
                {
                    cmd += " -aa";
                    cmd += " -aa_preset " + flag_aaPreset;
                }
                else
                {
                    if (SemanticVersion.Compare(build.version, "3.8.0") >= 0)
                    {
                        if (flag_aaPreset <= 2)
                        {
                            cmd += " -fxaa";
                            switch (flag_aaPreset)
                            {
                                case 0: cmd += " -fxaa_preset 1"; break;
                                case 1: cmd += " -fxaa_preset 5"; break;
                                case 2: cmd += " -fxaa_preset 7"; break;
                            }
                        }
                        else
                        {
                            cmd += " -smaa";
                            switch (flag_aaPreset)
                            {
                                case 3: cmd += " -smaa_preset 0"; break;
                                case 4: cmd += " -smaa_preset 1"; break;
                                case 5: cmd += " -smaa_preset 2"; break;
                                case 6: cmd += " -smaa_preset 3"; break;
                            }
                        }
                    }
                }
            }
            if (msaaPreset > 0)
            {
                switch (msaaPreset)
                {
                    case 1: cmd += " -msaa 4"; break;
                    case 2: cmd += " -msaa 8"; break;
                }
            }
            if (enableSoftParticles)
            {
                cmd += " -soft_particles";
            }
            if (!enableDeferredLighting)
            {
                cmd += " -no_deferred";
            }
            switch (windowMode)
            {
                case 0: cmd += " -window"; break;
                case 1: cmd += " -fullscreen_window"; break;
                case 2: break; //fullscreen
            }
            if (!vsync)
            {
                cmd += " -no_vsync";
            }
            if(!postProcess)
            {
                cmd += " -no_post_process";
            }
            if (noFpsCapping)
            {
                cmd += " -no_fps_capping";
            }
            if(showFps)
            {
                cmd += " -fps";
            }
            if(disableAudio)
            {
                cmd += " -nosound";
            }
            if(disableMusic)
            {
                cmd += " -nomusic";
            }
            return cmd;
        }
    }
}
