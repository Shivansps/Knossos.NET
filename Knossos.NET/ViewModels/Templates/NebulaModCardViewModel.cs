using Avalonia.Platform;
using Avalonia.Media.Imaging;
using System;
using Knossos.NET.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Views;
using System.IO;
using System.Threading.Tasks;
using System.Threading;

namespace Knossos.NET.ViewModels
{
    public partial class NebulaModCardViewModel : ViewModelBase, IComparable<NebulaModCardViewModel>
    {
        private CancellationTokenSource? cancellationTokenSource = null;
        public Mod? modJson { get; set; }
        public string? ID { get { return modJson != null ? modJson.id : null; } }

        /* UI Bindings */
        internal string? Name { get { return modJson != null ? modJson.title : null; } }
        internal string? ModVersion { get { return modJson != null ? modJson.version : null; } }
        [ObservableProperty]
        internal Bitmap? tileImage;

        public bool androidButtonsEnabled = false;

        private Bitmap? tileModBitmap;

        private bool visible = true;
        /// <summary>
        /// External property to enable/disable mod card visibility
        /// </summary>
        public bool Visible
        {
            get { 
                return visible; 
            }
            set {
                if (visible != value)
                {
                    visible = value;
                    CardVisible = value;
                    if(CardVisible)
                    {
                       _ = LazyReLoadTileImageAsync();
                    }
                    else
                    {
                        TileImage = MainViewModel.Instance?.placeholderTileImage;
                    }
                }
            }
        }

        [ObservableProperty]
        internal bool isInstalling = false;

        [ObservableProperty]
        internal bool cardVisible = true;

        /* Should only be used by the editor preview */
        public NebulaModCardViewModel()
        {
            modJson = new Mod();
            modJson.title = "default test string very long";
            modJson.version = "1.0.0";
            modJson.id = "test";
            TileImage = new Bitmap(AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/general/NebulaDefault.png")));
        }

        public NebulaModCardViewModel(Mod modJson)
        {
            modJson.ClearUnusedData();
            this.modJson = modJson;
            //Moved to load by external call only
            //LoadImage(modJson.fullPath, modJson.tile);
        }

        /// <summary>
        /// Calls to load the tile image
        /// </summary>
        public async Task LoadImage()
        {
            if (TileImage == null && modJson != null)
            {
                await LoadImage(modJson.fullPath, modJson.tile);
            }
        }

        /// <summary>
        /// Reloads the tile image to the view at a random time
        /// after the mod card itseft becomes visible
        /// </summary>
        private async Task LazyReLoadTileImageAsync()
        {
            await Task.Delay(new Random().Next(200,700));
            TileImage = tileModBitmap != null? tileModBitmap : MainViewModel.Instance?.placeholderTileImage;
        }

        /* Button Commands */
        internal void ButtonCommand(object command)
        {
            if (KnUtils.IsAndroid && !androidButtonsEnabled)
            {
                return;
            }
            ButtonCommandRelay(command);
        }

        internal void ButtonCommandRelay(object command)
        {
            switch ((string)command)
            {
                case "details": ButtonCommandDetails(); break;
                case "install": ButtonCommandInstall(); break;
                case "cancel": CancelInstallCommand(); break;
            }
        }

        internal async void ButtonCommandInstall()
        {
            if (modJson != null)
            {
                var dialog = new ModInstallView();
                dialog.DataContext = new ModInstallViewModel(modJson, dialog);
                await dialog.ShowDialog<ModInstallView?>(MainWindow.instance!);
            }
        }

        public void InstallingMod(CancellationTokenSource cancelToken)
        {
            IsInstalling = true;
            cancellationTokenSource = cancelToken;
            FreespaceViewModel.Instance?.SetInstalling(ID!, true);
        }

        public void CancelInstall()
        {
            IsInstalling = false;
            cancellationTokenSource = null;
            FreespaceViewModel.Instance?.SetInstalling(ID!, false);
        }

        public void CancelInstallCommand()
        {
            IsInstalling = false;
            try
            {
                cancellationTokenSource?.Cancel();
            }
            catch { }
            cancellationTokenSource = null;
            TaskViewModel.Instance?.CancelAllInstallTaskWithID(ID!, ModVersion);
            FreespaceViewModel.Instance?.SetInstalling(ID!,false);
        }

        internal async void ButtonCommandDetails()
        {
            if (ModVersion != null)
            {
                if(modJson != null)
                {
                    var dialog = new ModDetailsView();
                    dialog.DataContext = new ModDetailsViewModel(modJson, dialog);
                    await dialog.ShowDialog<ModDetailsView?>(MainWindow.instance);
                }
            }
        }

        private async Task LoadImage(string modFullPath, string? tileString)
        {
            TileImage = MainViewModel.Instance?.placeholderTileImage;

            try
            {
                if (!string.IsNullOrEmpty(tileString))
                {
                    if (!tileString.ToLower().Contains("http"))
                    {
                        tileModBitmap = new Bitmap(modFullPath + Path.DirectorySeparatorChar + tileString);
                    }
                    else
                    {
                        using (var fs = await KnUtils.GetRemoteResourceStream(tileString).ConfigureAwait(false))
                        {
                            if (fs != null)
                                tileModBitmap = new Bitmap(fs);
                        }
                    }
                    if (tileModBitmap != null)
                    {
                        TileImage = tileModBitmap;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Warning, "NebulaModCardViewModel.LoadImage", ex);
            }
        }

        public int CompareTo(NebulaModCardViewModel? other)
        {
            if (other == null)
                return -1;
            return Mod.SortMods(modJson, other.modJson);
        }
    }
}
