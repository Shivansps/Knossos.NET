using System.Text.Json;
using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using Knossos.NET.ViewModels;
using System.Linq;
using System.Text.Json.Serialization;
using Avalonia.Threading;
using System.Collections.Generic;
using Knossos.NET.Classes;
using System.Threading;
using System.Security.Cryptography;
using Knossos.NET.Views;

namespace Knossos.NET.Models
{
    /// <summary>
    /// Model to handle all the Nebula website operations.
    /// </summary>
    public static class Nebula
    {
        private class NewerModVersionsData
        {
            public string Id { get; set; } = string.Empty;
            public string Version { get; set; } = string.Empty;
        }

        private struct NebulaSettings
        {
            public string? etag { get; set; }
            public string? user { get; set; }
            public string? pass { get; set; }
            public bool logged { get; set; }
            public List<NewerModVersionsData> NewerModsVersions { get; set; }
            public NebulaModTagJson[] modtags { get; set; }


            public NebulaSettings()
            {
                etag = null;
                user = null;
                pass = null;
                logged = false;
                NewerModsVersions = new List<NewerModVersionsData>();
                modtags = new NebulaModTagJson[0];
            }
        }

        public struct RepoData
        {
            public Mod[]? mods { get; set; }
        }

        private struct NebulaCache
        { 
            public string modID { get; set; }
            public string modVersion { get; set; }
            public string modString { get; set; }
        }

        public struct NebulaModTagJson
        {
            public string modid { get; set; }
            public string[] filters { get; set; }
            public string[] tags { get; set; }
        }

        private readonly static string ModTagsURL = "https://raw.githubusercontent.com/KnossosNET/KNet-General-Resources-Repo/main/modtags.json";

        //https://cf.fsnebula.org/storage/repo.json
        //https://dl.fsnebula.org/storage/repo.json
        //https://fsnebula.org/storage/repo.json"

        public static string[] nebulaMirrors = { "cf.fsnebula.org", "dl.fsnebula.org", "fsnebula.org", "talos.feralhosting.com", "fsnebula.global.ssl.fastly.net" }; //lowercase, last one is the image host
        private static readonly string repoUrl = @"https://fsnebula.org/storage/repo_minimal.json";
        private static readonly string apiURL = @"https://api.fsnebula.org/api/1/";
        private static readonly string nebulaURL = @"https://fsnebula.org/";
        private static readonly bool listFS2Override = false;
        private static CancellationTokenSource? cancellationToken = null;
        public static bool repoLoaded = false;
        private static NebulaSettings settings = new NebulaSettings();
        private static string? apiUserToken = null;
        public static bool userIsLoggedIn { get { return settings.logged; } }
        public static string? userName { get { return settings.user; } }
        public static string? userPass { get { return settings.pass != null ? KnUtils.StringDecryption(settings.pass) : null; } }
        private static Mod[]? privateMods;
        private static string[]? editableIds;
        private static List<NebulaCache?> nebulaModDataCache = new List<NebulaCache?>();

        /// <summary>
        /// Initial method
        /// Loads settings from nebula.json
        /// Check etag/gets repo.json
        /// parse repo json
        /// load nebula mods/fso builds to UI
        /// If user has saved credentials to Nebula, login and check private mods
        /// Displays mods updates to taskview (if any)
        /// </summary>
        public static async Task Trinity()
        {
            //Custom mode with no nebula services
            if(Knossos.inSingleTCMode && !CustomLauncher.UseNebulaServices)
            {
                Log.Add(Log.LogSeverity.Warning, "Nebula.Trinity()", "Nebula services has been disabled.");
                repoLoaded = true;
                return;
            }
            try
            {
                nebulaModDataCache.Clear();
                repoLoaded = false;
                if (cancellationToken != null)
                {
                    cancellationToken.Cancel();
                    await Task.Delay(5000).ConfigureAwait(false);
                }
                cancellationToken = new CancellationTokenSource();

                if (File.Exists(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "nebula.json"))
                {
                    string jsonString = File.ReadAllText(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "nebula.json");
                    settings = JsonSerializer.Deserialize<NebulaSettings>(jsonString);
                    Log.Add(Log.LogSeverity.Information, "Nebula.Constructor()", "Nebula settings have been loaded");
                }
                else
                {
                    Log.Add(Log.LogSeverity.Information, "Nebula.Constructor()", "File nebula.json does not exist.");
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.Constructor()", ex);
            }
            try
            {
                bool displayUpdates = settings.NewerModsVersions.Any() && !CustomLauncher.IsCustomMode ? true : false;
                var webEtag = await KnUtils.GetUrlFileEtag(repoUrl).ConfigureAwait(false);
                var neb_text_downloading = CustomLauncher.IsCustomMode ? CustomLauncher.NebulaTextDownloading : "Downloading repo_minimal.json";
                var neb_text_uptodate = CustomLauncher.IsCustomMode ? CustomLauncher.NebulaTextUpToDate : "Nebula: repo_minimal.json is up to date!";
                var neb_text_tooltip = CustomLauncher.IsCustomMode ? CustomLauncher.NebulaTextToolTip : "The repo_minimal.json file contains info on all the mods available in Nebula, without this you will not be able to install new mods or engine builds";
                if (!File.Exists(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "repo_minimal.json") || settings.etag != webEtag)
                {
                    //Download the repo_minimal.json
                    if (TaskViewModel.Instance != null)
                    {
                        var result = await Dispatcher.UIThread.InvokeAsync(async()=>await TaskViewModel.Instance.AddFileDownloadTask(repoUrl, KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "repo_minimal_temp.json", neb_text_downloading, true, neb_text_tooltip), DispatcherPriority.Background).ConfigureAwait(false);

                        if (cancellationToken!.IsCancellationRequested)
                        {
                            throw new TaskCanceledException();
                        }
                        if (result != null && result == true)
                        {
                            try
                            {
                                File.Delete(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "repo_minimal.json");
                                File.Move(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "repo_minimal_temp.json", KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "repo_minimal.json");
                                settings.etag = webEtag;
                                SaveSettings();
                            }
                            catch (Exception ex)
                            {
                                if (cancellationToken != null)
                                {
                                    cancellationToken.Dispose();
                                    cancellationToken = null;
                                }
                                Log.Add(Log.LogSeverity.Error, "Nebula.Trinity()", ex);
                            }
                        }
                    }
                }
                else
                {
                    //No update is needed
                    Dispatcher.UIThread.Invoke(() => TaskViewModel.Instance?.AddMessageTask(neb_text_uptodate, neb_text_tooltip), DispatcherPriority.Background);
                    Log.Add(Log.LogSeverity.Information, "Nebula.Trinity()", "repo_minimal.json is up to date!");
                    displayUpdates = false;
                    repoLoaded = true;
                }
                if (cancellationToken != null && cancellationToken!.IsCancellationRequested)
                {
                    throw new TaskCanceledException();
                }
                var updates = await InitialRepoLoad().ConfigureAwait(false);
                if (updates != null && updates.Any())
                {
                    /**************************************************************************************************************************/
                    //If we have a repo update lets update modtags as well
                    //This part of the code should be replaced once modtags are integrated into nebula
                    if (!CustomLauncher.IsCustomMode)
                    {
                        try
                        {
                            HttpResponseMessage response = await KnUtils.GetHttpClient().GetAsync(ModTagsURL).ConfigureAwait(false);
                            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            var tagsRepo = JsonSerializer.Deserialize<NebulaModTagJson[]>(json)!;
                            if (tagsRepo != null)
                            {
                                settings.modtags = tagsRepo;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Add(Log.LogSeverity.Error, "Nebula.Trinity()", ex);
                        }
                    }
                    /**************************************************************************************************************************/
                    SaveSettings();
                    if (displayUpdates)
                    {
                        try
                        {
                            Dispatcher.UIThread.Invoke(() => TaskViewModel.Instance?.AddDisplayUpdatesTask(updates), DispatcherPriority.Background);
                        }
                        catch { }
                    }
                }
                if (cancellationToken != null && cancellationToken!.IsCancellationRequested)
                {
                    throw new TaskCanceledException();
                }
                if (userIsLoggedIn && !Knossos.inSingleTCMode || userIsLoggedIn && Knossos.inSingleTCMode && CustomLauncher.MenuDisplayNebulaLoginEntry)
                {
                    await LoadPrivateMods(cancellationToken).ConfigureAwait(false);
                }
                int filters = 0, tags = 0;
                //Load mod tags
                if (!CustomLauncher.IsCustomMode)
                {
                    foreach (var modtag in settings.modtags)
                    {
                        if (modtag.filters != null)
                        {
                            foreach (var f in modtag.filters)
                            {
                                ModTags.AddModFilter(modtag.modid, f);
                                filters++;
                            }
                        }
                        if (modtag.tags != null)
                        {
                            foreach (var t in modtag.tags)
                            {
                                ModTags.AddModTag(modtag.modid, t);
                                tags++;
                            }
                        }
                    }
                }

                Log.Add(Log.LogSeverity.Information, "Nebula.Trinity()", "ModTags has loaded: " + filters + " filters (" + ModTags.GetListAllFilters().Count() + " unique) and " + tags + " mod tags.");
                repoLoaded = true;
            }
            catch (TaskCanceledException)
            {
                if (cancellationToken != null)
                {
                    cancellationToken.Dispose();
                    cancellationToken = null;
                }
            }
        }

        /// <summary>
        /// Gets private mods from nebula and display them on UI
        /// </summary>
        /// <param name="cancellationToken"></param>
        private static async Task LoadPrivateMods(CancellationTokenSource? cancellationToken)
        {
            try
            {
                privateMods = await GetPrivateMods().ConfigureAwait(false);
                if (privateMods != null && privateMods.Any())
                {
                    foreach (var mod in privateMods)
                    {
                        if (cancellationToken != null && cancellationToken!.IsCancellationRequested)
                        {
                            throw new TaskCanceledException();
                        }
                        if (mod.type == ModType.engine)
                        {
                            //This is already installed?
                            var isInstalled = Knossos.GetInstalledBuildsList(mod.id)?.FirstOrDefault(b => b.version == mod.version);
                            if (isInstalled == null)
                            {
                                Dispatcher.UIThread.Invoke(() => FsoBuildsViewModel.Instance?.AddBuildToUi(new FsoBuild(mod)), DispatcherPriority.Background);
                            }
                            else
                            {
                                if(isInstalled.modData != null)
                                {
                                    isInstalled.modData.inNebula = true;
                                    if (isInstalled.modData.devMode)
                                        DeveloperModsViewModel.Instance?.UpdateVersionManager(isInstalled.modData.id);
                                }
                            }
                        }
                        //Remove Installed and FS2 parent mods if FS2 root pack is not detected, Mark update available to installed ones
                        if (mod.type == ModType.tc || mod.type == ModType.mod && (listFS2Override || mod.parent != "FS2" || mod.parent == "FS2" && Knossos.retailFs2RootFound))
                        {

                            //This is already installed?
                            var isInstalled = Knossos.GetInstalledModList(mod.id);
                            if (isInstalled != null && isInstalled.Any())
                            {
                                var versionInNebula = isInstalled.FirstOrDefault(x => x.version == mod.version);
                                if (versionInNebula != null)
                                    versionInNebula.inNebula = true;
                                var newer = isInstalled.MaxBy(x => new SemanticVersion(x.version));
                                if (newer != null && ( new SemanticVersion(newer.version) < new SemanticVersion(mod.version) || newer.version == mod.version && newer.lastUpdate != mod.lastUpdate))
                                {
                                    Dispatcher.UIThread.Invoke(() => MainWindowViewModel.Instance?.MarkAsUpdateAvailable(mod.id, true, mod.version), DispatcherPriority.Background);
                                }
                                if(isInstalled.First().devMode)
                                    DeveloperModsViewModel.Instance?.UpdateVersionManager(isInstalled.First().id);
                            }
                            else
                            {
                                Dispatcher.UIThread.Invoke(() => MainWindowViewModel.Instance?.AddNebulaMod(mod), DispatcherPriority.Background);
                            }
                        }
                    }
                }
            } catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.LoadPrivateMods()", ex);
            }
        }

        /// <summary>
        /// Checks if a MOD version is
        /// newer than the saved version on NewWerModVersions
        /// for that ID.
        /// </summary>
        /// <param name="mod"></param>
        /// <returns>true/false</returns>
        private static bool IsModUpdate(Mod mod)
        {
            try
            {
                string id = mod.id;
                if (mod.type == ModType.engine)
                {
                    if (mod.stability == "rc")
                    {
                        id += "_RC";
                    }
                    else if (mod.stability == "nightly")
                    {
                        id += "_nightly";
                    }
                }
                var exist = settings.NewerModsVersions.FirstOrDefault(m => m.Id == id);
                if (exist != null)
                {
                    if (SemanticVersion.Compare(exist.Version, mod.version) < 0)
                    {
                        exist.Version = mod.version;
                        return true;
                    }
                }
                else
                {
                    settings.NewerModsVersions.Add(new NewerModVersionsData { Id = id, Version = mod.version });
                    mod.isNewMod = true;
                    return true;
                }
            } catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.IsModUpdate()", ex);
            }
            return false;
        }

