using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using Knossos.NET.Views;
using ObservableCollections;

namespace Knossos.NET.ViewModels
{
    /// <summary>
    /// Installed mod card container view, this is the "Home" tab
    /// </summary>
    public partial class ModListViewModel : ViewModelBase
    {
        [ObservableProperty]
        internal bool sorting = true;

        /// <summary>
        /// For the knossos Loading animation 
        /// </summary>
        [ObservableProperty]
        internal LoadingIconViewModel loadingAnimation;

        /// <summary>
        /// Current Sort Mode
        /// </summary>
        internal ModSortType localSort = ModSortType.name;

        [ObservableProperty]
        internal bool fs2Present = true;

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


        //The actual collection were the mods are
        private ObservableList<ModCardViewModel> Mods = new ObservableList<ModCardViewModel>();
        //A hook for the UI, do not access directly
        internal NotifyCollectionChangedSynchronizedViewList<ModCardViewModel> CardsView { get; set; }

        public ModListViewModel()
        {
            LoadingAnimation = new LoadingIconViewModel();
            CardsView = Mods.ToNotifyCollectionChangedSlim(SynchronizationContextCollectionEventDispatcher.Current);
        }

        public void ApplyTagFilter(int tagIndex)
        {
            if (MainViewModel.Instance != null)
            {
                var tags = ModTags.GetListAllFilters();
                if (tags.Count() > tagIndex)
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
                if (ModTags.IsFilterPresentInModID(card.ID, "Hidden"))
                {
                    visibility = false;
                    if (MainViewModel.Instance != null && MainViewModel.Instance.tagFilter.FirstOrDefault(f => f == "Hidden") != null)
                    {
                        visibility = true;
                    }
                }
                else
                { 
                    if (MainViewModel.Instance != null && MainViewModel.Instance.tagFilter.Any())
                    {
                        visibility = false;
                        foreach (var filter in MainViewModel.Instance.tagFilter)
                        {
                            if(filter == "Hidden")
                            {
                                if(MainViewModel.Instance.tagFilter.Count == 1)
                                    visibility = true;
                                continue;
                            }
                            if (card.ID != null && ModTags.IsFilterPresentInModID(card.ID, filter))
                            {
                                visibility = true;
                                break;
                            }
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
        /// 
        /// </summary>
        public void OpenTab()
        {
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

            if (Knossos.initIsComplete)
            {
                Fs2Present = Knossos.retailFs2RootFound;
            }

            SortString = "Sorted by " + localSort;         
            BuildFilterString();
        }

        /// <summary>
        /// Reset currently selected filters
        /// </summary>
        public void ResetFilters()
        {
            ModListView.Instance?.GenerateFilterButtons();
            MainViewModel.Instance?.tagFilter.Clear();
            ApplyFilters();
        }

        public void CloseTab()
        {
            if (MainViewModel.Instance != null)
                MainViewModel.Instance.sharedSearch = Search;
            Parallel.ForEach(Mods, new ParallelOptions { MaxDegreeOfParallelism = 4 }, card =>
            {
                card.Visible = false;
            });
        }

        /// <summary>
        /// Clears all mods in view
        /// </summary>
        public void ClearView()
        {
            Mods.Clear();
        }

        /// <summary>
        /// Updates border color and status icons on all mod cards
        /// </summary>
        public void RunModStatusChecks()
        {
            Mods.ForEach(m => m.RefreshSpecialIcons());
            Mods.ForEach(m => m.CheckDependencyActiveVersion());
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
                int i;
                for (i = 0; i < Mods.Count; i++)
                {
                    if (Mods[i].ActiveVersion != null)
                    {
                        if(Mod.SortMods(Mods[i].ActiveVersion!,modJson) > 0)
                        {
                            break;
                        }
                    }
                }
                Mods.Insert(i, new ModCardViewModel(modJson));
                ModTags.AddModFiltersRuntime(modJson);
            }
            else
            {
                modCard.AddModVersion(modJson);
            }
        }

        /// <summary>
        /// Change sorting mode and re-order the list of mods
        /// </summary>
        /// <param name="sort"></param>
        internal void ChangeSort(object sort)
        {
            Sorting = true;
            LoadingAnimation.Animate = 1;
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
                Mods.Sort(); //It will use ModCardViewModel.CompareTo()
            }
            SortString = "Sorted by " + newSort;
            LoadingAnimation.Animate = 0;
            Sorting = false;
        }

        /// <summary>
        /// Calls a "UpdateIsAvailable(value)" on the mod card
        /// This would be a external way to telling the mod card that the mod has updates available
        /// </summary>
        /// <param name="id"></param>
        /// <param name="value"></param>
        public void UpdateIsAvailable(string id,bool value)
        {
            var modCard = Mods.FirstOrDefault(m => m.ID == id);
            if (modCard != null)
            {
                modCard.UpdateIsAvailable(value);
            }
        }

        /// <summary>
        /// Remove a mod card from view
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
        /// Deletes a specific mod version from the UI modcard version list
        /// IF this is the last version of a mod the modcard will be deleted instead
        /// </summary>
        /// <param name="mod"></param>
        public void RemoveModVersion(Mod mod)
        {
            var modCard = Mods.FirstOrDefault(m => m.ID == mod.id);
            if (modCard != null)
            {
                if(modCard.GetNumberOfModVersions() > 1)
                {
                    modCard.DeleteModVersion(mod);
                }
                else
                {
                    Mods.Remove(modCard);
                }
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
        /// <returns>ModcardViewModel or null</returns>
        public ModCardViewModel? GetModCardByID (string modId)
        {
            return Mods.FirstOrDefault (m => m.ID == modId);
        }
    }
}
