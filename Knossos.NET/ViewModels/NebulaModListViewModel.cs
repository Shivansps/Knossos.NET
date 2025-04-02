using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Classes;
using Knossos.NET.Models;
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

        [ObservableProperty]
        internal bool filtersEnabled = false;

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
            if (MainWindowViewModel.Instance != null)
            {
                var tags = ModTags.GetListAllFilters();
                if(tags.Count() > tagIndex)
                {
                    MainWindowViewModel.Instance.tagFilter.Add(tags[tagIndex]);
                }
                ApplyFilters();
            }
        }

        public void RemoveTagFilter(int tagIndex)
        {
            if (MainWindowViewModel.Instance != null)
            {
                var tags = ModTags.GetListAllFilters();
                if (tags.Count() > tagIndex)
                {
                    MainWindowViewModel.Instance.tagFilter.Remove(tags[tagIndex]);
                }

                if (MainWindowViewModel.Instance.tagFilter.Count < 1) {
                    FiltersEnabled = false;
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
                if (visibility && MainWindowViewModel.Instance != null && MainWindowViewModel.Instance.tagFilter.Any())
                {
                    visibility = false;
                    foreach (var filter in MainWindowViewModel.Instance.tagFilter)
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
            if (MainWindowViewModel.Instance == null){
                FilterString = "";
                FiltersEnabled = false;
                return;
            }
            
            int externalCount = MainWindowViewModel.Instance.tagFilter.Count;

            if (externalCount == 0 ){
                FilterString = "";
                FiltersEnabled = false;
                return;
            }

            int count = 0;
            FilterString = "Filtering for ";
            TextInfo myTI = new CultureInfo("en-US", false).TextInfo;

            foreach (var filter in MainWindowViewModel.Instance.tagFilter) {
                if (count > 3){ 
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

                FiltersEnabled = true;
                count++;
            }
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
                    if (MainWindowViewModel.Instance != null)
                    {
                        if (Search != MainWindowViewModel.Instance.sharedSearch)
                        {
                            Search = MainWindowViewModel.Instance.sharedSearch;
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
        }

        /// <summary>
        /// Close the tab code
        /// </summary>
        public void CloseTab()
        {
            ShowTiles = false;
            if (MainWindowViewModel.Instance != null)
            {
                MainWindowViewModel.Instance.sharedSearch = Search;
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
    }
}
