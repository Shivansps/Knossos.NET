using BBcodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Knossos.NET.ViewModels
{
    public partial class ModMediaItemViewModel : ViewModelBase
    {
        private readonly Action<ModMediaItemViewModel> open;
        [ObservableProperty] private string? imagePath;
        [ObservableProperty] private bool isLoading = true;
        [ObservableProperty] private bool unavailable;
        public string Title { get; }
        public string? VideoUrl { get; }
        public bool IsVideo => VideoUrl != null;

        public ModMediaItemViewModel(string title, string? videoUrl, Action<ModMediaItemViewModel> open)
        {
            Title = title;
            VideoUrl = videoUrl;
            this.open = open;
        }

        [RelayCommand]
        private void Open() => open(this);
    }

    /// <summary>Loads the current version without allowing stale requests to replace newer content.</summary>
    public partial class ModInformationViewModel : ViewModelBase, IDisposable
    {
        private readonly ModPropertiesViewModel owner;
        private CancellationTokenSource? loadCancellation;
        private Mod? mod;
        [ObservableProperty] private string name = string.Empty;
        [ObservableProperty] private string subtitle = string.Empty;
        [ObservableProperty] private string released = "—";
        [ObservableProperty] private string lastUpdated = "—";
        [ObservableProperty] private string owners = "—";
        [ObservableProperty] private string description = string.Empty;
        [ObservableProperty] private string? banner;
        [ObservableProperty] private bool hasBanner;
        [ObservableProperty] private bool isLoading;
        [ObservableProperty] private string loadError = string.Empty;
        [ObservableProperty] private bool canLaunch;
        [ObservableProperty] private bool canUseEditors;
        [ObservableProperty] private bool isReleaseAvailable;
        [ObservableProperty] private bool isDebugAvailable;
        [ObservableProperty] private bool isFred2Available;
        [ObservableProperty] private bool isFred2DebugAvailable;
        [ObservableProperty] private bool isQtFredAvailable;
        [ObservableProperty] private bool isQtFredDebugAvailable;
        [ObservableProperty] private bool forumAvailable;
        [ObservableProperty] private bool canReport;
        [ObservableProperty] private bool canDelete;
        [ObservableProperty] private bool showVideos;
        public ModPropertiesViewModel Properties => owner;
        public ObservableCollection<ModMediaItemViewModel> Screenshots { get; } = new();
        public ObservableCollection<ModMediaItemViewModel> Videos { get; } = new();
        public IEnumerable<ModMediaItemViewModel> VisibleMedia => ShowVideos ? Videos : Screenshots;
        public string ScreenshotTab => "Screenshots (" + Screenshots.Count + ")";
        public string VideoTab => "Videos (" + Videos.Count + ")";
        public bool HasMedia => Screenshots.Count + Videos.Count > 0;
        public bool HasVisibleMedia => ShowVideos ? Videos.Count > 0 : Screenshots.Count > 0;
        public bool HasLoadError => !string.IsNullOrEmpty(LoadError);

        public ModInformationViewModel() : this(new ModPropertiesViewModel()) { }
        public ModInformationViewModel(ModPropertiesViewModel properties) { owner = properties; }

        partial void OnShowVideosChanged(bool value)
        {
            OnPropertyChanged(nameof(VisibleMedia));
            OnPropertyChanged(nameof(HasVisibleMedia));
        }
        partial void OnLoadErrorChanged(string value) => OnPropertyChanged(nameof(HasLoadError));

        [RelayCommand]
        private void SelectMediaTab(string tab) => ShowVideos = tab == "videos";
        [RelayCommand]
        private Task RetryLoadAsync() => LoadAsync(owner.ActiveVersion);

        public async Task LoadAsync(Mod selectedMod)
        {
            loadCancellation?.Cancel();
            loadCancellation?.Dispose();
            loadCancellation = new CancellationTokenSource();
            var token = loadCancellation.Token;
            mod = selectedMod;
            Name = mod.title;
            Subtitle = mod.type == ModType.engine ? "FreeSpace Open engine build" : "Explore this mod in the FreeSpace universe";
            Description = string.Empty;
            Banner = null;
            HasBanner = false;
            Screenshots.Clear();
            Videos.Clear();
            ShowVideos = false;
            NotifyMedia();
            LoadError = string.Empty;
            IsLoading = true;
            SetMetadata();
            RefreshLaunchAvailability();
            try
            {
                if (mod.modSource != ModSource.local && !mod.installed && !string.IsNullOrEmpty(mod.id))
                {
                    var loaded = await Task.Run(mod.LoadFulLNebulaData);
                    if (token.IsCancellationRequested) return;
                    if (!loaded) LoadError = "Some information could not be loaded from Nebula. Check your connection and retry.";
                }
                token.ThrowIfCancellationRequested();
                SetMetadata();
                var rawDescription = mod.description;
                var formatted = await Task.Run(() => FormatDescription(rawDescription), token);
                token.ThrowIfCancellationRequested();
                Description = formatted;
                var tasks = new List<Task> { LoadBannerAsync(mod, token) };
                foreach (var source in mod.screenshots ?? Array.Empty<string>())
                {
                    var item = new ModMediaItemViewModel("Screenshot " + (Screenshots.Count + 1), null,
                        media => owner.ShowMedia(media, Screenshots.ToList()));
                    Screenshots.Add(item);
                    tasks.Add(LoadMediaAsync(item, mod.fullPath, source, token));
                }
                foreach (var source in mod.videos ?? Array.Empty<string>())
                {
                    var id = GetYouTubeVideoId(source);
                    if (id == null) continue;
                    var url = "https://www.youtube.com/watch?v=" + id;
                    var item = new ModMediaItemViewModel("YouTube video " + (Videos.Count + 1), url,
                        media => owner.ShowMedia(media, Videos.ToList()));
                    Videos.Add(item);
                    tasks.Add(LoadMediaAsync(item, string.Empty, "https://img.youtube.com/vi/" + id + "/hqdefault.jpg", token));
                }
                if (Screenshots.Count == 0 && Videos.Count > 0) ShowVideos = true;
                NotifyMedia();
                // Text and actions are usable while the cached media finishes loading.
                IsLoading = false;
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    LoadError = "The mod information could not be loaded. Please retry.";
                    Log.Add(Log.LogSeverity.Warning, "ModInformation.Load", ex);
                }
            }
            finally { if (!token.IsCancellationRequested) IsLoading = false; }
        }

        private void SetMetadata()
        {
            Released = string.IsNullOrWhiteSpace(mod?.firstRelease) ? "—" : mod.firstRelease;
            LastUpdated = string.IsNullOrWhiteSpace(mod?.lastUpdate) ? "—" : mod.lastUpdate;
            Owners = mod?.owners?.Any() == true ? string.Join(", ", mod.owners) : "—";
            ForumAvailable = Uri.TryCreate(mod?.releaseThread, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
            CanReport = mod?.modSource != ModSource.local && (!CustomLauncher.IsCustomMode || CustomLauncher.UseNebulaServices);
            CanDelete = mod?.installed == true && !mod.devMode && mod.id != "FS2";
        }

        public void RefreshLaunchAvailability()
        {
            CanLaunch = mod?.installed == true && mod.type != ModType.engine;
            CanUseEditors = CanLaunch && !KnUtils.IsAndroid && !KnUtils.IsBrowser;
            IsReleaseAvailable = IsDebugAvailable = false;
            IsFred2Available = IsFred2DebugAvailable = false;
            IsQtFredAvailable = IsQtFredDebugAvailable = false;
            if (!CanLaunch || mod == null) return;
            try
            {
                var available = mod.GetAvailableExecutables();
                IsReleaseAvailable = available.Contains(FsoExecType.Release);
                IsDebugAvailable = available.Contains(FsoExecType.Debug);
                IsFred2Available = CanUseEditors && available.Contains(FsoExecType.Fred2);
                IsFred2DebugAvailable = CanUseEditors && available.Contains(FsoExecType.Fred2Debug);
                IsQtFredAvailable = CanUseEditors && available.Contains(FsoExecType.QtFred);
                IsQtFredDebugAvailable = CanUseEditors && available.Contains(FsoExecType.QtFredDebug);
            }
            catch (Exception ex) { Log.Add(Log.LogSeverity.Warning, "ModInformation.Executables", ex); }
        }

        private static string FormatDescription(string? text)
        {
            var html = string.IsNullOrWhiteSpace(text) ? "No description has been provided for this version." : BBCode.ConvertToHtml(text, BBCode.BasicRules);
            return "<body style='white-space:pre-line;color:#D7E0E8;font-family:Arial;font-size:14px;text-align:left;'>" + html + "</body>";
        }

        private void NotifyMedia()
        {
            OnPropertyChanged(nameof(VisibleMedia));
            OnPropertyChanged(nameof(ScreenshotTab));
            OnPropertyChanged(nameof(VideoTab));
            OnPropertyChanged(nameof(HasMedia));
            OnPropertyChanged(nameof(HasVisibleMedia));
        }

        private async Task LoadBannerAsync(Mod source, CancellationToken token)
        {
            var image = await ResolveImageAsync(source.fullPath, source.banner, token);
            if (token.IsCancellationRequested) return;
            Banner = image;
            HasBanner = image != null;
        }

        private static async Task LoadMediaAsync(ModMediaItemViewModel item, string directory, string source, CancellationToken token)
        {
            var image = await ResolveImageAsync(directory, source, token);
            if (token.IsCancellationRequested) return;
            item.ImagePath = image;
            item.Unavailable = image == null;
            item.IsLoading = false;
        }

        private static async Task<string?> ResolveImageAsync(string directory, string? source, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;
            try
            {
                return await Task.Run(async () =>
                {
                    token.ThrowIfCancellationRequested();
                    if (source.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)) return source;
                    if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                        return await KnUtils.GetRemoteResource(source);
                    var local = Path.IsPathRooted(source) ? source : Path.Combine(directory, source);
                    return File.Exists(local) ? local : null;
                }, token);
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Warning, "ModInformation.Image", ex);
                return null;
            }
        }

        /// <summary>Accepts watch, short, embed, live and Shorts links from YouTube hosts only.</summary>
        internal static string? GetYouTubeVideoId(string source)
        {
            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return null;
            var host = uri.Host.ToLowerInvariant();
            string? id = null;
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (host == "youtu.be" || host == "www.youtu.be") id = segments.FirstOrDefault();
            else if (host == "youtube.com" || host.EndsWith(".youtube.com") || host == "youtube-nocookie.com" || host.EndsWith(".youtube-nocookie.com"))
            {
                if (segments.FirstOrDefault() == "watch") id = HttpUtility.ParseQueryString(uri.Query)["v"];
                else if (segments.Length == 2 && new[] { "embed", "shorts", "live", "v" }.Contains(segments[0])) id = segments[1];
            }
            return id != null && Regex.IsMatch(id, "^[a-zA-Z0-9_-]{11}$") ? id : null;
        }

        [RelayCommand]
        private void Launch(string executable)
        {
            var vrMode = executable == "ReleaseVR" || executable == "DebugVR";
            var executableType = vrMode ? executable[..^2] : executable;
            if (mod == null || !Enum.TryParse<FsoExecType>(executableType, out var type)) return;
            owner.SaveOptions();
            RefreshLaunchAvailability();
            var available = type switch
            {
                FsoExecType.Release => IsReleaseAvailable,
                FsoExecType.Debug => IsDebugAvailable,
                FsoExecType.Fred2 => IsFred2Available,
                FsoExecType.Fred2Debug => IsFred2DebugAvailable,
                FsoExecType.QtFred => IsQtFredAvailable,
                FsoExecType.QtFredDebug => IsQtFredDebugAvailable,
                _ => false
            };
            if (available) Knossos.PlayMod(mod, type, vrMode: vrMode);
        }

        [RelayCommand]
        private void Forum() { if (ForumAvailable && mod?.releaseThread != null) KnUtils.OpenBrowserURL(mod.releaseThread); }

        public void Dispose()
        {
            loadCancellation?.Cancel();
            loadCancellation?.Dispose();
            loadCancellation = null;
        }
    }
}
