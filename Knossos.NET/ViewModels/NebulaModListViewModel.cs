using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using Knossos.NET.Views;
using ObservableCollections;

namespace Knossos.NET.ViewModels
{
    /// <summary>
    /// Nebula mod card container view, this is the "explore" tab
    /// </summary>
    public partial class NebulaModListViewModel : ViewModelBase
    {
        /// <summary>
        /// The user has opened this tab in this session?
        /// </summary>
        internal bool IsTabOpen = false;

        private bool _isLoading = true;
        internal bool isLoading{
            get { return _isLoading; }
            set { 
                _isLoading = value;
                ShowTiles = !isLoading;
                if (ShowTiles)
                {
                    LoadingAnimation.Animate = 0;
                } 
                else  
                {
                    LoadingAnimation.Animate = 1;
                }        
            }
        }

        /// <summary>
        /// For the UI to detmerine whether to show mod tiles.  It needs to check more than one property
        /// </summary>
        [ObservableProperty]
        public bool showTiles = false;

        [ObservableProperty]
        internal LoadingIconViewModel loadingAnimation = new LoadingIconViewModel();

        [ObservableProperty]
        internal bool fs2Present = true;

        /// <summary>
        /// Search string
        /// </summary>
        internal string search = string.Empty;
        internal string Search
        {
            get { return search; }
            set 
            {
                if (value != Search) 
                {
                    this.SetProperty(ref search, value);
                    ApplyFilters();
                }
            }
        }

        [ObservableProperty]
        internal String sortString = String.Empty;
        
        [ObservableProperty]
        internal String filterString = String.Empty;

        private ModSortType localSort = ModSortType.name;
        //The actual collection were the mods are
        private ObservableList<NebulaModCardViewModel> Mods = new ObservableList<NebulaModCardViewModel>();
        //A hook for the UI, do not access directly
        internal NotifyCollectionChangedSynchronizedViewList<NebulaModCardViewModel> CardsView { get; set; }

        public NebulaModListViewModel()
        {
            LoadingAnimation.Animate = 1;
            CardsView = Mods.ToNotifyCollectionChangedSlim(SynchronizationContextCollectionEventDispatcher.Current);
        }

        public void ApplyTagFilter(int tagIndex)
        {
            if (MainViewModel.Instance != null)
            {
                var tags = ModTags.GetListAllFilters();
                if(tags.Count() > tagIndex)
                {
                    MainViewModel.Instance.tagFilter.Add(tags[tagIndex]);
                }
                ApplyFilters();
            }
        }

        public void RemoveTagFilter(int tagIndex)
        {
            if (MainViewModel.Instance != null)
            {
                var tags = ModTags.GetListAllFilters();
                if (tags.Count() > tagIndex)
                {
                    MainViewModel.Instance.tagFilter.Remove(tags[tagIndex]);
                }

                ApplyFilters();
            }
        }

        private void ApplyFilters()
        {
            BuildFilterString();

            Parallel.ForEach(Mods, new ParallelOptions { MaxDegreeOfParallelism = 4 }, card =>
            {
                bool visibility = true;
                //By search
                if (Search.Trim() != string.Empty)
                {
                    if (card.Name == null || !card.Name.Contains(Search, StringComparison.CurrentCultureIgnoreCase))
                    {
                        //if it dosent passes title search check tags
                        visibility = ModTags.IsTagPresentInModID(card.ID!, search);
                    }
                }
                //Filters
                if (visibility && MainViewModel.Instance != null && MainViewModel.Instance.tagFilter.Any())
                {
                    visibility = false;
                    foreach (var filter in MainViewModel.Instance.tagFilter)
                    {
                        if(card.ID != null && ModTags.IsFilterPresentInModID(card.ID, filter))
                        {
                            visibility = true;
                            break;
                        }
                    }
                }
                card.Visible = visibility;
            });
        }

        private void BuildFilterString(){
            if (MainViewModel.Instance == null){
                FilterString = "No Filters Applied";
                return;
            }
            
            int externalCount = MainViewModel.Instance.tagFilter.Count;

            if (externalCount == 0 ){
                FilterString = "No Filters Applied";
                return;
            }

            int count = 0;
            FilterString = "Filtering for ";
            TextInfo myTI = new CultureInfo("en-US", false).TextInfo;

            foreach (var filter in MainViewModel.Instance.tagFilter) {
                if (count > 2){ 
                    FilterString += " and ...";
                    break;
                // easiest case, this handles a filter list of one and the start of all other cases
                } else if (count == 0){
                    if (Knossos.filterDisplayStrings.ContainsKey(filter.ToLower()))
                        FilterString += Knossos.filterDisplayStrings[filter.ToLower()];
                    else 
                        FilterString += myTI.ToTitleCase(filter.Replace("_", " "));
                // Last case except for 0 will always have an and
                } else if (count == externalCount - 1){
                    if (Knossos.filterDisplayStrings.ContainsKey(filter.ToLower()))
                        FilterString += " and " + Knossos.filterDisplayStrings[filter.ToLower()];
                    else 
                        FilterString += " and " + myTI.ToTitleCase(filter.Replace("_", " "));; // No Oxford commas here!
                // Other cases will always have a comma
                } else {
                    if (Knossos.filterDisplayStrings.ContainsKey(filter.ToLower()))
                        FilterString += ", " + Knossos.filterDisplayStrings[filter.ToLower()];
                    else 
                        FilterString += ", " + myTI.ToTitleCase(filter.Replace("_", " "));
                }

                count++;
            }
        }

        /// <summary>
        /// Used to signal the mod list view to update its "have you installed FS2" button
        /// </summary>
        public void UpdateFS2InstallButton()
        {
            Fs2Present = Knossos.retailFs2RootFound;
        }

