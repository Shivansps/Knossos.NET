using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Classes;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media;
using System.Net.Http;
using Avalonia.Threading;

namespace Knossos.NET.ViewModels
{
    public partial class DevToolManagerViewModel : ViewModelBase
    {
        /**/
        public partial class ToolItem : ObservableObject
        {
            [ObservableProperty]
            internal bool wine = false;
            [ObservableProperty]
            internal bool shortcutEnabled = KnUtils.IsWindows;
            [ObservableProperty]
            internal IBrush favoriteColor = Brushes.White;
            [ObservableProperty]
            internal bool disableButtons = false;
            [ObservableProperty]
            internal bool isInstalled = false;
            [ObservableProperty]
            internal bool updateAvailable = false;
            [ObservableProperty]
            internal string updateTooltip = "";
            [ObservableProperty]
            internal string description = "";
            [ObservableProperty]
            public Tool tool;
            public Tool? repo;
            private DevToolManagerViewModel? toolMgr;

            public ToolItem(Tool tool, DevToolManagerViewModel? toolMgr) 
            { 
                this.tool = tool;
                Description = tool.description + "\n\n\nLast Updated: " + tool.lastUpdate;
                IsInstalled = tool.isInstalled;
                if (Tool.isFavorite)
                    FavoriteColor = Brushes.Yellow;
                this.toolMgr = toolMgr;
                if(KnUtils.IsLinux)
                {
                    var checkStatus = tool.GetBestPackage()?.wineSupport;
                    Wine = checkStatus.HasValue ? checkStatus.Value : false;
                }
            }

            internal void ToggleFavorite()
            {
                Tool.isFavorite = !Tool.isFavorite;
                if (Tool.isFavorite)
                    FavoriteColor = Brushes.Yellow;
                else
                    FavoriteColor = Brushes.White;
                if (Tool.isInstalled)
                {
                    Tool.SaveJson();
                }
            }

            public void AddRepoVersion(Tool repo)
            {
                if(SemanticVersion.Compare(repo.version,Tool.version) > 0)
                {
                    this.repo = repo;
                    UpdateAvailable = true;
                    UpdateTooltip = "New Version Available: " + repo.version + "\nUpdate Date: " + repo.lastUpdate;
                }
            }

            internal async void Update()
            {
                DisableButtons = true;
                if(repo != null)
                    await TaskViewModel.Instance!.DownloadTool(repo, Tool, Callback);
            }

            internal void Delete()
            {
                try
                {
                    Tool.Delete();
                    if (UpdateAvailable)
                    {
                        UpdateAvailable = false;
                        UpdateTooltip = "";
                        Tool = repo!;
                        repo = null;
                        IsInstalled = Tool.isInstalled;
                    }
                    IsInstalled = false;
                    toolMgr?.SortTools();
                }
                catch { }
            }

            internal async void Install()
            {
                DisableButtons = true;
                await TaskViewModel.Instance!.DownloadTool(Tool,null,Callback);
                toolMgr?.SortTools();
            }

            internal void Shortcut()
            {
                var execPath = Tool.GetExecutableFullPath();
                var knPath = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
                if (execPath != null && knPath != null)
                {
                    if (KnUtils.IsAppImage)
                    {
                        knPath = KnUtils.AppImagePath;
                    }
                    KnUtils.CreateDesktopShortcut(Tool.name, knPath, "-tool "+ "\"" + Tool.name + "\"", execPath);
                }
            }

            internal void Open()
            {
                _= Tool.Open();
            }

            internal void Callback(bool _)
            {
                DisableButtons = false;
                if (UpdateAvailable)
                {
                    UpdateAvailable = false;
                    UpdateTooltip = "";
                    Tool = repo!;
                    repo = null;
                }
                IsInstalled = Tool.isInstalled;
            }

            /// <summary>
            /// To use with List .Sort()
            /// </summary>
            public static int CompareTools(ToolItem t1, ToolItem t2)
            {
                if (t1.IsInstalled && !t2.IsInstalled)
                {
                    return -1;
                }
                if (!t1.IsInstalled && t2.IsInstalled)
                {
                    return 1;
                }
                return string.Compare(t1.Tool.name, t2.Tool.name);
            }
        }
        /**/

        [ObservableProperty]
        internal ObservableCollection<ToolItem> tools = new ObservableCollection<ToolItem>();

        private bool toolsRepoLoaded = false;
        private bool toolsLoading = false;

        public DevToolManagerViewModel()
        {
        }

        public async void LoadTools()
        {
            if (toolsRepoLoaded || toolsLoading)
                return;

            toolsLoading = true;
            try
            {
                await Knossos.WaitForInitialLibraryScanAsync().ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var installedTool in Knossos.GetTools())
                    {
                        InsertInOrder(installedTool);
                    }
                });
                await LoadToolRepo().ConfigureAwait(false);
                toolsRepoLoaded = true;
            }
            finally
            {
                toolsLoading = false;
            }
        }

        internal void SortTools()
        {
            Dispatcher.UIThread.Invoke(new Action(() => {
                var list = Tools.ToList();
                list.Sort((x, y) => ToolItem.CompareTools(x,y));
                Tools.Clear();
                foreach (var item in list)
                {
                    Tools.Add(item);
                }
            }));
        }

        private async Task LoadToolRepo()
        {
            try
            {
                using var response = await KnUtils.GetHttpClient().GetAsync(Knossos.ToolRepoURL).ConfigureAwait(false);
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var toolsRepo = JsonSerializer.Deserialize<Tool[]>(json)!;
                if(toolsRepo != null && toolsRepo.Any())
                {
                    foreach(var tool in toolsRepo)
                    {
                        if(tool.IsValidPlatform())
                        {
                            Dispatcher.UIThread.Invoke(() => { 
                                var installed = Tools.FirstOrDefault(x=>x.Tool.name == tool.name);
                                if (installed != null)
                                {
                                    installed.AddRepoVersion(tool);
                                }
                                else
                                {
                                    InsertInOrder(tool);
                                }
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevToolManagerViewModel.LoadToolRepo()", ex);
            }
        }

        private void InsertInOrder(Tool tool)
        {
            int i;
            for (i = 0; i < Tools.Count; i++)
            {
                if (!Tools[i].Tool.isInstalled && tool.isInstalled)
                {
                    break;
                }
                if (Tools[i].Tool.isInstalled == tool.isInstalled)
                {
                    if (String.Compare(Tools[i].Tool.name, tool.name) > 0)
                    {
                        break;
                    }
                }
            }
            Tools.Insert(i, new ToolItem(tool, this));
        }
    }
}
