using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Views;
using Microsoft.VisualBasic;
using System;

namespace Knossos.NET.ViewModels
{
    public partial class FreespaceModCardViewModel : ViewModelBase
    {
        [ObservableProperty]
        internal Bitmap? bannerImage;
        [ObservableProperty]
        internal bool installingOverlay = false;
        [ObservableProperty]
        internal bool loadingOverlay = false;
        [ObservableProperty]
        internal bool retailOverlay = false;
        [ObservableProperty]
        internal bool installed = false;
        [ObservableProperty]
        internal string tooltip = "Test";

        public readonly string ModID;
        public readonly bool NeedsRetail = false;

        public FreespaceModCardViewModel() 
        {
            ModID = "";
            BannerImage = new Bitmap(AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/fs2_demo_banner.png")));
        }

        public FreespaceModCardViewModel(string modID, string modTooltip, string assetBannerPath, bool needsFs2Retail)
        {
            ModID = modID;
            Tooltip = modTooltip;
            BannerImage = new Bitmap(AssetLoader.Open(new Uri(assetBannerPath)));
            NeedsRetail = needsFs2Retail;
            LoadingOverlay = true;
            if(NeedsRetail)
                RetailOverlay = true;
        }

        public void SetInstallingOverlay(bool status)
        {
            InstallingOverlay = status;
        }

        public void SetInstalled(bool status)
        {
            Installed = status;
        }

        public void SetLoadingOverlay(bool status)
        {
            LoadingOverlay = status;
        }

        public void SetRetailOverlay(bool status)
        {
            if(NeedsRetail)
                RetailOverlay = status;
        }

        internal void CommandPlay()
        {
            var card = FindModCard();
            if (card != null)
            {
                card.ButtonCommandRelay("play");
            }
            else
            {
                MessageBox.Show(MainWindow.instance,"Unable to find modcard id: " + ModID + ", on the installed mod list. This should not happen, try restarting KnossosNET.", "An error has ocurred", MessageBox.MessageBoxButtons.OK);
            }
        }

        internal void CommandSettings()
        {
            var card = FindModCard();
            if (card != null)
            {
                card.ButtonCommandRelay("settings");
            }
            else
            {
                MessageBox.Show(MainWindow.instance, "Unable to find modcard id: " + ModID + ", on the installed mod list. This should not happen, try restarting KnossosNET.", "An error has ocurred", MessageBox.MessageBoxButtons.OK);
            }
        }

        internal void CommandModify()
        {
            var card = FindModCard();
            if (card != null)
            {
                card.ButtonCommandRelay("modify");
            }
            else
            {
                MessageBox.Show(MainWindow.instance, "Unable to find modcard id: " + ModID + ", on the installed mod list. This should not happen, try restarting KnossosNET.", "An error has ocurred", MessageBox.MessageBoxButtons.OK);
            }
        }

        internal void CommandDetails()
        {
            var card = FindModCard();
            if (card != null)
            {
                card.ButtonCommandRelay("details");
            }
            else
            {
                MessageBox.Show(MainWindow.instance, "Unable to find modcard id: " + ModID + ", on the installed mod list. This should not happen, try restarting KnossosNET.", "An error has ocurred", MessageBox.MessageBoxButtons.OK);
            }
        }

        internal void CommandInstall()
        {
            var card = FindNebulaModCard();
            if (card != null)
            {
                card.ButtonCommandRelay("install");
            }
            else
            {
                MessageBox.Show(MainWindow.instance, "Unable to find modcard id: " + ModID + ", on the nebula mod list. This should not happen, try restarting KnossosNET.", "An error has ocurred", MessageBox.MessageBoxButtons.OK);
            }
        }

        internal void CommandRetail()
        {
            if (MainViewModel.Instance != null && MainViewModel.Instance.InstalledModsView != null)
            {
                MainViewModel.Instance.InstalledModsView.InstallFS2Command();
            }
        }

        internal void CommandGetGOG()
        {
            KnUtils.OpenBrowserURL("https://www.gog.com/en/game/freespace_2");
        }

        internal void CommandGetSteam()
        {
            KnUtils.OpenBrowserURL("https://store.steampowered.com/app/273620/Freespace_2/");
        }

        private ModCardViewModel? FindModCard()
        {
            if (MainViewModel.Instance != null && MainViewModel.Instance.InstalledModsView != null)
            {
                return MainViewModel.Instance.InstalledModsView.GetModCardByID(ModID);
            }
            return null;
        }

        private NebulaModCardViewModel? FindNebulaModCard()
        {
            if (MainViewModel.Instance != null && MainViewModel.Instance.NebulaModsView != null)
            {
                return MainViewModel.Instance.NebulaModsView.GetModCardByID(ModID);
            }
            return null;
        }
    }
}
