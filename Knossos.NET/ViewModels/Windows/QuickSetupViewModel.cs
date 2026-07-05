using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System.Threading.Tasks;

namespace Knossos.NET.ViewModels
{
    /// <summary>
    /// Simple Quick Setup Guide
    /// Basic view model for the Quick Setup view
    /// </summary>
    public partial class QuickSetupViewModel : ViewModelBase
    {
        private int pageNumber = 1;

        [ObservableProperty]
        internal bool repoDownloaded = false;
        [ObservableProperty]
        internal bool canGoBack = false;
        [ObservableProperty]
        internal bool canContinue = true;
        [ObservableProperty]
        internal bool lastPage = false;

        [ObservableProperty]
        internal bool isPortableMode = false;

        [ObservableProperty]
        internal string? libraryPath = null;

        [ObservableProperty]
        public string latestBuild = string.Empty;
        [ObservableProperty]
        internal bool buildAvailable = false;

        [ObservableProperty]
        internal bool page1 = true;
        [ObservableProperty]
        internal bool page2 = false;
        [ObservableProperty]
        internal bool page3 = false;
        [ObservableProperty]
        internal bool page4 = false;
        [ObservableProperty]
        internal bool page5 = false;
        [ObservableProperty]
        internal bool page6 = false;

        private KnossosWindow? dialog;

        public static QuickSetupViewModel? Instance;

        public QuickSetupViewModel() 
        {
            isPortableMode = Knossos.inPortableMode;
        }

        public QuickSetupViewModel(KnossosWindow dialog) 
        {
            this.dialog = dialog;
            isPortableMode = Knossos.inPortableMode;
            Instance = this;
            UpdateBuildName(MainViewModel.Instance!.LatestStable);
            TrackRepoStatus();
        }

        /// <summary>
        /// Wait until repo_minimal.json has been parsed
        /// </summary>
        private void TrackRepoStatus()
        {
            Task.Run(async () =>
            {
                do
                {
                    await Task.Delay(1000);
                } while (!Nebula.repoLoaded);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    RepoDownloaded = Nebula.repoLoaded;
                });
            });
        }

        internal void OpenDiscordQuickSetup()
        {
            KnUtils.OpenBrowserURL(@"https://discord.gg/raSEhVeTGw");
        }

        /// <summary>
        /// Wait until the Knossos library path is set
        /// </summary>
        private void EnterPage2()
        {
            Task.Run(() => 
            {
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    LibraryPath = Knossos.globalSettings.basePath;

                    if (Page2)
                    {
                        if (LibraryPath == null)
                        {
                            await Task.Delay(1000);
                            EnterPage2();
                        }
                        else
                        {
                            CanContinue = true;
                        }
                    }
                });
            });
        }

        /// <summary>
        /// Wait until FSO flags has been loaded
        /// </summary>
        private void EnterPage3()
        {
            Task.Run(() =>
            {
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (Page3)
                    {
                        if (!Knossos.flagDataLoaded)
                        {
                            await Task.Delay(1000);
                            EnterPage3();
                        }
                        else
                        {
                            CanContinue = true;
                        }
                    }
                });
            });
        }

        internal void GoBackCommand()
        {
            pageNumber--;
            SetActivePage();
        }

        internal void ContinueCommand()
        {
            pageNumber++;
            SetActivePage();
        }

        internal void Finish()
        {
            if(dialog != null)
                dialog.Close();
        }

        private void SetActivePage()
        {
            switch(pageNumber)
            {
                case 1: CanGoBack = false; CanContinue = true; Page1 = true; Page2 = false; LastPage = false;  break;
                case 2: CanGoBack = true; CanContinue = false; Page1 = false; Page2 = true; Page3 = false; EnterPage2(); LastPage = false;  break;
                case 3: CanGoBack = true; CanContinue = false; Page2 = false; Page3 = true; Page4 = false; EnterPage3(); LastPage = false;  break;
                case 4: CanGoBack = true; CanContinue = true; Page3 = false; Page4 = true; Page5 = false; LastPage = false; break;
                case 5: CanGoBack = true; CanContinue = true; Page4 = false; Page5 = true; Page6 = false; LastPage = false; break;
                case 6: CanGoBack = true; CanContinue = false; Page5 = false; Page6 = true; LastPage = true; break;
            }
        }

        public void UpdateBuildName(string stableIn){
            LatestBuild = stableIn;
            UpdateBuildInstallButton();
        }

        public void UpdateBuildInstallButton()
        {
            if (LatestBuild == "")
            {
                BuildAvailable = false;
            }
            else 
            {
                var installed = Knossos.GetInstalledBuild("FSO", LatestBuild);
                if (installed != null)
                {
                    LatestBuild = "";
                    BuildAvailable = false;
                } 
                else 
                {
                    BuildAvailable = true;
                }
            } 
        }

        public void DownloadLatestStable()
        {
            if (LatestBuild != ""){
                var stable = new Mod();
                stable.id = "FSO";
                stable.version = LatestBuild;
                stable.type = ModType.engine;
                stable.stability = "stable";

                MainViewModel.Instance!.FsoBuildsView!.RelayInstallBuild(stable);

                var installed = Knossos.GetInstalledBuild("FSO", LatestBuild);
                if (installed != null){
                    LatestBuild = "";
                    UpdateBuildInstallButton();
                }
            }
        }
        
        public void ClickSettingsButton()
        {
            MainViewModel.Instance?.ClickOnMenuButton("Settings");
            MainViewModel.Instance?.GlobalSettingsView?.ExpandKnossosSection();
        }

        public void ClickEngineButton()
        {
            MainViewModel.Instance?.ClickOnMenuButton("Engine");
        }

    }
}
