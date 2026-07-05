using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Knossos.NET.Classes;
using Knossos.NET.Models;

namespace Knossos.NET.ViewModels
{
    /// <summary>
    /// Mod Cleanup Window View Model
    /// </summary>
    public partial class CleanupKnossosLibraryViewModel : ViewModelBase
    {
        internal ObservableCollection<CheckableModListViewModel> DeletableMods { get; set; } = new ObservableCollection<CheckableModListViewModel>();
        
        public event EventHandler? OnRequestClose;

        public async void LoadRemovableMods()
        { 
            await Task.Run(async () => 
            {
                try
                {
                    var modsToKeep = new Collection<Mod>();

                    var newestMod = new Dictionary<string, string>();
                    foreach (var mod in Knossos.GetInstalledModList(null))
                    {
                        //Always skip retail
                        if(mod.id == "FS2")
                        {
                            modsToKeep.Add(mod);
                            continue;
                        }

                        //Always keep mods in dev mode
                        if (mod.devMode)
                        {
                            modsToKeep.Add(mod);
                            continue;
                        }

                        if (!newestMod.ContainsKey(mod.id) || SemanticVersion.Compare(newestMod[mod.id], mod.version) < 0)
                        {
                            newestMod[mod.id] = mod.version;
                        }
                    }

                    foreach (var newestModVersion in newestMod)
                    {
                        //Keep newest version of mod
                        Mod mod = Knossos.GetInstalledMod(newestModVersion.Key, newestModVersion.Value)!;
                        modsToKeep.Add(mod);
                        
                        //And all of its dependencies, both those it has set from nebula, and those set as a manual override
                        var dependencyList = mod.GetModDependencyList() ?? Enumerable.Empty<ModDependency>().Union(mod.GetModDependencyList(true) ?? Enumerable.Empty<ModDependency>()).Distinct();
                        foreach (var dependency in dependencyList)
                        {
                            var resolved = dependency.SelectMod();
                            if (resolved != null)
                                modsToKeep.Add(resolved);
                        }
                    }

                    var modViewModels = new Dictionary<Mod, CheckableModListViewModel>();
                    var modDependencies = new Dictionary<Mod, Collection<Mod>>();
                    foreach (var toRemove in Knossos.GetInstalledModList(null).Except(modsToKeep.Distinct()))
                    {
                        //Make a checkbox for any mod not to keep
                        modViewModels.Add(toRemove, new CheckableModListViewModel(toRemove));
                       
                        //Also, figure out its dependencies in case the user selects to keep the mod anyways
                        var dependencyList = toRemove.GetModDependencyList(false, true) ?? Enumerable.Empty<ModDependency>().Union(toRemove.GetModDependencyList(true, true) ?? Enumerable.Empty<ModDependency>()).Distinct();
                        var dependencyModList = new Collection<Mod>();
                        
                        foreach (var dep in dependencyList)
                        {
                            var depMod = dep.SelectMod();
                            if (depMod != null)
                            {
                                dependencyModList.Add(depMod);
                            }
                        }
                        
                        modDependencies.Add(toRemove, dependencyModList);
                    }
                    
                    //Add the Checkboxes and link their dependencies
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        foreach(var mod in modViewModels.OrderBy(mod => mod.Key.tile))
                        {
                            var dependencyViews = new Collection<CheckableModListViewModel>();
                            
                            foreach (var dep in modDependencies[mod.Key])
                            {
                                //If this mod depends on any other mod we could be removing, make sure that we don't remove the dependency if we don't remove this mod
                                if (modViewModels.ContainsKey(dep))
                                    dependencyViews.Add(modViewModels[dep]);
                            }
                            
                            mod.Value.onModCheckChangedHandler = (modChanged, modChecked) =>
                            {
                                foreach (var dependency in dependencyViews)
                                    dependency.SetModCheckedEnabled(modChecked);
                            };
                            
                            DeletableMods.Add(mod.Value);
                        }
                    }, DispatcherPriority.Default);
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "CleanupKnossosLibraryViewModel.LoadRemovableMods", ex);
                }
            });
        }

        internal async void Cleanup()
        {
            await Task.Run(() => { 
                foreach (var modView in DeletableMods)
                {
                    if (!modView.ModChecked)
                        continue;

                    var mod = modView.mod;
                    if (mod == null)
                        continue;

                    Knossos.RemoveMod(mod);
                }
            });
            MainViewModel.Instance?.RunModStatusChecks();
            OnRequestClose?.Invoke(this, EventArgs.Empty);
        }
    }
}