        /// <summary>
        /// Parses the repo_minimal.json file
        /// </summary>
        /// <returns>List of mods or null</returns>
        private static async Task<List<Mod>?> GetModsInRepo(CancellationTokenSource? cancelToken = null)
        {
            try
            {
                await WaitForFileAccess(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "repo_minimal.json");
                if (cancelToken != null && cancelToken!.IsCancellationRequested)
                {
                    throw new TaskCanceledException();
                }
                RepoData? repoData = null;
                using (FileStream? fileStream = new FileStream(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "repo_minimal.json", FileMode.Open, FileAccess.ReadWrite))
                {
                    JsonSerializerOptions serializerOptions = new JsonSerializerOptions();
                    try
                    {
                        repoData = await JsonSerializer.DeserializeAsync<RepoData>(fileStream);
                    }
                    catch
                    {
                        //TODO: Remove this at some point after a month or two (2023/10/25)
                        //The old parsing method deleted the last character from the json file, what would fail the parsing of the file if already downloaded
                        //before this update, so re-add it and try to desetialize again
                        fileStream.Seek(-1, SeekOrigin.End);
                        if (fileStream.ReadByte() != '}')
                        {
                            fileStream.WriteByte(Convert.ToByte('}'));
                            fileStream.Seek(0, SeekOrigin.Begin);
                            repoData = await JsonSerializer.DeserializeAsync<RepoData>(fileStream);
                        }
                    }
                }
                if (repoData != null && repoData.Value.mods != null)
                {
                    return repoData.Value.mods.ToList();
                }
            }catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.GetModsInRepo()", ex);
            }
            return null;
        }

