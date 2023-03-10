using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Knossos.NET.ViewModels
{
    public partial class ModDetailsViewModel : ViewModelBase
    {
        private List<Mod> modVersions = new List<Mod>();
        private ModCardViewModel? modCard = null;

        /* UI Variables */
        [ObservableProperty]
        private string name = string.Empty;
        [ObservableProperty]
        private string? released = string.Empty;
        [ObservableProperty]
        private string? lastUpdated = string.Empty;
        [ObservableProperty]
        private string? description = string.Empty;
        [ObservableProperty]
        private Bitmap? banner = null;
        [ObservableProperty]
        private bool forumAvalible = true;
        [ObservableProperty]
        private bool isInstalled = true;
        private ObservableCollection<ComboBoxItem> VersionItems { get; set; } = new ObservableCollection<ComboBoxItem>();

        private int itemSelectedIndex = 0;
        public int ItemSelectedIndex
        {
            get
            {
                return itemSelectedIndex;
            }
            set
            {
                /* Workaround for the combo box selecting a -1 when deleting the selected item */
                if (value != -1)
                {
                    if (itemSelectedIndex != value)
                    {
                        itemSelectedIndex = value;
                        LoadVersion(value);
                    }
                    this.SetProperty(ref itemSelectedIndex, value);
                }
            }
        }



        /* Only used in preview */
        public ModDetailsViewModel()
        {
        }

        public ModDetailsViewModel(List<Mod> modVersions, int selectedIndex, ModCardViewModel modCard)
        {
            this.modVersions = modVersions;
            this.modCard = modCard;
            foreach (var mod in modVersions)
            {
                var item = new ComboBoxItem();
                item.Content = mod.version;
                VersionItems.Add(item);
            }
            //Data loads on selected index change
            ItemSelectedIndex = selectedIndex;

            if (modVersions.Count == 1)
                LoadVersion(0);

            if(Knossos.globalSettings.ttsDescription)
            {
                PlayDescription(500);
            }
        }

        private async void PlayDescription(int delay = 0)
        {
            if(delay > 0)
            {
                await Task.Delay(delay);
            }
            Knossos.Tts(Regex.Replace(modVersions[ItemSelectedIndex].description!, @" ?\[.*?\]", string.Empty));
        }

        private void StopTts()
        {
            Knossos.Tts(string.Empty);
        }

        private void LoadVersion(int index)
        {
            Name = modVersions[index].title;
            LastUpdated = modVersions[index].lastUpdate;
            Description = modVersions[index].description;
            Released = modVersions[index].firstRelease;
            IsInstalled = modVersions[index].installed;
            if (modVersions[index].releaseThread != null)
            {
                ForumAvalible = true;
            }
            else
            {
                ForumAvalible = false;
            }
            LoadImage(index);
            modCard?.SwitchModVersion(index);
        }

        private void LoadImage(int selectedIndex)
        {
            try
            {
                if (!string.IsNullOrEmpty(modVersions[selectedIndex].banner))
                {
                    if(System.IO.File.Exists(modVersions[selectedIndex].fullPath + @"\" + modVersions[selectedIndex].banner))
                    {
                        Banner = new Bitmap(modVersions[selectedIndex].fullPath + @"\" + modVersions[selectedIndex].banner);
                    }
                    else
                    {
                        var url = modVersions[selectedIndex].banner;
                        if (url != null && url.ToLower().Contains("http"))
                        {
                            DownloadImage(url);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Warning, "ModCardViewModel.LoadImage", ex);
            }
        }

        private async void DownloadImage(string url)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    HttpResponseMessage response = await client.GetAsync(url);
                    byte[] content = await response.Content.ReadAsByteArrayAsync();
                    Stream stream = new MemoryStream(content);
                    Banner = new Avalonia.Media.Imaging.Bitmap(stream);
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Warning, "ModCardViewModel.DownloadImage", ex);
            }
        }

        /* Button Commands */
        private void ButtonCommandPlay()
        {
            Knossos.PlayMod(modVersions[ItemSelectedIndex], FsoExecType.Release);
        }

        private void ButtonCommandPlayDebug()
        {
            Knossos.PlayMod(modVersions[ItemSelectedIndex], FsoExecType.Debug);
        }

        private void ButtonCommandFred2()
        {
            Knossos.PlayMod(modVersions[ItemSelectedIndex], FsoExecType.Fred2);
        }

        private void ButtonCommandQtFred()
        {
            Knossos.PlayMod(modVersions[ItemSelectedIndex], FsoExecType.QtFred);
        }

        private async void ButtonCommandSettings()
        {
            if (MainWindow.instance != null)
            {
                var dialog = new ModSettingsView();
                dialog.DataContext = new ModSettingsViewModel(modVersions[ItemSelectedIndex]);

                await dialog.ShowDialog<ModSettingsView?>(MainWindow.instance);
            }
        }

        private async void ButtonCommandDelete(ModDetailsView window)
        {
            if (MainWindow.instance != null)
            {
                if (modVersions.Count > 1)
                {
                    var resp = await MessageBox.Show(MainWindow.instance, "You are about to delete version " + modVersions[ItemSelectedIndex].version + " of this mod, this will remove this version only. Do you want to continue?", "Delete mod version", MessageBox.MessageBoxButtons.YesNo);
                    if (resp == MessageBox.MessageBoxResult.Yes)
                    {
                        var delete = modVersions[ItemSelectedIndex];
                        var verDel = VersionItems[ItemSelectedIndex];
                        modVersions.Remove(delete);
                        Knossos.RemoveMod(delete);
                        ItemSelectedIndex = modVersions.Count - 1;
                        VersionItems.Remove(verDel);
                    }
                }
                else
                {
                    var resp = await MessageBox.Show(MainWindow.instance, "You are about to delete the last installed version of this mod. Do you want to continue?", "Delete mod version", MessageBox.MessageBoxButtons.YesNo);
                    if (resp == MessageBox.MessageBoxResult.Yes)
                    {
                        //Last version
                        Knossos.RemoveMod(modVersions[0].id);
                        if (window != null)
                        {
                            window.Close();
                        }
                    }
                }
            }
        }

        private void ButtonCommandForum()
        {
            var url = modVersions[ItemSelectedIndex].releaseThread;
            if (url != null)
            {
                Knossos.OpenBrowserURL(url);
            }
        }
    }
}
