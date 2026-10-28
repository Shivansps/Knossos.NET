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
        private readonly ModCardViewModel? modCard;
        private readonly List<Mod> versions;
        private ModLaunchOptionsViewModel? options;
        private bool disposed;
        private bool modalBusy;
        private string? modalAction;
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
        public Mod ActiveVersion => versions[selectedVersionIndex];
        public bool IsInformationSelected => SelectedPage == MenuPageEntry.Information;
        public bool IsOptionsSelected => SelectedPage == MenuPageEntry.Options;
        public bool CanConfigure => IsInstalled && ActiveVersion.type != ModType.engine;
        public bool CanBrowseMedia => IsMediaModal && mediaItems.Count > 1;
        public bool IsPreviewUnavailable => IsMediaModal && PreviewPath == null;

        public void AttachView(ModPropertiesView propertiesView) => view = propertiesView;

        partial void OnPreviewPathChanged(string? value) => OnPropertyChanged(nameof(IsPreviewUnavailable));
        public int SelectedVersionIndex
        {
            get => selectedVersionIndex;
            set
            {
                if (value < 0 || value >= versions.Count || value == selectedVersionIndex) return;
                SaveOptions();
                options = null;
                SetProperty(ref selectedVersionIndex, value);
                modCard?.SwitchModVersion(value);
                RefreshVersion();
            }
        }

        public ModPropertiesViewModel() : this(new Mod { title = "Blue Planet Complete", version = "3.3.1" }) { }

        public ModPropertiesViewModel(Mod modActiveVersion, ModPropertiesView? propertiesView = null,
            MenuPageEntry menuPageEntry = MenuPageEntry.Information)
            : this(new List<Mod> { modActiveVersion }, 0, null, propertiesView, menuPageEntry) { }

        public ModPropertiesViewModel(List<Mod> modVersions, int selectedIndex, ModCardViewModel? card,
            ModPropertiesView? propertiesView = null, MenuPageEntry menuPageEntry = MenuPageEntry.Information)
        {
            if (modVersions.Count == 0) throw new ArgumentException("At least one mod version is required.", nameof(modVersions));
            versions = modVersions.ToList();
            selectedVersionIndex = Math.Clamp(selectedIndex, 0, versions.Count - 1);
            view = propertiesView;
            modCard = card;
            foreach (var mod in versions) VersionItems.Add(mod.version);
            Information = new ModInformationViewModel(this);
            selectedPage = menuPageEntry;
            RefreshVersion();
        }

        private void RefreshVersion()
        {
            Title = ActiveVersion.title + " Properties";
            IsInstalled = ActiveVersion.installed;
            OnPropertyChanged(nameof(ActiveVersion));
            OnPropertyChanged(nameof(CanConfigure));
            Navigate(SelectedPage);
            _ = Information.LoadAsync(ActiveVersion);
        }

        [RelayCommand]
        private void Navigate(MenuPageEntry page)
        {
            if (disposed || IsModalOpen) return;
            SaveOptions();
            if (page == MenuPageEntry.Options && CanConfigure)
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
        }

        public void SaveOptions()
        {
            if (options != null && ReferenceEquals(CurrentPage, options)) options.SaveSettingsCommand();
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
        private void Delete()
        {
            ResetModal();
            ModalTitle = "Delete mod version";
            if (ActiveVersion.devMode || ActiveVersion.id == "FS2")
                ModalText = "This version cannot be deleted here. Manage development mods in the Develop section.";
            else if (TaskViewModel.Instance?.IsSafeState() != true)
                ModalText = "Wait until installation tasks finish before deleting a mod.";
            else
            {
                ModalText = "Delete " + ActiveVersion.title + " " + ActiveVersion.version + " and its installed files? Other versions will be kept.";
                IsConfirmationModal = true;
                ConfirmLabel = "Delete version";
                modalAction = "delete";
            }
            IsModalOpen = true;
        }

        [RelayCommand]
        private async Task ConfirmModalAsync()
        {
            if (modalBusy) return;
            modalBusy = IsSubmitting = true;
            Mod? deletedMod = null;
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
                else if (modalAction == "delete")
                {
                    if (TaskViewModel.Instance?.IsSafeState() != true) { ModalText = "Wait until installation tasks finish."; return; }
                    var mod = ActiveVersion;
                    SaveOptions();
                    options = null;
                    var lastVersion = Knossos.GetInstalledModList(mod.id).Count <= 1;
                    Knossos.RemoveMod(mod);
                    if (Knossos.GetInstalledModList(mod.id).Contains(mod))
                    {
                        ModalText = "The installed files could not be deleted. Check that the mod is not running and try again.";
                        return;
                    }
                    mod.installed = false;
                    if (lastVersion)
                    {
                        if (mod.modSource != ModSource.local) MainViewModel.Instance?.AddNebulaMod(mod);
                        if (Knossos.globalSettings.hiddenModIds.Remove(mod.id)) Knossos.globalSettings.Save();
                        FreespaceViewModel.Instance?.SetInstalled(mod.id, false);
                    }
                    MainViewModel.Instance?.RunModStatusChecks();
                    IsModalOpen = false;
                    deletedMod = mod;
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "ModProperties.ConfirmModal", ex);
                ModalText = "The action could not be completed. Please try again.";
            }
            finally { modalBusy = IsSubmitting = false; }
            if (deletedMod != null)
            {
                MainViewModel.Instance?.RemoveInstalledModVersion(deletedMod);
                view?.Close();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Information.Dispose();
            Knossos.Tts(string.Empty);
        }
    }
}
