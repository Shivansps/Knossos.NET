using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Models;
using Knossos.NET.Views;

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
        internal string retailFs2Status = string.Empty;

        private CompressionSettings modCompression = CompressionSettings.Manual;
        internal CompressionSettings ModCompression
        {
            get { return modCompression; }
            set
            {
                if (modCompression != value)
                {
                    this.SetProperty(ref modCompression, value);
                    Knossos.globalSettings.modCompression = value;
                    MainViewModel.Instance?.GlobalSettingsView?.UpdateModCompressionFromQuickSetup(value);
                    Knossos.globalSettings.Save(false);
                }
            }
        }

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

        private Window? dialog;

        public QuickSetupViewModel() 
        {
            isPortableMode = Knossos.inPortableMode;
            libraryPath = Knossos.globalSettings.basePath;
            modCompression = Knossos.globalSettings.modCompression;
        }

        public QuickSetupViewModel(Window dialog) 
        {
            this.dialog = dialog;
            isPortableMode = Knossos.inPortableMode;
            libraryPath = Knossos.globalSettings.basePath;
            modCompression = MainViewModel.Instance?.GlobalSettingsView?.ModCompression ?? Knossos.globalSettings.modCompression;
        }

        internal void OpenDiscordQuickSetup()
        {
            KnUtils.OpenBrowserURL(@"https://discord.gg/raSEhVeTGw");
        }

        private void EnterPage2()
        {
            LibraryPath = Knossos.globalSettings.basePath;
        }

        private void EnterPage4()
        {
            RetailFs2Status = Knossos.retailFs2RootFound
                ? "Current Status: Freespace 2 Game Data is Installed"
                : "Current Status: Freespace 2 Game Data is NOT Installed";
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
                case 2: CanGoBack = true; CanContinue = true; Page1 = false; Page2 = true; Page3 = false; EnterPage2(); LastPage = false;  break;
                case 3: CanGoBack = true; CanContinue = true; Page2 = false; Page3 = true; Page4 = false; LastPage = false;  break;
                case 4: CanGoBack = true; CanContinue = true; Page3 = false; Page4 = true; Page5 = false; EnterPage4(); LastPage = false; break;
                case 5: CanGoBack = true; CanContinue = true; Page4 = false; Page5 = true; LastPage = true; break;
            }
        }
        
        public void ClickSettingsButton()
        {
            MainViewModel.Instance?.ClickOnMenuButton("Settings");
            MainViewModel.Instance?.GlobalSettingsView?.ExpandKnossosSection();
        }
    }
}
