using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Knossos.NET.ViewModels
{
    /// <summary>Owns navigation, the selected version and all ModProperties overlays.</summary>
    public partial class ModPropertiesViewModel : ViewModelBase, IDisposable
    {
        public enum MenuPageEntry { Information, Versions, DLC, Options, Files, Settings = Options }

        private ModPropertiesView? view;
        private ModCardViewModel? modCard;
        // Accessed on the UI thread; only unfinished operations are retained.
        private static readonly Dictionary<string, ModPropertiesViewModel> runningSessions = new();
        private readonly HashSet<ModPropertiesView> attachedViews = new();
        private bool releaseWhenIdle;
        private readonly List<Mod> versions;
        private ModLaunchOptionsViewModel? options;
        private bool disposed;
        private bool modalBusy;
        private string? modalAction;
        private TaskCompletionSource<bool>? pendingConfirmation;
        private ModMediaItemViewModel? selectedMedia;
        private IReadOnlyList<ModMediaItemViewModel> mediaItems = Array.Empty<ModMediaItemViewModel>();

        [ObservableProperty] private string title = string.Empty;
        [ObservableProperty] private bool isInstalled;
        [ObservableProperty] private ViewModelBase? currentPage;
        [ObservableProperty] private MenuPageEntry selectedPage;
        [ObservableProperty] private bool isModalOpen;
        [ObservableProperty] private string modalTitle = string.Empty;
        [ObservableProperty] private string modalText = string.Empty;
        [ObservableProperty] private string reportReason = string.Empty;
        [ObservableProperty] private string? previewPath;
        [ObservableProperty] private bool isMediaModal;
        [ObservableProperty] private bool isVideoModal;
        [ObservableProperty] private bool isReportModal;
        [ObservableProperty] private bool isConfirmationModal;
        [ObservableProperty] private bool isSubmitting;
        [ObservableProperty] private string confirmLabel = "Confirm";
        private int selectedVersionIndex;

        public ObservableCollection<string> VersionItems { get; } = new();
        public ModInformationViewModel Information { get; }
        public ModVersionsViewModel Versions { get; }
        public bool IsVersionsSelected => SelectedPage == MenuPageEntry.Versions;
        public bool CanDownload => !IsInstalled && CanManageVersions;
        public bool CanChangeInformationVersion => IsInstalled && !Versions.IsApplying;
        public bool CanManageVersions => ActiveVersion.modSource != ModSource.local || ActiveVersion.devMode;
        public Mod ActiveVersion => versions[selectedVersionIndex];
        public bool IsInformationSelected => SelectedPage == MenuPageEntry.Information;
        public bool IsOptionsSelected => SelectedPage == MenuPageEntry.Options;
        public bool CanConfigure => IsInstalled && ActiveVersion.type != ModType.engine;
        public bool CanBrowseMedia => IsMediaModal && mediaItems.Count > 1;
        public bool IsPreviewUnavailable => IsMediaModal && PreviewPath == null;

        /// <summary>Reuses a running operation without retaining any closed window.</summary>
        public static ModPropertiesViewModel GetOrCreate(Mod modActiveVersion, ModPropertiesView? propertiesView = null,
            MenuPageEntry menuPageEntry = MenuPageEntry.Information, bool forceDeveloper = false, ModPackageCatalog? catalog = null)
            => GetOrCreate(new List<Mod> { modActiveVersion }, 0, null, propertiesView, menuPageEntry, forceDeveloper, catalog);

        public static ModPropertiesViewModel GetOrCreate(List<Mod> modVersions, int selectedIndex, ModCardViewModel? card,
            ModPropertiesView? propertiesView = null, MenuPageEntry menuPageEntry = MenuPageEntry.Information, bool forceDeveloper = false, ModPackageCatalog? catalog = null)
        {
            if (modVersions.Count == 0) throw new ArgumentException("At least one mod version is required.", nameof(modVersions));
            var id = modVersions[Math.Clamp(selectedIndex, 0, modVersions.Count - 1)].id;
            if (runningSessions.TryGetValue(id, out var session) && !session.disposed)
            {
                session.modCard = card ?? session.modCard;
                if (propertiesView != null) session.AttachView(propertiesView);
                session.Navigate(menuPageEntry);
                return session;
            }
            return new ModPropertiesViewModel(modVersions, selectedIndex, card, propertiesView, menuPageEntry, forceDeveloper, catalog);
        }

        public void AttachView(ModPropertiesView propertiesView)
        {
            attachedViews.Add(propertiesView);
            view = propertiesView;
            releaseWhenIdle = false;
        }

        public void DetachView(ModPropertiesView propertiesView)
        {
            attachedViews.Remove(propertiesView);
            if (ReferenceEquals(view, propertiesView)) view = attachedViews.LastOrDefault();
            if (attachedViews.Count != 0) return;
            releaseWhenIdle = true;
            modCard = null;
            options = null;
            Knossos.Tts(string.Empty);
            ResetModal();
            IsModalOpen = false;
            if (!Versions.IsApplying) Dispose();
        }

        internal void OperationStarted()
        {
            runningSessions[ActiveVersion.id] = this;
            OnPropertyChanged(nameof(CanChangeInformationVersion));
        }

        internal void OperationEnded()
        {
            if (runningSessions.TryGetValue(ActiveVersion.id, out var session) && ReferenceEquals(session, this))
                runningSessions.Remove(ActiveVersion.id);
            OnPropertyChanged(nameof(CanChangeInformationVersion));
            if (!disposed && releaseWhenIdle && attachedViews.Count == 0) Dispose();
        }

        partial void OnIsSubmittingChanged(bool value) => Versions.RefreshCommands();

        partial void OnPreviewPathChanged(string? value) => OnPropertyChanged(nameof(IsPreviewUnavailable));
        public int SelectedVersionIndex
        {
            get => selectedVersionIndex;
            set
            {
                if (value < 0 || value >= versions.Count || value == selectedVersionIndex || Versions.IsApplying || IsSubmitting) return;
                SaveOptions();
                options = null;
                SetProperty(ref selectedVersionIndex, value);
                modCard?.SelectVersion(versions[value].version);
                Versions.SelectInstalledVersion(versions[value].version);
                RefreshVersion();
            }
        }

        public ModPropertiesViewModel() : this(new Mod { title = "Blue Planet Complete", version = "3.3.1" }) { }

        public ModPropertiesViewModel(Mod modActiveVersion, ModPropertiesView? propertiesView = null,
            MenuPageEntry menuPageEntry = MenuPageEntry.Information, bool forceDeveloper = false, ModPackageCatalog? catalog = null)
            : this(new List<Mod> { modActiveVersion }, 0, null, propertiesView, menuPageEntry, forceDeveloper, catalog) { }

        public ModPropertiesViewModel(List<Mod> modVersions, int selectedIndex, ModCardViewModel? card,
            ModPropertiesView? propertiesView = null, MenuPageEntry menuPageEntry = MenuPageEntry.Information, bool forceDeveloper = false, ModPackageCatalog? catalog = null)
        {
            if (modVersions.Count == 0) throw new ArgumentException("At least one mod version is required.", nameof(modVersions));
            versions = modVersions.ToList();
            selectedVersionIndex = Math.Clamp(selectedIndex, 0, versions.Count - 1);
            if (propertiesView != null) AttachView(propertiesView);
            modCard = card;
            foreach (var mod in versions) VersionItems.Add(mod.version);
            Information = new ModInformationViewModel(this);
            Versions = new ModVersionsViewModel(this, forceDeveloper, catalog);
            selectedPage = menuPageEntry;
            RefreshVersion();
        }

        private void RefreshVersion()
        {
            Title = ActiveVersion.title + " Properties";
            IsInstalled = ActiveVersion.installed;
            OnPropertyChanged(nameof(ActiveVersion));
            OnPropertyChanged(nameof(CanConfigure));
            OnPropertyChanged(nameof(CanManageVersions));
            OnPropertyChanged(nameof(CanDownload));
            OnPropertyChanged(nameof(CanChangeInformationVersion));
            Navigate(SelectedPage);
            _ = Information.LoadAsync(ActiveVersion);
        }

        [RelayCommand]
        private void Navigate(MenuPageEntry page)
        {
            if (disposed || IsModalOpen || IsSubmitting) return;
            SaveOptions();
            if (page == MenuPageEntry.Versions && CanManageVersions)
            {
                CurrentPage = Versions;
                SelectedPage = MenuPageEntry.Versions;
                _ = Versions.EnsureLoadedAsync();
            }
            else if (page == MenuPageEntry.Options && CanConfigure && !Versions.IsApplying)
            {
                options ??= new ModLaunchOptionsViewModel(ActiveVersion, modCard);
                CurrentPage = options;
                SelectedPage = MenuPageEntry.Options;
            }
            else
            {
                CurrentPage = Information;
                SelectedPage = MenuPageEntry.Information;
                Information.RefreshLaunchAvailability();
            }
            OnPropertyChanged(nameof(IsInformationSelected));
            OnPropertyChanged(nameof(IsOptionsSelected));
            OnPropertyChanged(nameof(IsVersionsSelected));
        }

        public void SaveOptions()
        {
            if (options != null && ReferenceEquals(CurrentPage, options)) options.SaveSettingsCommand();
        }

        public void InstalledVersionChanged(Mod installed)
        {
            SaveOptions();
            options = null;
            versions.Clear();
            versions.AddRange(Knossos.GetInstalledModList(installed.id));
            if (versions.Count == 0) versions.Add(installed);
            VersionItems.Clear();
            foreach (var mod in versions) VersionItems.Add(mod.version);
            var index = versions.FindIndex(m => m.version == installed.version);
            selectedVersionIndex = index;
            OnPropertyChanged(nameof(SelectedVersionIndex));
            modCard?.SelectVersion(installed.version);
            RefreshVersion();
        }

        public void ShowMessage(string title, string text)
        {
            ResetModal();
            ModalTitle = title;
            ModalText = text;
            IsModalOpen = true;
        }

        public Task<bool> ConfirmAsync(string title, string text, string label)
        {
            ShowMessage(title, text);
            pendingConfirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            IsConfirmationModal = true;
            ConfirmLabel = label;
            modalAction = "callback";
            return pendingConfirmation.Task;
        }

        [RelayCommand]
        private void Back() => view?.Close();

        public void ShowMedia(ModMediaItemViewModel item, IReadOnlyList<ModMediaItemViewModel> items)
        {
            ResetModal();
            mediaItems = items;
            selectedMedia = item;
            IsMediaModal = true;
            UpdatePreview();
            IsModalOpen = true;
        }

        private void UpdatePreview()
        {
            PreviewPath = selectedMedia?.ImagePath;
            IsVideoModal = selectedMedia?.IsVideo == true;
            ModalTitle = selectedMedia?.Title ?? string.Empty;
            ModalText = IsVideoModal ? "Play this video on YouTube using your browser or the YouTube app." : string.Empty;
            OnPropertyChanged(nameof(CanBrowseMedia));
            OnPropertyChanged(nameof(IsPreviewUnavailable));
        }

        [RelayCommand]
        private void ChangeMedia(string offset)
        {
            if (selectedMedia == null || mediaItems.Count == 0) return;
            var index = mediaItems.ToList().IndexOf(selectedMedia);
            selectedMedia = mediaItems[(index + int.Parse(offset) + mediaItems.Count) % mediaItems.Count];
            UpdatePreview();
        }

        [RelayCommand]
        private void PlayVideo()
        {
            if (selectedMedia?.VideoUrl != null) KnUtils.OpenBrowserURL(selectedMedia.VideoUrl);
        }

        private void ResetModal()
        {
            pendingConfirmation?.TrySetResult(false);
            pendingConfirmation = null;
            IsMediaModal = IsVideoModal = IsReportModal = IsConfirmationModal = false;
            PreviewPath = null;
            ModalText = ReportReason = string.Empty;
            modalAction = null;
        }

        [RelayCommand]
        private void CloseModal()
        {
            if (modalBusy) return;
            IsModalOpen = false;
            ResetModal();
        }

        [RelayCommand]
        private void Report()
        {
            ResetModal();
            ModalTitle = "Report " + ActiveVersion.title;
            if (!Nebula.userIsLoggedIn)
                ModalText = "Sign in to Nebula in the Develop section before submitting a report.";
            else
            {
                ModalText = "Describe the problem with version " + ActiveVersion.version + ".";
                IsReportModal = IsConfirmationModal = true;
                ConfirmLabel = "Submit report";
                modalAction = "report";
            }
            IsModalOpen = true;
        }

        [RelayCommand]
        private Task DeleteAsync() => DeleteInstalledVersionAsync(ActiveVersion);

        internal async Task DeleteInstalledVersionAsync(Mod selected)
        {
            if (!selected.installed || Versions.IsApplying || IsSubmitting) return;
            if (selected.devMode || selected.id == "FS2")
            {
                ShowMessage("Delete mod version", selected.id == "FS2" ? "The retail installation cannot be deleted here." :
                    "Manage development mods in the Develop section.");
                return;
            }
            if (TaskViewModel.Instance?.IsSafeState() != true)
            {
                ShowMessage("Installation busy", "Wait until installation tasks finish before deleting a version.");
                return;
            }
            if (!await ConfirmAsync("Delete mod version", "Delete " + selected.title + " " + selected.version +
                " and its installed files? Other versions will be kept.", "Delete version")) return;
            if (disposed || Versions.IsApplying || TaskViewModel.Instance?.IsSafeState() != true) return;
            modalBusy = IsSubmitting = true;
            try
            {
                SaveOptions();
                options = null;
                if (selected.type == ModType.engine)
                {
                    var build = Knossos.GetInstalledBuild(selected.id, selected.version);
                    if (build == null) return;
                    if (FsoBuildsViewModel.Instance == null) throw new InvalidOperationException("The build manager is unavailable.");
                    await FsoBuildsViewModel.Instance.DeleteBuild(build, null);
                    if (Knossos.GetInstalledBuild(selected.id, selected.version) != null)
                        throw new InvalidOperationException("The installed build could not be deleted. Check that it is not running and try again.");
                }
                else
                {
                    var mod = Knossos.GetInstalledMod(selected.id, selected.version);
                    if (mod == null) return;
                    Knossos.RemoveMod(mod);
                    if (Knossos.GetInstalledMod(selected.id, selected.version) != null)
                        throw new InvalidOperationException("The installed files could not be deleted. Check that the mod is not running and try again.");
                    mod.installed = false;
                    MainViewModel.Instance?.RemoveInstalledModVersion(mod);
                    if (Knossos.GetInstalledModList(mod.id).Count == 0)
                    {
                        if (mod.modSource != ModSource.local) MainViewModel.Instance?.AddNebulaMod(mod);
                        if (Knossos.globalSettings.hiddenModIds.Remove(mod.id)) Knossos.globalSettings.Save();
                        FreespaceViewModel.Instance?.SetInstalled(mod.id, false);
                    }
                    MainViewModel.Instance?.RunModStatusChecks();
                }
                await Versions.InstalledVersionRemovedAsync(selected);
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "ModProperties.DeleteVersion", ex);
                ShowMessage("Deletion failed", ex.Message);
            }
            finally { modalBusy = IsSubmitting = false; }
        }

        internal void InstalledVersionRemoved(Mod deleted, List<Mod> remaining)
        {
            var active = ActiveVersion.version;
            versions.Clear();
            versions.AddRange(remaining);
            if (versions.Count == 0)
            {
                var downloadable = ModPackageCatalog.Copy(deleted);
                downloadable.installed = false;
                versions.Add(downloadable);
            }
            selectedVersionIndex = Math.Max(0, versions.FindIndex(m => m.version == active));
            VersionItems.Clear();
            foreach (var mod in versions) VersionItems.Add(mod.version);
            OnPropertyChanged(nameof(SelectedVersionIndex));
            if (ActiveVersion.installed) modCard?.SelectVersion(ActiveVersion.version);
            RefreshVersion();
        }

        [RelayCommand]
        private async Task ConfirmModalAsync()
        {
            if (modalBusy) return;
            if (modalAction == "callback")
            {
                var confirmation = pendingConfirmation;
                pendingConfirmation = null;
                CloseModal();
                confirmation?.TrySetResult(true);
                return;
            }
            modalBusy = IsSubmitting = true;
            try
            {
                if (modalAction == "report")
                {
                    if (string.IsNullOrWhiteSpace(ReportReason)) { ModalText = "Please describe the problem before submitting."; return; }
                    if (await Nebula.ReportMod(ActiveVersion, ReportReason.Trim()))
                    {
                        IsConfirmationModal = IsReportModal = false;
                        ModalText = "Your report has been submitted to Nebula.";
                    }
                    else ModalText = "The report could not be submitted. Please try again.";
                }

            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "ModProperties.ConfirmModal", ex);
                ModalText = "The action could not be completed. Please try again.";
            }
            finally { modalBusy = IsSubmitting = false; }
        }

        public void Dispose()
        {
            if (disposed) return;
            if (Versions.IsApplying) { releaseWhenIdle = true; return; }
            disposed = true;
            OperationEnded();
            view = null;
            if (attachedViews.Count > 0) Knossos.Tts(string.Empty);
            attachedViews.Clear();
            modCard = null;
            Information.Dispose();
            Versions.Dispose();
            pendingConfirmation?.TrySetResult(false);
        }
    }
}
