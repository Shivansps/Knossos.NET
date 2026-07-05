using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Views;
using Knossos.NET.Views.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace Knossos.NET.ViewModels
{
    public class PxoGamesActive
    {
        public string? Game { get; set; }
        public string? Tag { get; set; }
        public List<PxoServer> Servers { get; set; } = new List<PxoServer>();
    }

    public class PxoServer
    {
        public string Added { get; set; } = string.Empty;
        public string LastUpdate { get; set; } = string.Empty;
        public List<string> Flags { get; set; } = new List<string>();
        public string Probe { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string IP { get; set; } = string.Empty;
        public int Port { get; set; }
        public PxoGame Game { get; set; } = new PxoGame();
    }

    public class PxoGame
    {
        public string Name { get; set; } = string.Empty;
        public int NumPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public string Mode { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Mission { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public int Version { get; set; }
        public List<string> Flags { get; set; } = new List<string>();
    }

    /// <summary>
    /// PXO Tab View Model Class
    /// </summary>
    public partial class PxoViewModel : ViewModelBase
    {
        public static PxoViewModel? Instance;
        /// <summary>
        /// Server loading is only done once, when the user open tha tab
        /// </summary>
        private bool initialLoadDone = false;

        [ObservableProperty]
        internal List<PxoGamesActive> activeGames = new List<PxoGamesActive>();

        [ObservableProperty]
        internal string login = string.Empty;
        [ObservableProperty]
        internal string password = string.Empty;

        public PxoViewModel()
        {
            Instance = this;
        }

        internal void OpenPXOWeb()
        {
            KnUtils.OpenBrowserURL(@"https://pxo.nottheeye.com/");
        }

        internal void JoinHLPDiscord()
        {
            KnUtils.OpenBrowserURL(@"https://discord.gg/cyhMBhMHzK");
        }

        /// <summary>
        /// Load multi data into UI when the users clicks the tab
        /// </summary>
        public void InitialLoad()
        {
            Login = Knossos.globalSettings.pxoLogin;
            Password = Knossos.globalSettings.pxoPassword;
            if (!initialLoadDone)
            {
                RefreshData();
                initialLoadDone = true;
            }
        }

        /// <summary>
        /// Write PXO login credentians to fs2_open.ini file
        /// </summary>
        internal async void SavePXOCredentials()
        {
            Login = Login.Replace(" ","");
            Password = Password.Replace(" ", "");
            if (Login == string.Empty || Password == string.Empty)
            {
                await MessageBox.Show(MainWindow.instance!, "The PXO Login and Password cant be empty.", "Validation error", MessageBox.MessageBoxButtons.OK);
                return;
            }

            if (!Login.All(char.IsDigit) || Login.Length > 8)
            {
                await MessageBox.Show(MainWindow.instance!, "The PXO Login can only contain digits and have a max length of 8.", "Validation error", MessageBox.MessageBoxButtons.OK);
                return;
            }

            if (Password.All(char.IsDigit) || !Password.All(char.IsLower) || Password.Length != 12)
            {
                await MessageBox.Show(MainWindow.instance!, "The PXO Password can only contain 12 lowercase letters.", "Validation error", MessageBox.MessageBoxButtons.OK);
                return;
            }

            Knossos.globalSettings.pxoLogin = Login;
            Knossos.globalSettings.pxoPassword = Password;
            Knossos.globalSettings.WriteFS2IniValues();
        }

        /// <summary>
        /// Open the standalone server creator window
        /// </summary>
        internal async void OpenServerCreator()
        {
            var dialog = new ServerCreatorView();
            dialog.DataContext = new ServerCreatorViewModel();

            await dialog.ShowDialog<ServerCreatorView?>(MainWindow.instance);
        }

        /// <summary>
        /// Reload data from the PXO api
        /// There is a limit, do not do this too often
        /// </summary>
        public async void RefreshData()
        {
            try
            {
                using var response = await KnUtils.GetHttpClient().GetAsync("https://pxo.nottheeye.com/api/v1/games/active").ConfigureAwait(false);
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Dispatcher.UIThread.Invoke(() =>
                {
                    ActiveGames = JsonSerializer.Deserialize<List<PxoGamesActive>>(json)!;
                    foreach (var result in ActiveGames)
                    {
                        foreach(var server in result.Servers)
                        {
                            foreach(var flag in server.Flags)
                            {
                                if(flag.Contains("probe"))
                                {
                                    server.Probe = flag;
                                }
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                await MessageBox.Show(MainWindow.instance,ex.Message,"Error getting PXO Game list",MessageBox.MessageBoxButtons.OK);
            }
        }
    }
}