        /// <summary>
        /// Load data from repo to UI
        /// Return list of NewerModVersions
        /// That contains every ID and its newer version
        /// </summary>
        /// <returns>NewerModVersions list</returns>
        private static async Task<List<Mod>?> InitialRepoLoad()
        {
            try
            {
                var allModsInRepo = await GetModsInRepo(cancellationToken);
                if (allModsInRepo == null)
                    return null;

                //Only keep the newerest version of each mod
                //Determine if it should be added to the update list to display repo changes in UI
                var modsByID = allModsInRepo.GroupBy(m => m.id);
                var newerestModVersionPerID = new List<Mod>();
                var modUpdates = new List<Mod>();
                foreach(var idGroup in modsByID)
                {
                    Mod? newerestOfID = idGroup.First();
                    if(idGroup.Count() > 1)
                    {
                        newerestOfID = idGroup.MaxBy(x => new SemanticVersion(x.version));
                    }
                    if (newerestOfID != null)
                    {
                        newerestModVersionPerID.Add(newerestOfID);
                        if (IsModUpdate(newerestOfID))
                        {
                            modUpdates.Add(newerestOfID);
                        }
                    }
                };

                if (cancellationToken != null && cancellationToken!.IsCancellationRequested)
                {
                    throw new TaskCanceledException();
                }

                //Mods, TCs
                var modsTcs = newerestModVersionPerID.Where( m => m.type == ModType.mod || m.type == ModType.tc ).ToList();
                //Remove Installed and FS2 parent mods if FS2 root pack is not detected, Mark update available to installed ones, set installed ones as inNebula
                foreach (var m in modsTcs.ToList())
                {

                    if (listFS2Override || ( m.parent != "FS2" || m.parent == "FS2" && Knossos.retailFs2RootFound ))
                    {
                        //This is already installed?
                        var isInstalled = Knossos.GetInstalledModList(m.id);
                        if (isInstalled != null && isInstalled.Any())
                        {
                            //Set installed mods that are uploaded to Nebula as "inNebula=true"
                            isInstalled.ForEach(mod =>
                            {
                                if (mod != null && allModsInRepo.FirstOrDefault(repo => repo.id == mod.id && repo.version == mod.version) != null)
                                {
                                    mod.inNebula = true;
                                }
                            });
                            var newer = isInstalled.MaxBy(x => new SemanticVersion(x.version));
                            if (newer != null && ( new SemanticVersion(newer.version) < new SemanticVersion(m.version) || newer.version == m.version && newer.lastUpdate != m.lastUpdate ))
                            {
                                await Dispatcher.UIThread.InvokeAsync(() => MainWindowViewModel.Instance?.MarkAsUpdateAvailable(m.id, true, m.version), DispatcherPriority.Background);
                            }
                            modsTcs.Remove(m);
                        }
                    }
                    else
                    {
                        modsTcs.Remove(m);
                    }
                };

                await Dispatcher.UIThread.InvokeAsync(() => MainWindowViewModel.Instance?.BulkLoadNebulaMods(modsTcs, true), DispatcherPriority.Background);

                //Engine Builds
                var builds = allModsInRepo.Where(m => m.type == ModType.engine).ToList();

                var newestNightly = string.Empty;
                var newestNightlyVersion = string.Empty;
                var newestStableVersion = string.Empty;
                

                foreach (var build in builds.ToList())
                {
                    // Check if this is the most recent build, so we can store it in the main window model.
                    if (build.id == "FSO" && build.stability == "stable" && (string.IsNullOrEmpty(newestStableVersion) || SemanticVersion.Compare(build.version, newestStableVersion) > 0)){
                        newestStableVersion = build.version;

                    } else if (build.stability == "nightly" && (string.IsNullOrEmpty(newestNightly) || string.Compare(newestNightly, build.lastUpdate) < 0)){
                        newestNightly = build.lastUpdate;
                        newestNightlyVersion = build.version;
                    }

                    //This is already installed? Remove it!
                    //Also mark it as in inNebula
                    var isInstalled = Knossos.GetInstalledBuildsList(build.id)?.FirstOrDefault(b => b.version == build.version);
                    if (isInstalled != null)
                    {
                        builds.Remove(build);
                        if (isInstalled.modData != null)
                            isInstalled.modData.inNebula = true;
                    }
                }

                // If the latest of either of these is not installed, signal the main window
                var installed = Knossos.GetInstalledBuild("FSO", newestStableVersion);
                MainWindowViewModel.Instance?.AddMostRecent((installed == null) ? newestStableVersion! : "", false);
                installed = Knossos.GetInstalledBuild("FSO", newestNightlyVersion);
                MainWindowViewModel.Instance?.AddMostRecent((installed == null) ? newestNightlyVersion! : "", true);
                MainWindowViewModel.Instance?.UpdateBuildInstallButtons();

                await Dispatcher.UIThread.InvokeAsync(() => FsoBuildsViewModel.Instance?.BulkLoadNebulaBuilds(builds), DispatcherPriority.Background);

                if (cancellationToken != null)
                {
                    cancellationToken.Dispose();
                    cancellationToken = null;
                }
                GC.Collect();
                Log.Add(Log.LogSeverity.Information, "Nebula.InitialRepoLoad()", "Loaded: " + allModsInRepo.Count() + " repo entries. Engine builds: "+ builds.Count() + ", Mods: " + modsByID.Count() + ", TCs: " + modsTcs.Count() + "." );
                return modUpdates;
            }
            catch (TaskCanceledException)
            {
                if (cancellationToken != null)
                {
                    cancellationToken.Dispose();
                    cancellationToken = null;
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.InitialRepoLoad()", ex);
                if (cancellationToken != null)
                {
                    cancellationToken.Dispose();
                    cancellationToken = null;
                }
            }
            return null;
        }

        /// <summary>
        /// Cancel repo download
        /// </summary>
        public static void CancelOperations()
        {
            if (cancellationToken != null)
            {
                cancellationToken.Cancel();
            }
        }

        /// <summary>
        /// Checks if a mod ID is used in Nebula
        /// Uses NewerModVersions array, or, if user is logged in, the nebula api if not found on NewerModVersions.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public static async Task<bool> IsModIdInNebula(string id)
        {
            //Use the mod list used for newer versions
            var exist = settings.NewerModsVersions.FirstOrDefault(x => x.Id.ToLower() == id.ToLower());
            if (exist != null)
            {
                return true;
            }
            //If we are logged in check using the api
            if (userIsLoggedIn)
            {
                return !await CheckIDAvailable(id);
            }
            return false;
        }

        /// <summary>
        /// Returns all mods versions of an id
        /// Includes private and public versions
        /// </summary>
        /// <param name="id"></param>
        /// <returns>List Mod or empty list</returns>
        public static async Task<List<Mod>> GetAllModsWithID(string? id)
        {
            var modList = new List<Mod>();
            if (privateMods != null && privateMods.Any())
            {
                modList.AddRange(privateMods.Where(m=> m != null && (m.id == id || id == null)));
            }
            try
            {
                var mods = await GetModsInRepo();
                if (mods != null)
                {
                    modList.AddRange(mods.Where(m => m != null && (m.id == id || id == null)));
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.GetAllModsWithID()", ex);
            }
            return modList;
        }

        /// <summary>
        /// Waits for read access to repo_minimal.json
        /// Returns when the file can be used or the task is cancelled
        /// </summary>
        /// <param name="filename"></param>
        private static async Task WaitForFileAccess(string filename)
        {
            try
            {
                if (!File.Exists(filename))
                {
                    return;
                }
                using (FileStream inputStream = File.Open(filename, FileMode.Open, FileAccess.Read))
                {
                    inputStream.Close();
                    return;
                }
            }
            catch (IOException)
            {
                Log.WriteToConsole("repo_minimal.json is in use. Waiting for file access...");
                await Task.Delay(500);
                if (cancellationToken != null && cancellationToken!.IsCancellationRequested)
                {
                    return;
                }
                await WaitForFileAccess(filename);
            }
        }

        /// <summary>
        /// Save nebula.json file
        /// </summary>
        public static async void SaveSettings()
        {
            try
            {
                await WaitForFileAccess(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "nebula.json");
                var encoderSettings = new TextEncoderSettings();
                encoderSettings.AllowRange(UnicodeRanges.All);

                var options = new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.Create(encoderSettings),
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "nebula.json", json, Encoding.UTF8);
                Log.Add(Log.LogSeverity.Information, "Nebula.SaveSettings()", "Nebula settings have been saved.");
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.SaveSettings()", ex);
            }
        }

        /* Nebula API handling starts here */
        #region NebulaApi
        private struct ApiReply
        {
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public bool result { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public string id { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public string token { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public string reason { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public JsonElement[] mods { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public ModMember[] members { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public int[] finished_parts { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public bool done { get; set; }
          
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public Mod mod { get; set; }
        }

        private enum ApiMethod
        {
            POST,
            PUT,
            GET
        }

        /// <summary>
        /// Nebula Api call
        /// Resource url example: "mod/release"
        /// </summary>
        /// <param name="resourceUrl"></param>
        /// <param name="data"></param>
        /// <param name="needsLogIn"></param>
        /// <param name="timeoutSeconds"></param>
        /// <param name="method"></param>
        /// <returns>
        /// apireply or null if the call failed completely
        /// It is difficult to point out exactly what the reply is because it changed from call to call
        /// Most api calls return a ApiReply.result true or false if successfull or not but this is not to case for every call
        /// </returns>
        private static async Task<ApiReply?> ApiCall(string resourceUrl, HttpContent? data, bool needsLogIn = false, int timeoutSeconds = 45, ApiMethod method = ApiMethod.POST)
        {
            //Custom mode with no nebula services
            if (Knossos.inSingleTCMode && !CustomLauncher.UseNebulaServices)
            {
                Log.Add(Log.LogSeverity.Warning, "Nebula.Trinity()", "Nebula services has been disabled.");
                return null;
            }
            try
            {
                var client = KnUtils.GetHttpClient();
                if (needsLogIn)
                {
                    if (apiUserToken == null)
                    {
                        await Login();
                        if (apiUserToken == null)
                        {
                            Log.Add(Log.LogSeverity.Warning, "Nebula.ApiCall(" + resourceUrl + ")", "An api call that needed a login token was requested, but we were unable to log into the nebula service.");
                            return null;
                        }
                    }
                    client.DefaultRequestHeaders.Add("X-KN-TOKEN", apiUserToken);
                }
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                switch (method)
                {
                    case ApiMethod.POST:
                        {
                            if (data == null)
                            {
                                throw new ArgumentNullException(nameof(data));
                            }
                            var response = await client.PostAsync(apiURL + resourceUrl, data);
                            data.Dispose();
                            if (response.IsSuccessStatusCode)
                            {
                                var jsonReply = await response.Content.ReadAsStringAsync();
                                if (jsonReply == "OK") // multiupload/part hack
                                {
                                    var reply = new ApiReply();
                                    reply.result = true;
                                    return reply;
                                }
                                if (jsonReply != null)
                                {
                                    var reply = JsonSerializer.Deserialize<ApiReply>(jsonReply);
                                    if (!reply.result && resourceUrl != "mod/check_id")
                                        Log.Add(Log.LogSeverity.Error, "Nebula.ApiCall(" + resourceUrl + ")", "An error has ocurred during nebula api POST call: " + response.StatusCode + "(" + (int)response.StatusCode + ")\n" + data);

                                    return reply;
                                }
                            }
                            else
                            {
                                /* Nebula responds with a HTTP status code 401 for expired tokens, so lets try this again */
                                if(needsLogIn && apiUserToken != null && response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                                {
                                    Log.Add(Log.LogSeverity.Information, "Nebula.ApiCall()", "Nebula: User login token is expired.");
                                    apiUserToken = null;
                                    return await ApiCall(resourceUrl, data, needsLogIn, timeoutSeconds, method);
                                }
                                /* Upload/Update/delete Mod Timeout Hack */
                                if(response.StatusCode.ToString() == "GatewayTimeout" && (resourceUrl == "mod/release" || resourceUrl == "mod/release/update" || resourceUrl == "mod/release/delete"))
                                {
                                    Log.Add(Log.LogSeverity.Warning, "Nebula.ApiCall(" + resourceUrl + ")", "During mod/release request a GatewayTimeout was recieved. This is a known issue with Nebula and while Knet handles this" +
                                        " as a success there is not an actual way to know if the api call was really successfull.");
                                    var reply = new ApiReply();
                                    reply.result = true;
                                    return reply;
                                }
                            }
                            Log.Add(Log.LogSeverity.Error, "Nebula.ApiCall(" + resourceUrl + ")", "An error has ocurred during nebula api POST call: " + response.StatusCode + "(" + (int)response.StatusCode + ")");
                        }
                        break;
                    case ApiMethod.PUT:
                        {
                            throw new NotImplementedException();
                        }
                    case ApiMethod.GET:
                        {
                            var response = await client.GetAsync(apiURL + resourceUrl);
                            if (response.IsSuccessStatusCode)
                            {
                                var json = await response.Content.ReadAsStringAsync();
                                if (json != null)
                                {
                                    var reply = JsonSerializer.Deserialize<ApiReply>(json);
                                    if (!reply.result)
                                    {
                                        if(reply.reason == "not_found")
                                            Log.Add(Log.LogSeverity.Warning, "Nebula.ApiCall(" + resourceUrl + ")", "GetMod returned: not_found");
                                        else
                                            Log.Add(Log.LogSeverity.Error, "Nebula.ApiCall(" + resourceUrl + ")", "An error has ocurred during nebula api GET call: " + response.StatusCode + "(" + (int)response.StatusCode + ")\n" + json);
                                    }

                                    return reply;
                                }
                            }
                            else
                            {
                                /* Nebula responds with a HTTP status code 401 for expired tokens, so lets try this again */
                                if (needsLogIn && apiUserToken != null && response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                                {
                                    Log.Add(Log.LogSeverity.Information, "Nebula.ApiCall()", "Nebula: User login token is expired.");
                                    apiUserToken = null;
                                    return await ApiCall(resourceUrl, data, needsLogIn, timeoutSeconds, method);
                                }
                            }
                            Log.Add(Log.LogSeverity.Error, "Nebula.ApiCall(" + resourceUrl + ")", "An error has ocurred during nebula api GET call: " + response.StatusCode + "(" + (int)response.StatusCode + ").");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.ApiCall(" + resourceUrl + ")", ex);
            }
            return null;
        }

        /// <summary>
        /// Gets Mod data from Nebula for a specific mod id and version
        /// if not found it returns reason "not_found" 
        /// </summary>
        /// <param name="modid"></param>
        /// <param name="version"></param>
        /// <returns>Mod class or null</returns>
        public static async Task<Mod?> GetModData(string modid, string version)
        {
            try
            {
                if (privateMods != null && privateMods.Any())
                {
                    foreach (Mod mod in privateMods)
                    {
                        if (mod != null && mod.id == modid && mod.version == version)
                        {
                            return mod;
                        }
                    }
                }

                //Check if Mod and version we are looking for is stored in cache
                var inCache = nebulaModDataCache.FirstOrDefault(cache => cache.HasValue && cache.Value.modID == modid && cache.Value.modVersion == version);
                if (inCache != null)
                {
                    //Get mod data result from cache
                    try
                    {
                        Log.Add(Log.LogSeverity.Information, "Nebula.GetModJson", "Data for mod ID: " + modid + " Version: " + version + " was found in cache, skipping api call.");
                        return JsonSerializer.Deserialize<Mod>(inCache.Value.modString);
                    }
                    catch (Exception ex)
                    {
                        Log.Add(Log.LogSeverity.Error, "Nebula.GetModJson", "Error while getting api data from cache, doing the api call instead. " + ex.Message);
                    }
                }

                var reply = await ApiCall("mod/json/"+modid+"/"+version, null, false, 30, ApiMethod.GET);

                if (reply.HasValue)
                {
                    if (reply.Value.result)
                    {
                        try
                        {
                            //Saving result in temp cache
                            var cache = new NebulaCache();
                            cache.modID = reply.Value.mod.id;
                            cache.modVersion = reply.Value.mod.version;
                            //Note: we need to serialize and save as string to avoid object modification deeper in the code
                            cache.modString = JsonSerializer.Serialize(reply.Value.mod);
                            nebulaModDataCache.Add(cache);
                        }
                        catch(Exception ex)
                        {
                            Log.Add(Log.LogSeverity.Error, "Nebula.GetModJson", "Error while saving returned api data to cache." + ex.Message);
                        }
                        return reply.Value.mod;
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Error, "Nebula.GetModJson", "Unable to get mod json data. ID: " + modid + " Version: " + version + " Reason: " + reply.Value.reason);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.GetModJson", ex);
            }
            return null;
        }

        /// <summary>
        /// Upload fso log string to nebula
        /// And opens the browser on the returned URL
        /// </summary>
        /// <param name="logString"></param>
        /// <returns>true if successfull, false otherwise</returns>
        public static async Task<bool> UploadLog(string logString)
        {
            try
            {
                var data = new MultipartFormDataContent()
                {
                    { new StringContent(logString), "log" }
                };
                var reply = await ApiCall("log/upload", data);
                if (reply.HasValue)
                {
                    if (reply.Value.result)
                    {
                        Log.Add(Log.LogSeverity.Information, "Nebula.UploadLog", "Uploaded log file to Nebula: " + nebulaURL + "log/" + reply.Value.id);
                        KnUtils.OpenBrowserURL(nebulaURL + "log/" + reply.Value.id);
                        return true;
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Error, "Nebula.UploadLog", "Error uploading log to Nebula, reason: " + reply.Value.reason);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.UploadLog", ex);
            }
            return false;
        }

        /// <summary>
        /// Reports a mod to nebula
        /// </summary>
        /// <param name="mod"></param>
        /// <param name="reason"></param>
        /// <returns>true if successfull, false otherwise</returns>
        public static async Task<bool> ReportMod(Mod mod, string reason)
        {
            try
            {
                var data = new MultipartFormDataContent()
                {
                    { new StringContent(mod.id), "mid" },
                    { new StringContent(mod.version), "version" },
                    { new StringContent(reason), "message" }
                };
                var reply = await ApiCall("mod/release/report", data, true);
                if (reply.HasValue)
                {
                    if (reply.Value.result)
                    {
                        Log.Add(Log.LogSeverity.Information, "Nebula.ReportMod", "Reported Mod: " + mod + " to fsnebula successfully.");
                        return true;
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Error, "Nebula.ReportMod", "Error reporting mod to nebula, reason: " + reply.Value.reason);
                    }

                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.ReportMod", ex);
            }
            return false;
        }

        /// <summary>
        /// Log in to Nebula
        /// User password is decoded from base64
        /// </summary>
        /// <param name="user"></param>
        /// <param name="password"></param>
        /// <returns>true if successfull, false otherwise</returns>
        public static async Task<bool> Login(string? user = null, string? password = null)
        {
            try
            {
                if (apiUserToken != null)
                {
                    //Already logged in
                    return true;
                }
                if (user == null)
                    user = settings.user;
                if (password == null && settings.pass != null)
                    password = KnUtils.StringDecryption(settings.pass);

                if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
                {
                    Log.Add(Log.LogSeverity.Warning, "Nebula.Login", "User or Password was null or empty.");
                    return false;
                }

                var data = new MultipartFormDataContent()
                {
                    { new StringContent(user), "user" },
                    { new StringContent(password), "password" }
                };
                var reply = await ApiCall("login", data);
                if (!reply.HasValue || string.IsNullOrEmpty(reply.Value.token))
                {
                    Log.Add(Log.LogSeverity.Warning, "Nebula.Login", "Nebula login failed.");
                    settings.logged = false;
                    apiUserToken = null;
                    SaveSettings();
                    return false;
                }
                else
                {
                    var encryptedPassword = KnUtils.StringEncryption(password);
                    if (settings.user != user || settings.pass != encryptedPassword || !settings.logged)
                    {
                        settings.user = user;
                        settings.pass = encryptedPassword;
                        settings.logged = true;
                        SaveSettings();
                    }
                    apiUserToken = reply.Value.token;
                    Log.Add(Log.LogSeverity.Information, "Nebula.Login", "Login successful, we got a token!");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.Login", ex);
            }
            return false;
        }

        /// <summary>
        /// Register a new username in nebula
        /// </summary>
        /// <param name="user"></param>
        /// <param name="password"></param>
        /// <param name="email"></param>
        /// <returns>"ok" if successfull or reason for failure string</returns>
        public static async Task<string> Register(string user, string password, string email)
        {
            try
            {
                var data = new MultipartFormDataContent()
                {
                    { new StringContent(user), "name" },
                    { new StringContent(password), "password" },
                    { new StringContent(email), "email" }
                };
                var reply = await ApiCall("register", data);
                if (reply.HasValue)
                {
                    if (reply.Value.result)
                    {
                        Log.Add(Log.LogSeverity.Information, "Nebula.Register", "Registered new user to nebula.");
                        return "ok";
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Information, "Nebula.Register", "Error registering new user to nebula. Reason: " + reply.Value.reason);
                        return reply.Value.reason;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.Register", ex);
            }
            return "unknown error";
        }

        /// <summary>
        /// Reset user password
        /// </summary>
        /// <param name="user"></param>
        /// <returns>"ok" if successfull or reason for failure string</returns>
        public static async Task<string> Reset(string user)
        {
            try
            {
                var data = new MultipartFormDataContent()
                {
                    { new StringContent(user), "user" }
                };
                var reply = await ApiCall("reset_password", data);
                if (reply.HasValue)
                {
                    if (reply.Value.result)
                    {
                        Log.Add(Log.LogSeverity.Information, "Nebula.Reset", "Requested password reset to nebula for username: " + user);
                        return "ok";
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Information, "Nebula.Reset", "Error requesting password reset. Reason: " + reply.Value.reason);
                        return reply.Value.reason;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.Reset", ex);
            }
            return "unknown error";
        }

        /// <summary>
        /// Clears saved user data
        /// </summary>
        public static void LogOff()
        {
            settings.logged = false;
            apiUserToken = null;
            settings.user = null;
            settings.pass = null;
            editableIds = null;
            privateMods = null;
            SaveSettings();
        }

        /// <summary>
        /// Checks if the MODID is available in Nebula
        /// (Needs to be logged in)
        /// </summary>
        /// <param name="id"></param>
        /// <param name="title"></param>
        /// <returns>
        /// true if MODID is available, false if it is already in use
        /// </returns>
        public static async Task<bool> CheckIDAvailable(string id, string title = "None")
        {
            try
            {
                var data = new MultipartFormDataContent()
                {
                    { new StringContent(id), "id" },
                    { new StringContent(title), "title" }
                };
                //TODO: A potencially huge problem, for Nebula, modid is case sensitive!
                var reply = await ApiCall("mod/check_id", data, true);
                if (reply.HasValue)
                {
                    Log.Add(Log.LogSeverity.Information, "Nebula.CheckID", "Check Mod ID:" + id + " is available in Nebula: " + reply.Value.result);
                    return reply.Value.result;
                }
                else
                {
                    Log.Add(Log.LogSeverity.Error, "Nebula.CheckID", "Unable to check mod in in Nebula.");
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.CheckID", ex);
            }
            return false;
        }

        /// <summary>
        /// Checks if the user has write permissions to a modid
        /// </summary>
        /// <param name="id"></param>
        /// <returns>true if user has write access, false if not or not logged in</returns>
        public static async Task<bool> IsModEditable(string id)
        {
            try
            {
                var data = new MultipartFormDataContent()
                {
                    { new StringContent(id), "mid" }
                };

                var reply = await ApiCall("mod/is_editable", data, true);
                if (reply.HasValue)
                {
                    Log.Add(Log.LogSeverity.Information, "Nebula.IsModEditable", "Is mod id:" + id + " editable? " + reply.Value.result);
                    return reply.Value.result;
                }
                else
                {
                    Log.Add(Log.LogSeverity.Error, "Nebula.IsModEditable", "Unable to check if mod is editable.");
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.IsModEditable", ex);
            }
            return false;
        }

        /// <summary>
        /// Get an array of mods that the user has write access to.
        /// Only loaded once per session
        /// </summary>
        /// <returns>An array of Mods or null</returns>
        public static async Task<string[]?> GetEditableModIDs()
        {
            try
            {
                if(editableIds != null)
                    return editableIds;

                var reply = await ApiCall("mod/editable", null, true, 45, ApiMethod.GET);
                if (reply.HasValue)
                { 
                    if (reply.Value.mods != null && reply.Value.mods.Any())
                    {
                        var ids = reply.Value.mods.Select(x => x.Deserialize<string>()!).ToArray()!;
                        Log.Add(Log.LogSeverity.Information, "Nebula.GetEditableMods", "Editable mod ids in Nebula: " + string.Join(",", ids));
                        editableIds = ids;
                        return ids;
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Information, "Nebula.GetEditableMods", "Returned an empty array.");
                    }
                }
                else
                {
                    Log.Add(Log.LogSeverity.Error, "Nebula.GetEditableMods", "Unable to check editable mod list.");
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.GetEditableMods", ex);
            }
            return null;
        }

        /// <summary>
        /// Get an array of private mods that the user has access to.
        /// </summary>
        /// <returns>An array of mods</returns>
        public static async Task<Mod[]?> GetPrivateMods(bool tryUseCache = false)
        {
            try
            {
                if (tryUseCache && privateMods != null)
                    return privateMods;

                var reply = await ApiCall("mod/list_private", null, true, 45, ApiMethod.GET);
                if (reply.Value.mods != null && reply.Value.mods.Any())
                {
                    var mods = reply.Value.mods.Select(x => x.Deserialize<Mod>()!).ToArray()!;
                    foreach (var mod in mods)
                    {
                        Log.Add(Log.LogSeverity.Information, "Nebula.GetPrivateMods", mod.ToString());
                    }
                    return mods;
                }
                else
                {
                    Log.Add(Log.LogSeverity.Information, "Nebula.GetPrivateMods", "Returned an empty array.");
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.GetPrivateMods", ex);
            }
            return null;
        }

        /// <summary>
        /// Gets all members in a mod
        /// Needs to be logged in
        /// </summary>
        /// <param name="modid"></param>
        /// <returns>An ModMember[] array or null if failed.</returns>
        public static async Task<ModMember[]?> GetTeamMembers(string modid)
        {
            try
            {
                var data = new MultipartFormDataContent()
                {
                    { new StringContent(modid), "mid" }
                };

                if (!userIsLoggedIn){
                    
                    return null;
                }

                var reply = await ApiCall("mod/team/fetch", data, true);
                if (reply.HasValue)
                {
                    if (reply.Value.result)
                    {
                        if (reply.Value.members != null)
                        {
                            var members = string.Empty;
                            foreach (var item in reply.Value.members)
                            {
                                members += item.user + "(" + item.role.ToString() + ") ";
                            }
                            Log.Add(Log.LogSeverity.Information, "Nebula.GetTeamMembers", "Mod ID: " + modid + " members: " + members);
                        }
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Error, "Nebula.GetTeamMembers", "Error while getting mod members: " + reply.Value.reason);
                    }
                    return reply.Value.members;
                }
                else
                {
                    Log.Add(Log.LogSeverity.Error, "Nebula.GetTeamMembers", "Unable to check if this mod has team members.");
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.GetTeamMembers", ex);
            }
            return null;
        }

        /// <summary>
        /// Updates mod team members, the entire list must be passed here, owner included.
        /// </summary>
        /// <param name="modid"></param>
        /// <param name="members"></param>
        /// <returns>"ok" if successfull, a reason if we were unable to update members or null if the call failed</returns>
        public static async Task<string?> UpdateTeamMembers(string modid, ModMember[] members)
        {
            try
            {
                var newMembers = JsonSerializer.Serialize(members);

                var callString = "{\"mid\":\"" + modid + "\",\"members\": " + newMembers + "}";
                var reply = await ApiCall("mod/team/update", new StringContent(callString, Encoding.UTF8, "application/json"), true);
                if (reply.HasValue)
                {
                    if (reply.Value.result)
                    {
                        Log.Add(Log.LogSeverity.Information, "Nebula.UpdateTeamMembers", "Updated mod members for mod: " + modid + " New Members: " + newMembers);
                        return "ok";
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Error, "Nebula.UpdateTeamMembers", "Error while updating mod members: " + reply.Value.reason + " Passed members: " + newMembers);
                        return reply.Value.reason;
                    }
                }
                else
                {
                    Log.Add(Log.LogSeverity.Error, "Nebula.UpdateTeamMembers", "Unable to update mod team members.");
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.UpdateTeamMembers", ex);
            }
            return null;
        }

        /// <summary>
        /// Deletes a given Mod Version release from Nebula
        /// Needs to be logged in and have write permissions
        /// </summary>
        /// <param name="mod"></param>
        /// <returns>returns "ok" if successfull, a reason if we were unable to delete the mod version or null if the call failed</returns>
        public static async Task<string?> DeleteModVersion(Mod mod)
        {
            try
            {
                var data = new MultipartFormDataContent()
                {
                    { new StringContent(mod.id), "mid" },
                    { new StringContent(mod.version), "version" }
                };

                var reply = await ApiCall("mod/release/delete", data, true, 300);
                if (reply.HasValue)
                {
                    if (reply.Value.result)
                    {
                        Log.Add(Log.LogSeverity.Information, "Nebula.DeleteModVersion", "Deleted mod/version : " + mod + " from Nebula.");
                        return "ok";
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Error, "Nebula.DeleteModVersion", "Error while deleting mod/version: " + mod + " Reason: " + reply.Value.reason);
                        return reply.Value.reason;
                    }
                }
                else
                {
                    Log.Add(Log.LogSeverity.Error, "Nebula.DeleteModVersion", "Unable to delete mod version. ");
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.DeleteModVersion", ex);
            }
            return null;
        }

        /// <summary>
        /// Checks if a file is already uploaded to Nebula
        /// Checksum or contentChecksum must be provided
        /// TODO: Find out the difference
        /// </summary>
        /// <param name="checksum"></param>
        /// <param name="contentChecksum"></param>
        /// <returns>true if it exist, false otherwise</returns>
        public static async Task<bool> IsFileUploaded(string? checksum = null, string? contentChecksum = null)
        {
            try
            {
                if (checksum == null && contentChecksum == null)
                    throw new ArgumentNullException(nameof(checksum) + " " + nameof(contentChecksum));

                var data = new MultipartFormDataContent();
                if (checksum != null)
                {
                    data.Add(new StringContent(checksum), "checksum");
                }
                else
                {
                    data.Add(new StringContent(contentChecksum!), "content_checksum");
                }

                var reply = await ApiCall("upload/check", data, true);
                if (reply.HasValue)
                {
                    Log.Add(Log.LogSeverity.Information, "Nebula.IsFileUploaded", "Is file: " + checksum + contentChecksum + " uploaded? " + reply.Value.result);
                    return reply.Value.result;
                }
                else
                {
                    Log.Add(Log.LogSeverity.Error, "Nebula.IsFileUploaded", "Unable to check if a file is uploaded");
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.IsFileUploaded", ex);
            }
            return false;
        }

        /// <summary>
        /// Uploads an image file to Nebula
        /// It also checks if the image is already uploaded
        /// 10MB max limit
        /// </summary>
        /// <param name="path"></param>
        /// <returns>returns the file hash if successfull or null if failed</returns>
        public static async Task<string?> UploadImage(string path)
        {
            try
            {
                if (!File.Exists(path))
                    throw new Exception("File " + path + " dosent exist.");

                using (FileStream? file = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    if (file.Length > 10485760)
                        throw new Exception("File " + path + " is over the 10MB limit.");

                    var checksumString = await KnUtils.GetFileHash(path);

                    if (checksumString == null)
                        return null;

                    if (await IsFileUploaded(checksumString))
                    {
                        //Already uploaded
                        return checksumString;
                    }

                    using (MemoryStream ms = new MemoryStream())
                    {
                        file.CopyTo(ms);
                        var imgBytes = ms.ToArray();

                        var data = new MultipartFormDataContent()
                        {
                            { new StringContent(checksumString), "checksum" },
                            { new ByteArrayContent(imgBytes, 0, imgBytes.Length), "file", "upload" }
                        };

                        var reply = await ApiCall("upload/file", data, true, 600);
                        if (reply.HasValue)
                        {
                            if (reply.Value.result)
                            {
                                Log.Add(Log.LogSeverity.Information, "Nebula.UploadImage", "File: " + path + " uploaded to Nebula with checksum: " + checksumString);
                                return checksumString;
                            }
                            else
                            {
                                Log.Add(Log.LogSeverity.Error, "Nebula.UploadImage", "Unable to upload File: " + path + " to Nebula. Reason: " + reply.Value.reason);
                            }
                        }
                        else
                        {
                            Log.Add(Log.LogSeverity.Error, "Nebula.UploadImage", "Unable upload image file: " + path);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.UploadImage", ex);
            }
            return null;
        }

        /// <summary>
        /// Does a metadata check to Nebula
        /// attachments, screenshots and banner must be clear
        /// (thats is what old-knossos does)
        /// </summary>
        /// <param name="mod"></param>
        /// <returns>
        /// "ok" if successfull, a reason if it finds a problem, and null if modid is not found(api call returns 404 in that case) or any other problem
        /// "duplicated version" if version is already uploaded
        /// </returns>
        public static async Task<string?> PreflightCheck(Mod mod)
        {
            try
            {
                var reply = await ApiCall("mod/release/preflight", new StringContent(JsonSerializer.Serialize(mod), Encoding.UTF8, "application/json"), true);
                if (reply.HasValue)
                {
                    if (reply.Value.result)
                    {
                        Log.Add(Log.LogSeverity.Information, "Nebula.PreflightCheck", "PreflightCheck: " + mod + " OK!");
                        return "ok";
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Error, "Nebula.PreflightCheck", "PreflightCheck: " + mod + " error. Reason: " + reply.Value.reason);
                        return reply.Value.reason;
                    }
                }
                else
                {
                    Log.Add(Log.LogSeverity.Error, "Nebula.PreflightCheck", "Unable to do a preflight check. ");
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.PreflightCheck", ex);
            }
            return null;
        }

        /// <summary>
        /// Creates a new mod in Nebula (if mod id does not exist)
        /// tile image hash must be included here too if set
        /// Only id, title, type, parent, tile, first_release fields
        /// </summary>
        /// <param name="mod"></param>
        /// <returns>"ok" if successfull, a reason if it fails (maybe), null if api call fails</returns>
        public static async Task<string?> CreateMod(Mod mod)
        {
            try
            {
                var json = JsonSerializer.Serialize(mod);
                var reply = await ApiCall("mod/create", new StringContent(json, Encoding.UTF8, "application/json"), true);
                if (reply.HasValue)
                {
                    if (reply.Value.result)
                    {
                        Log.Add(Log.LogSeverity.Information, "Nebula.CreateMod", "CreateMod: " + mod + " OK!");
                        return "ok";
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Error, "Nebula.CreateMod", "CreateMod: " + mod + " error. Reason: " + reply.Value.reason);
                        return reply.Value.reason;
                    }
                }
                else
                {
                    Log.Add(Log.LogSeverity.Error, "Nebula.CreateMod", "Unable to do create a new mod. ");
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.CreateMod", ex);
            }
            return null;
        }

        /// <summary>
        /// This updates mod tile image and title
        /// </summary>
        /// <param name="mod"></param>
        /// <returns>"ok" if successfull</returns>
        public static async Task<string?> UpdateMod(Mod mod)
        {
            try
            {
                var json = JsonSerializer.Serialize(mod);
                var reply = await ApiCall("mod/update", new StringContent(json, Encoding.UTF8, "application/json"), true);
                if (reply.HasValue)
                {
                    if (reply.Value.result)
                    {
                        Log.Add(Log.LogSeverity.Information, "Nebula.UpdateMod", "UpdateMod: " + mod + " OK!");
                        return "ok";
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Error, "Nebula.UpdateMod", "UpdateMod: " + mod + " error. Reason: " + reply.Value.reason);
                        return reply.Value.reason;
                    }
                }
                else
                {
                    Log.Add(Log.LogSeverity.Error, "Nebula.UpdateMod", "Unable to update a mod. ");
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.UpdateMod", ex);
            }
            return null;
        }

        /// <summary>
        /// Releases mod to Nebula
        /// </summary>
        /// <param name="mod"></param>
        /// <returns>"ok" if successfull, a reason if it fails (maybe), and null if modid is not found(api call returns 404 in that case) or any other problem</returns>
        public static async Task<string?> ReleaseMod(Mod mod)
        {
            try
            {
                var json = JsonSerializer.Serialize(mod);
                var reply = await ApiCall("mod/release", new StringContent(json, Encoding.UTF8, "application/json"), true, 300);
                if (reply.HasValue)
                {
                    if (reply.Value.result)
                    {
                        Log.Add(Log.LogSeverity.Information, "Nebula.ReleaseMod", "ReleaseMod: " + mod + " OK!");
                        return "ok";
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Error, "Nebula.ReleaseMod", "ReleaseMod: " + mod + " error. Reason: " + reply.Value.reason);
                        return reply.Value.reason;
                    }
                }
                else
                {
                    Log.Add(Log.LogSeverity.Error, "Nebula.ReleaseMod", "Unable to do release mod. ");
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.ReleaseMod", ex);
            }
            return null;
        }

        /// <summary>
        /// Updates Released mod metadata
        /// </summary>
        /// <param name="mod"></param>
        /// <returns>"ok" if successfull, a reason if it fails (maybe), and null if modid is not found(api call returns 404 in that case) or any other problem</returns>
        public static async Task<string?> UpdateMetaData(Mod mod)
        {
            try
            {
                var json = JsonSerializer.Serialize(mod);
                var reply = await ApiCall("mod/release/update", new StringContent(json, Encoding.UTF8, "application/json"), true, 300);
                if (reply.HasValue)
                {
                    if (reply.Value.result)
                    {
                        Log.Add(Log.LogSeverity.Information, "Nebula.UpdateMetaData", "UpdateMetaData: " + mod + " OK!");
                        return "ok";
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Error, "Nebula.UpdateMetaData", "UpdateMetaData: " + mod + " error. Reason: " + reply.Value.reason);
                        return reply.Value.reason;
                    }
                }
                else
                {
                    Log.Add(Log.LogSeverity.Error, "Nebula.UpdateMetaData", "Unable to update mod meta data. ");
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Nebula.UpdateMetaData", ex);
            }
            return null;
        }

        /* Multipart Uploader */
        #region MultiPartUploader
        /// <summary>
        /// Multipart Nebula file uploader
        /// Call .Upload() to start
        /// Supports auto upload-resume and checks if the file is already uploaded
        /// Support upload cancel via token
        /// </summary>
        public class MultipartUploader
        {
            private static readonly int maxUploadParallelism = 3;
            private static readonly int maxUploadRetries = 4;
            private static readonly long partMaxSize = 10485760; //10MB
            private List<FilePart> fileParts = new List<FilePart>();
            private CancellationTokenSource cancellationTokenSource;
            private Action<string, int, int>? progressCallback;
            private string? fileChecksum;
            private string fileFullPath;
            private long fileLenght = 0;
            private bool completed = false;
            private bool verified = false;

            public MultipartUploader(string filePath, CancellationTokenSource? cancellationTokenSource, Action<string, int, int>? progressCallback)
            {
                fileFullPath = filePath;
                this.progressCallback = progressCallback;
                if (cancellationTokenSource != null)
                {
                    this.cancellationTokenSource = cancellationTokenSource;
                }
                else
                {
                    this.cancellationTokenSource = new CancellationTokenSource();
                }
                fileLenght = new FileInfo(filePath).Length;
                var partIdx = 0;
                while(partIdx * partMaxSize < fileLenght)
                {
                    var partLengh = fileLenght - ( partIdx * partMaxSize ) < partMaxSize ? fileLenght - (partIdx * partMaxSize) : partMaxSize;
                    fileParts.Add(new FilePart(this, partIdx, partIdx * partMaxSize, partLengh));
                    partIdx++;
                }
            }

            /// <summary>
            /// Starts the upload process
            /// </summary>
            /// <returns>true if the upload completed successfully or file is already uploaded, false otherwise</returns>
            /// <exception cref="ObjectDisposedException"></exception>
            /// <exception cref="TaskCanceledException"></exception>
            public async Task<bool> Upload()
            {
                if (cancellationTokenSource.IsCancellationRequested)
                    throw new TaskCanceledException();

                if (progressCallback != null)
                    progressCallback.Invoke("Starting Checkup...", 0, fileParts.Count());

                var start = await Start();

                int maxProgress = fileParts.Count();
                int progress = 0;

                if (!start)
                {
                    if(progressCallback != null)
                        progressCallback.Invoke("Error while getting starting response.", 0, maxProgress);
                    return false;
                }
                
                //Upload was completed previously?
                if(completed)
                {
                    if (progressCallback != null)
                        progressCallback.Invoke("Already Uploaded", 1, 1);
                    return true;
                }

                await Parallel.ForEachAsync(fileParts, new ParallelOptions { MaxDegreeOfParallelism = maxUploadParallelism }, async (part, token) =>
                {
                    if (cancellationTokenSource.IsCancellationRequested)
                        throw new TaskCanceledException();
                    if (progressCallback != null)
                        progressCallback.Invoke("Part: " + progress + " / " + maxProgress, progress, maxProgress);
                    var partCompleted = false;
                    var attempt = 1;
                    do
                    {
                        if (attempt > 1)
                            await Task.Delay(500);
                        var result = await part.Upload();
                        await Task.Delay(200);
                        if (result)
                        {
                            partCompleted = await part.Verify();
                            await Task.Delay(100);
                        }
                    } 
                    while ( partCompleted != true && attempt++ <= maxUploadRetries);

                    if (partCompleted)
                    {
                        progress++;
                        if (progressCallback != null)
                            progressCallback.Invoke("Part: " + progress + " / " + maxProgress, progress, maxProgress);
                    }
                    else
                    {
                        cancellationTokenSource.Cancel();
                    }
                });

                completed = true;

                if (progressCallback != null)
                    progressCallback.Invoke("Verifying Upload...", maxProgress, maxProgress);

                if (cancellationTokenSource.IsCancellationRequested)
                    throw new TaskCanceledException();

                verified = await Finish();

                if (verified && progressCallback != null)
                    progressCallback.Invoke("Verify: " + verified, maxProgress, maxProgress);

                return verified;
            }

            /// <summary>
            /// Call to complete the upload process
            /// Nebula will check the complete file checksum here
            /// </summary>
            /// <returns>true if everything is fine, false otherwise</returns>
            private async Task<bool> Finish()
            {
                var data = new MultipartFormDataContent()
                {
                    { new StringContent(fileChecksum!), "id" },
                    { new StringContent(fileChecksum!), "checksum" },
                    { new StringContent("None"), "content_checksum" },
                    { new StringContent("None"), "vp_checksum" }
                };

                var reply = await ApiCall("multiupload/finish", data, true, 160);
                if (reply.HasValue)
                {
                    if (!reply.Value.result)
                    {
                        Log.Add(Log.LogSeverity.Error, "MultipartUploader.Finish", "Unable to multi part upload process to Nebula. Reason: " + reply.Value.reason);
                        if (progressCallback != null)
                            progressCallback.Invoke("Verify: " + reply.Value.reason, 0, 1);
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Information, "MultipartUploader.Finish", "Multiupload: File uploaded to Nebula! " + fileFullPath);
                    }
                    completed = reply.Value.result;
                    return reply.Value.result;
                }
                return false;
            }

            /// <summary>
            /// Starts the file upload process
            /// Here we pass file checksum, file size and number of parts to Nebula
            /// </summary>
            /// <returns>true if everything is fine, false otherwise</returns>
            private async Task<bool> Start()
            {
                await GetChecksum();
                var data = new MultipartFormDataContent()
                {
                    { new StringContent(fileChecksum!), "id" },
                    { new StringContent(fileLenght.ToString()), "size" },
                    { new StringContent(fileParts.Count().ToString()), "parts" }
                };

                var reply = await ApiCall("multiupload/start", data, true);
                if (reply.HasValue)
                {
                    if (!reply.Value.result)
                    {
                        Log.Add(Log.LogSeverity.Error, "MultipartUploader.Start", "Unable to multi part upload process to Nebula. Reason: " + reply.Value.reason);
                    }
                    completed = reply.Value.done;
                    if(!completed && reply.Value.finished_parts != null)
                    {
                        foreach(var finished in reply.Value.finished_parts)
                        {
                            foreach (var part in fileParts.ToList())
                            {
                                if(part.GetID() == finished)
                                {
                                    var verify = await part.Verify();
                                    if (verify)
                                    {
                                        fileParts.Remove(part);
                                        Log.Add(Log.LogSeverity.Information, "MultipartUploader.Start", "Removing part id " + part.GetID() + " because it was already uploaded. Max parts: " + fileParts.Count());
                                    }
                                    else
                                    {
                                        Log.Add(Log.LogSeverity.Warning, "MultipartUploader.Start", "Part id " + part.GetID() + " is already uploaded, but the checksum is incorrect, re-uploading. Max parts: " + fileParts.Count());
                                    }
                                }
                            }
                        }
                    }
                    return reply.Value.result;
                }
                return false;
            }

            /// <summary>
            /// Return full path to file
            /// </summary>
            /// <returns>fullpath string</returns>
            public string GetFilePath()
            {
                return fileFullPath;
            }

            /// <summary>
            /// Get file hash
            /// </summary>
            /// <returns>file checksum or null</returns>
            /// <exception cref="ObjectDisposedException"></exception>
            internal async Task<string?> GetChecksum()
            {
                if(fileChecksum != null)
                {
                    return fileChecksum;
                }
                else
                {
                    fileChecksum = await KnUtils.GetFileHash(fileFullPath);

                    return fileChecksum;
                }
            }
        }

        internal class FilePart
        {
            private MultipartUploader uploader;
            private string? fileID;
            private byte[]? partBytes;
            private string? partChecksum;
            private long fileOffset;
            private long partLength;
            private int partIndex = 0;
            private bool verified = false;
            private bool completed = false;

            public FilePart(MultipartUploader uploader, int partID, long offset, long partLength)
            {
                this.partIndex = partID;
                this.uploader = uploader;
                this.partLength = partLength;
                fileOffset = offset;
            }

            private async Task ReadData()
            {
                if (partBytes == null)
                {
                    using (var fs = new FileStream(uploader.GetFilePath(), FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        fs.Seek(fileOffset, SeekOrigin.Begin);
                        partBytes = new byte[partLength];
                        if (await fs.ReadAsync(partBytes, 0, partBytes.Length) != partLength)
                            throw new Exception("Expected to read " + partLength + " bytes, but we read " + partBytes.Length + " instead.");
                    }
                }
            }

            public async Task<bool> Upload()
            {
                completed = false;
                if(fileID == null)
                {
                    fileID = await uploader.GetChecksum();
                }

                if (fileID != null)
                {
                    await ReadData();

                    if (partBytes != null)
                    {
                        var data = new MultipartFormDataContent()
                        {
                            { new StringContent(fileID), "id" },
                            { new StringContent(partIndex.ToString()), "part" },
                            { new ByteArrayContent(partBytes, 0, partBytes.Length), "file", "upload" }
                        };

                        var reply = await ApiCall("multiupload/part", data, true, 600);
                        if (reply.HasValue)
                        {
                            if (!reply.Value.result)
                            {
                                Log.Add(Log.LogSeverity.Error, "FilePart.Upload", "Unable to upload file part to Nebula. Reason: " + reply.Value.reason);
                            }
                            completed = reply.Value.result;
                            return reply.Value.result;
                        }
                    }
                }
                Log.Add(Log.LogSeverity.Error, "FilePart.Upload", "Unable upload file part.");
                return false;
            }

            public int GetID()
            {
                return partIndex;
            }

            public async Task<bool> Verify()
            {
                if (fileID == null)
                    fileID = await uploader.GetChecksum();

                await GetChecksum();
                if (fileID != null && partChecksum != null)
                {
                    var data = new MultipartFormDataContent()
                    {
                        { new StringContent(fileID), "id" },
                        { new StringContent(partIndex.ToString()), "part" },
                        { new StringContent(partChecksum), "checksum" },
                    };

                    var reply = await ApiCall("multiupload/verify_part", data, true);
                    if (reply.HasValue)
                    {
                        if (!reply.Value.result)
                        {
                            Log.Add(Log.LogSeverity.Error, "FilePart.Verify", "Unable to verify file part. Reason: " + reply.Value.reason);
                        }
                        verified = reply.Value.result;
                        return reply.Value.result;
                    }
                }
                Log.Add(Log.LogSeverity.Error, "FilePart.Verify", "Unable to verify file part.");
                return false;
            }

            public async Task<string> GetChecksum()
            {
                if(partChecksum == null)
                {
                    if(partBytes == null)
                    {
                        await ReadData();
                    }
                }
                else
                {
                    return partChecksum;
                }
                if (partBytes != null)
                {
                    using (SHA256 sha256 = SHA256.Create())
                    {
                        partChecksum = BitConverter.ToString(sha256.ComputeHash(partBytes)).Replace("-", String.Empty).ToLower();
                        return partChecksum;
                    }
                }
                else
                {
                    throw new Exception("The byte array was null while attemping to generate a sha256 hash.");
                }
            }

            public bool IsCompleted()
            {
                return completed;
            }

            public bool IsVerified()
            { 
                return verified; 
            }
        }
        #endregion
        #endregion
    }
}