        /// <summary>
        /// Open the tab code
        /// </summary>
        public void OpenTab()
        {
            IsTabOpen = true;

            if (!isLoading)
            {
                Task.Run(() =>
                {
                    ShowTiles = false;
                    LoadingAnimation.Animate = 1;
                    if (MainViewModel.Instance != null)
                    {
                        if (Search != MainViewModel.Instance.sharedSearch)
                        {
                            Search = MainViewModel.Instance.sharedSearch;
                        }
                        else
                        {
                            ApplyFilters();
                        }
                    }
                    
                    ChangeSort(Knossos.globalSettings.sortType);
                    BuildFilterString();

                    Parallel.ForEach(Mods, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async card =>
                    {
                        await card.LoadImage();
                    });
                    LoadingAnimation.Animate = 0;
                    ShowTiles = true;
                });
            }

            if (Knossos.initIsComplete)
            {
                Fs2Present = Knossos.retailFs2RootFound;
            }
        }

        /// <summary>
        /// Close the tab code
        /// </summary>
        public void CloseTab()
        {
            ShowTiles = false;
            if (MainViewModel.Instance != null)
            {
                MainViewModel.Instance.sharedSearch = Search;
            }
            Parallel.ForEach(Mods, new ParallelOptions { MaxDegreeOfParallelism = 4 }, card =>
            {
                card.Visible = false;
            });
        }

        /// <summary>
        /// Reload all mods from nebula
        /// </summary>
        public void ReloadRepoCommand()
        {
            Knossos.ResetBasePath();
        }

        /// <summary>
        /// Clears all mods in view
        /// </summary>
        public void ClearView()
        {
            Mods.Clear();
        }

        /// <summary>
        /// Adds a single mod into the view, it will be inserted in order depending on the current select sort mode
        /// </summary>
        /// <param name="modJson"></param>
        public void AddMod(Mod modJson)
        {
            var modCard = Mods.FirstOrDefault(m => m.ID == modJson.id);
            if (modCard == null)
            {
                var card = new NebulaModCardViewModel(modJson);
                ModTags.AddModFiltersRuntime(modJson);
                Mods.Add(card);
                Mods.Sort();
                if(IsTabOpen)
                    _ = card.LoadImage();
            }
            else
            {
                //Update? Should NOT be needed for Nebula mods
            }
        }

        /// <summary>
        /// Adds a new list of mods into the view
        /// </summary>
        /// <param name="modList"></param>
        public void AddMods(List<Mod> modList)
        {
            Task.Factory.StartNew(() => {
                Parallel.ForEach(modList, new ParallelOptions { MaxDegreeOfParallelism = 4 }, mod =>
                {
                    Mods.Add(new NebulaModCardViewModel(mod));
                });
                Mods.Sort();
                if(IsTabOpen)
                {
                    Parallel.ForEach(Mods, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async card =>
                    {
                        await card.LoadImage();
                    });
                }
                foreach(var mod in modList)
                {
                    ModTags.AddModFiltersRuntime(mod);
                }
                isLoading = false;
            });
        }

        /// <summary>
        /// Change sorting mode and re-order the list of mods
        /// </summary>
        /// <param name="sort"></param>
        internal void ChangeSort(object sort)
        {
            var newSort = ModSortType.unsorted;
            if (sort is ModSortType)
            {
                newSort = (ModSortType)sort;
            }
            else
            {
                newSort = (ModSortType)Enum.Parse(typeof(ModSortType), (string)sort);
            }

            if (newSort != localSort)
            {
                localSort = newSort;
                Knossos.globalSettings.sortType = newSort;
                Mods.Sort(); //It will use NebulaModCardViewModel.CompareTo()
            }
            
            SortString = "Sorted by " + newSort;
        }

        /// <summary>
        /// Changes a modcard to "installing" mode
        /// </summary>
        /// <param name="id"></param>
        /// <param name="cancelToken"></param>
        public void SetInstalling(string id, CancellationTokenSource cancelToken)
        {
            var modCard = Mods.FirstOrDefault(m => m.ID == id);
            if (modCard != null)
            {
                modCard.InstallingMod(cancelToken);
            }
        }

        /// <summary>
        /// Removes a mod id from view
        /// </summary>
        /// <param name="id"></param>
        public void RemoveMod(string id)
        {
            var modCard = Mods.FirstOrDefault(m => m.ID == id);
            if (modCard != null)
            {
                Mods.Remove(modCard);
            }
        }

        /// <summary>
        /// Cancels a mod install of a mod id in view, removing the "installing" mode 
        /// </summary>
        /// <param name="id"></param>
        public void CancelModInstall(string id)
        {
            var modCard = Mods.FirstOrDefault(m => m.ID == id);
            if (modCard != null)
            {
                modCard.CancelInstall();
            }
        }

        /// <summary>
        /// Open the retails FS2 installer window
        /// </summary>
        internal async void InstallFS2Command()
        {
            var dialog = new Fs2InstallerView();
            dialog.DataContext = new Fs2InstallerViewModel(dialog);
            await dialog.ShowDialog<Fs2InstallerView?>(MainWindow.instance);
            Fs2Present = Knossos.retailFs2RootFound;
        }        

        /// <summary>
        /// Find if a modcard is added to this view by mod id, and return a reference to it
        /// </summary>
        /// <param name="modId"></param>
        /// <returns>NebulaModcardViewModel or null</returns>
        public NebulaModCardViewModel? GetModCardByID(string modId)
        {
            return Mods.FirstOrDefault(m => m.ID == modId);
        }
    }
}
