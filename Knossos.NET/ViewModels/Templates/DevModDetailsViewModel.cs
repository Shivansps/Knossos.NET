using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Knossos.NET.ViewModels
{
    public partial class DevModDetailsViewModel : ViewModelBase
    {
        /******************************************************************************************/
        public partial class DevModScreenshot : ObservableObject
        {
            DevModDetailsViewModel detailsView;
            [ObservableProperty]
            public Bitmap? bitmap;
            public string path;

            public DevModScreenshot(string modPath, string path, DevModDetailsViewModel detailsView)
            {
                this.detailsView = detailsView;
                this.path = path;
                try
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        if (!path.ToLower().Contains("http"))
                        {
                            Bitmap = new Bitmap(modPath + Path.DirectorySeparatorChar + path);
                        }
                        else
                        {
                            Task.Run(async () => {
                                using var response = await KnUtils.GetHttpClient().GetAsync(path).ConfigureAwait(false);
                                byte[] content = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                                using (Stream stream = new MemoryStream(content))
                                {
                                    Dispatcher.UIThread.Invoke(()=>{ Bitmap = new Bitmap(stream); });
                                }
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "DevModScreenshot.Constructor", ex);
                }
            }

            internal void ScUp()
            {
                detailsView.ScUp(this);
            }

            internal void ScDown()
            {
                detailsView.ScDown(this);
            }

            internal void ScDel()
            {
                detailsView.ScDel(this);
            }
        }
        /******************************************************************************************/
        private DevModEditorViewModel? editor;
        [ObservableProperty]
        internal string modName = string.Empty;
        [ObservableProperty]
        internal string modId = string.Empty;
        [ObservableProperty]
        internal string? modType = string.Empty;
        [ObservableProperty]
        internal string? modParent = string.Empty;
        [ObservableProperty]
        internal string modDescription = string.Empty;
        [ObservableProperty]
        internal string? modForumLink = string.Empty;
        [ObservableProperty]
        internal string? modVideos = string.Empty;
        [ObservableProperty]
        internal Bitmap? tileImage;
        [ObservableProperty]
        internal string? tileImagePath;
        [ObservableProperty]
        internal Bitmap? bannerImage;
        [ObservableProperty]
        internal string? bannerImagePath;
        [ObservableProperty]
        internal ObservableCollection<DevModScreenshot> screenshots = new ObservableCollection<DevModScreenshot>();

        public DevModDetailsViewModel() 
        { 
        }

        public DevModDetailsViewModel(DevModEditorViewModel devModEditorViewModel)
        {
            editor = devModEditorViewModel;
            ModName = editor.ActiveVersion.title;
            ModId = editor.ActiveVersion.id;
            ModType = editor.ActiveVersion.type.ToString();
            ModParent = editor.ActiveVersion.parent;
            ModDescription = editor.ActiveVersion.description != null ? editor.ActiveVersion.description : string.Empty;
            ModForumLink = editor.ActiveVersion.releaseThread;
            if(editor.ActiveVersion.videos != null && editor.ActiveVersion.videos.Length > 0)
            {
                ModVideos = String.Join(Environment.NewLine, editor.ActiveVersion.videos);
            }
            TileImagePath = editor.ActiveVersion.tile;
            LoadTileImage();
            BannerImagePath = editor.ActiveVersion.banner;
            LoadBannerImage();
            if(editor.ActiveVersion.screenshots != null)
            {
                foreach(var sc in editor.ActiveVersion.screenshots)
                {
                    Screenshots.Add(new DevModScreenshot(editor.ActiveVersion.fullPath, sc,this));
                }
            }
        }

        internal void Save()
        {
            if(editor != null)
            {
                editor.ActiveVersion.title = ModName;
                editor.ActiveVersion.description = ModDescription;
                editor.ActiveVersion.releaseThread = ModForumLink;
                if(ModVideos != null && ModVideos.Trim() != string.Empty)
                {
                    editor.ActiveVersion.videos = ModVideos.Split(Environment.NewLine,StringSplitOptions.RemoveEmptyEntries);
                }
                else
                {
                    editor.ActiveVersion.videos = new string[0];
                }
                editor.ActiveVersion.tile = TileImagePath;
                editor.ActiveVersion.banner = BannerImagePath;
                if (Screenshots.Any())
                {
                    var scList = new List<string>();
                    foreach(var sc in Screenshots)
                    {
                        scList.Add(sc.path);
                    }
                    editor.ActiveVersion.screenshots = scList.ToArray();
                }
                else
                {
                    editor.ActiveVersion.screenshots = null;
                }
                editor.ActiveVersion.SaveJson();
            }
        }

        internal async void OpenDescriptionEditor()
        {
            if (editor != null)
            {
                var dialog = new DevModDescriptionEditorView();
                dialog.DataContext = new DevModDescriptionEditorViewModel(this, ModDescription);
                dialog.BindTextBox();
                await dialog.ShowDialog<DevModDescriptionEditorView?>(MainWindow.instance);
            }
        }

        public void UpdateDescription(string description)
        {
            ModDescription = description;
        }

        internal async void ChangeTileImage()
        {
            try
            {
                //get file
                FilePickerOpenOptions options = new FilePickerOpenOptions();
                options.AllowMultiple = false;
                options.FileTypeFilter = new List<FilePickerFileType> {
                    new("Image files (*.jpg, *.png)") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } }
                };

                var result = await KnUtils.GetTopLevel().StorageProvider.OpenFilePickerAsync(options);

                if (editor != null && result != null && result.Count > 0)
                {
                    var filepath = result[0].Path.LocalPath.ToString();

                    //Get File Hash (new filename)
                    using (FileStream? file = new FileStream(filepath, FileMode.Open, FileAccess.Read))
                    {
                        //300kb limit
                        if(file.Length > 307200)
                        {
                            var length = file.Length;
                            file.Close();
                            Dispatcher.UIThread.Invoke(() => {
                                MessageBox.Show(MainWindow.instance!, "The image file size (" + length + " bytes) is over the max limit allowed (307200 bytes)", "Selected image file too is too big", MessageBox.MessageBoxButtons.OK);
                            });
                            throw new Exception("The image file size (" + length + " bytes) is over the max limit allowed (307200 bytes)");
                        }
                        var filename = string.Empty;
                        using (SHA256 checksum = SHA256.Create())
                        {
                            filename = BitConverter.ToString(await checksum.ComputeHashAsync(file)).Replace("-", String.Empty).ToLower();
                        }
                        //Copy to new location
                        Directory.CreateDirectory(Path.Combine(editor.ActiveVersion.fullPath, "kn_images"));

                        var extension = Path.GetExtension(filepath);

                        using (FileStream? dest = new FileStream(editor.ActiveVersion.fullPath + Path.DirectorySeparatorChar + "kn_images" + Path.DirectorySeparatorChar + filename + extension, FileMode.Create, FileAccess.ReadWrite))
                        {
                            file.Position = 0;
                            await file.CopyToAsync(dest);
                            dest.Close();
                        }
                        file.Close();
                        TileImagePath = "kn_images" + Path.DirectorySeparatorChar + filename + extension;
                        LoadTileImage();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevModDetailsViewModel.ChangeTileImage", ex);
            }
        }

        internal void RemoveTileImage()
        {
            TileImagePath = null;
            TileImage?.Dispose();
            TileImage = new Bitmap(AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/general/NebulaDefault.png")));
        }

        internal async void ChangeBannerImage()
        {
            try
            {
                //get file
                FilePickerOpenOptions options = new FilePickerOpenOptions();
                options.AllowMultiple = false;
                options.FileTypeFilter = new List<FilePickerFileType> {
                    new("Image files (*.jpg, *.png)") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } }
                };

                var result = await KnUtils.GetTopLevel().StorageProvider.OpenFilePickerAsync(options);

                if (editor != null && result != null && result.Count > 0)
                {
                    var filepath = result[0].Path.LocalPath.ToString();

                    //Get File Hash (new filename)
                    using (FileStream? file = new FileStream(filepath, FileMode.Open, FileAccess.Read))
                    {
                        //10MB limit
                        if (file.Length > 10485760)
                        {
                            var length = file.Length;
                            file.Close();
                            Dispatcher.UIThread.Invoke(() => {
                                MessageBox.Show(MainWindow.instance!, "The image file size (" + length + " bytes) is over the max limit allowed (10485760 bytes)", "Selected image file too is too big", MessageBox.MessageBoxButtons.OK);
                            });
                            throw new Exception("The image file size (" + length + " bytes) is over the max limit allowed (10485760 bytes)");
                        }
                        var filename = string.Empty;
                        using (SHA256 checksum = SHA256.Create())
                        {
                            filename = BitConverter.ToString(await checksum.ComputeHashAsync(file)).Replace("-", String.Empty).ToLower();
                        }
                        //Copy to new location
                        Directory.CreateDirectory(Path.Combine(editor.ActiveVersion.fullPath, "kn_images"));

                        var extension = Path.GetExtension(filepath);

                        using (FileStream? dest = new FileStream(editor.ActiveVersion.fullPath + Path.DirectorySeparatorChar + "kn_images" + Path.DirectorySeparatorChar + filename + extension, FileMode.Create, FileAccess.ReadWrite))
                        {
                            file.Position = 0;
                            await file.CopyToAsync(dest);
                            dest.Close();
                        }
                        file.Close();
                        BannerImagePath = "kn_images" + Path.DirectorySeparatorChar + filename + extension;
                        LoadBannerImage();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevModDetailsViewModel.ChangeBannerImage", ex);
            }
        }

        internal void RemoveBannerImage()
        {
            BannerImagePath = null;
            BannerImage?.Dispose();
            BannerImage = null;
        }

        private void LoadBannerImage()
        {
            try
            {
                if (!string.IsNullOrEmpty(BannerImagePath))
                {
                    if (!BannerImagePath.ToLower().Contains("http") && editor != null)
                    {
                        BannerImage = new Bitmap(editor.ActiveVersion.fullPath + Path.DirectorySeparatorChar + BannerImagePath);
                    }
                    else
                    {
                        Task.Run(async () => {
                            
                            using var response = await KnUtils.GetHttpClient().GetAsync(TileImagePath).ConfigureAwait(false);
                            byte[] content = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            using (Stream stream = new MemoryStream(content))
                            {
                                Dispatcher.UIThread.Invoke(() =>
                                {
                                    BannerImage = new Bitmap(stream);
                                });
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevModDetailsViewModel.LoadBannerImage", ex);
            }
        }

        private void LoadTileImage()
        {
            try
            {
                if (!string.IsNullOrEmpty(TileImagePath))
                {
                    if (!TileImagePath.ToLower().Contains("http") && editor != null)
                    {
                        TileImage = new Bitmap(editor.ActiveVersion.fullPath + Path.DirectorySeparatorChar + TileImagePath);
                    }
                    else
                    {
                        Task.Run( async () => { 
                            using var response = await KnUtils.GetHttpClient().GetAsync(TileImagePath).ConfigureAwait(false);
                            byte[] content =  await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            using (Stream stream = new MemoryStream(content))
                            {
                                Dispatcher.UIThread.Invoke(() =>
                                {
                                    TileImage = new Bitmap(stream);
                                }); 
                            }
                        });
                    }
                }
                else
                {
                    TileImage?.Dispose();
                    TileImage = new Bitmap(AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/general/NebulaDefault.png")));
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevModDetailsViewModel.LoadTileImage", ex);
            }
        }

        internal async void NewScreenShot()
        {
            try
            {
                //get file
                FilePickerOpenOptions options = new FilePickerOpenOptions();
                options.AllowMultiple = false;
                options.FileTypeFilter = new List<FilePickerFileType> {
                    new("Image files (*.jpg, *.png)") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } }
                };

                var result = await KnUtils.GetTopLevel().StorageProvider.OpenFilePickerAsync(options);

                if (editor != null && result != null && result.Count > 0)
                {
                    var filepath = result[0].Path.LocalPath.ToString();

                    //Get File Hash (new filename)
                    using (FileStream? file = new FileStream(filepath, FileMode.Open, FileAccess.Read))
                    {
                        //10MB limit
                        if (file.Length > 10485760)
                        {
                            var length = file.Length;
                            file.Close();
                            Dispatcher.UIThread.Invoke(() => {
                                MessageBox.Show(MainWindow.instance!, "The image file size (" + length + " bytes) is over the max limit allowed (10485760 bytes)", "Selected image file too is too big", MessageBox.MessageBoxButtons.OK);
                            });
                            throw new Exception("The image file size (" + length + " bytes) is over the max limit allowed (10485760 bytes)");
                        }
                        var filename = string.Empty;
                        using (SHA256 checksum = SHA256.Create())
                        {
                            filename = BitConverter.ToString(await checksum.ComputeHashAsync(file)).Replace("-", String.Empty).ToLower();
                        }
                        //Copy to new location
                        Directory.CreateDirectory(Path.Combine(editor.ActiveVersion.fullPath, "kn_images"));

                        var extension = Path.GetExtension(filepath);

                        using (FileStream? dest = new FileStream(editor.ActiveVersion.fullPath + Path.DirectorySeparatorChar + "kn_images" + Path.DirectorySeparatorChar + filename + extension, FileMode.Create, FileAccess.ReadWrite))
                        {
                            file.Position = 0;
                            await file.CopyToAsync(dest);
                            dest.Close();
                        }
                        file.Close();
                        Screenshots.Add(new DevModScreenshot(editor.ActiveVersion.fullPath,"kn_images" + Path.DirectorySeparatorChar + filename + extension,this));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevModDetailsViewModel.NewScreenShot", ex);
            }
        }

        internal void ScUp(DevModScreenshot sc)
        {
            int index = Screenshots.IndexOf(sc);
            if (index > 0)
            {
                Screenshots.Move(index, index - 1);
            }
        }

        internal void ScDown(DevModScreenshot sc)
        {
            int index = Screenshots.IndexOf(sc);
            if (index + 1 < Screenshots.Count())
            {
                Screenshots.Move(index, index + 1);
            }
        }

        internal void ScDel(DevModScreenshot sc)
        {
            Screenshots.Remove(sc);
        }
    }
}
