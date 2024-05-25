using System;
using System.Text.Json;
using System.IO;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Knossos.NET.Classes;
using System.Text.Encodings.Web;
using System.Text;
using System.Threading.Tasks;
using IniParser;
using System.Linq;
using System.Collections;

namespace Knossos.NET.Models
{
    /// <summary>
    /// "mod, tc and engine" are used in the mod.json
    /// tool and ext are unimplemented in Nebula and ignored by Knet
    /// modlegacy is used for data loaded from a mod.ini
    /// </summary>
    public enum ModType
    {
        mod,
        tc,
        engine,
        tool,
        ext,
        modlegacy
    }

    /// <summary>
    /// nebula = petty much every normal mod
    /// local = mod.ini or any mod not stored in nebula, like fs2 retail, it disables some options like update/modify
    /// </summary>
    public enum ModSource
    {
        nebula,
        local
    }

    /// <summary>
    /// Nebula int values, do not change
    /// It is not clear what can each one of the roles do and what not
    /// </summary>
    public enum ModMemberRole
    {
        Owner = 0,
        Manager = 10,
        Uploader = 20,
        Tester = 30
    }

    /*

     */

    /// <summary>
    /// The "mod class" variables math the json properties for the mod.json file that is located at the root of the mod folder.
    /// In order to maintain compatibility with other launchers all properties saved in the mod.json file must be
    /// in the original type and value range. Adding new data with additional values is ok.
    /// Most of the comments on the original data structure were copied from https://github.com/ngld/old-knossos/blob/develop/schema.txt
    /// </summary>
    public class Mod
    {
        /*
            Knet only, this defines the source of the mod and defines some behaviours
            Ex: A local mod cant be modified and is not added the nebula tab when deleted
        */
        [JsonPropertyName("mod_source")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ModSource modSource { get; set; } = ModSource.nebula;
        public bool installed { get; set; } = false;
        public string id { get; set; } = string.Empty; // required, internal *unique* identifier, should be URL friendly, never shown to the user
        public string title { get; set; } = string.Empty; // required, a UTF-8 compatible string, displayed to the user
        /*
            Tells the client if this mod depends on retail files (mod), 
            contains executables (engine / tool) or is a Total Conversion(tc). 
            ext is not yet finished.
        */
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ModType? type { get; set; } // "<mod|tc|engine|tool|ext>",  Folder Structure: <Knossos>\TC\MOD-VER, <Knossos>\TC\TC-VER, <Knossos>\bin\ENGINE-VER, <Knossos>\FS2(TC)\<Retail fs2> + <MOD-VER FOLDERS>
        public string? parent { get; set; } // null if type=tc and tc name if type=mod. Examples TC: FS2, Wing_commander_saga, Solaris, etc. 
        public string version { get; set; } = "0.0.0"; // required, http://semver.org/
        [JsonPropertyName("private")]
        public bool isPrivate { get; set; } = false;
        public string? stability { get; set; } // "<stable|rc|nightly>"  default: stable. Only applies to type == engine. 
        public string? description { get; set; } = string.Empty; // optional, with optional html format, should match the mod.ini's description
        public string? notes { get; set; } = string.Empty; // optional, these will be displayed during the installation.
        public string? logo { get; set; } // "<path to image>", default: null. This the old mod.ini logo, legacy support only, 255×112.
        public string? tile { get; set; } // "<path to image>", optional, default: null, Used in the library view. 150×225. Under some unknown condition this can be a https url.
        public string? banner { get; set; } // "<path to image>", optional, default: null, Used in the mod detail view. 1070x300. Under some unknown condition this can be a https url.
        [JsonPropertyName("release_thread")]
        public string? releaseThread { get; set; } // optional url string, default: null, Will display a button in the details view which opens the given link
        public string[]? videos { get; set; } = new string[0]; // optional, default: [], A list of video links (the links will be loaded in an iframe to display the videos in the mod details view)
        public string[]? screenshots { get; set; } = new string[0];  // optional, default: [], A list of images to be displayed in the mod details view.
        public string[]? attachments { get; set; } = new string[0];
        [JsonPropertyName("first_release")]
        public string? firstRelease { get; set; } // "YYYY-MM-DD", optional, default: null, the first release formatted in ISO 8601
        [JsonPropertyName("last_update")]
        public string? lastUpdate { get; set; } // "YYYY-MM-DD", optional, default: null, the latest update formatted in ISO 8601
        public string? cmdline { get; set; } = string.Empty; // optional, allows the modder to specify a default cmdline for this mod
        [JsonPropertyName("mod_flag")]
        public List<string> modFlag { get; set; } = new List<string>(); //it should not be used directly, and instead use GetModFlagList()
        [JsonPropertyName("dev_mode")]
        public bool devMode { get; set; } = false; // User is mod owner?
        [JsonPropertyName("custom_build")]
        public string? customBuild { get; set; }
        public string[]? owners { get; set; }
        public List<ModPackage> packages { get; set; } = new List<ModPackage>();

        /* Added for Internal use Only */
        [JsonIgnore]
        public string fullPath { get; set; } = string.Empty;
        [JsonIgnore]
        public string folderName { get; set; } = string.Empty;
        [JsonIgnore]
        public ModSettings modSettings { get; set; } = new ModSettings();
        [JsonIgnore]
        public bool isSelected { get; set; } = false;
        [JsonIgnore]
        public bool isEnabled { get; set; } = false;
        [JsonIgnore]
        public bool isNewMod { get; set; } = false;
        [JsonIgnore]
        public bool inNebula { get; set; } = false;
        [JsonIgnore]
        public List<ModMember>? members { get; set; } = null;
        [JsonIgnore]
        public bool fullDataLoaded { get; set; } = false;

        public Mod()
        {
        }

        /// <summary>
        /// Creates a mod class and parses the mod.json file
        /// Also loads mod_settings.json if it exist in folder
        /// Modtype should only be passed when loading a mod.ini (modlegacy)
        /// </summary>
        /// <param name="modPath"></param>
        /// <param name="folderName"></param>
        /// <param name="type"></param>
        public Mod(string modPath, string folderName, ModType? type = null)
        {
            this.fullPath = modPath;
            this.folderName = folderName;
            if (type == ModType.modlegacy)
            {
                ParseIni(modPath);
            }
            else
            {
                ParseJson(modPath);
            }
            if (type != ModType.engine)
            {
                modSettings.Load(fullPath);
            }
        }

        /// <summary>
        /// Returns mod title + version
        /// </summary>
        public override string ToString()
        {
            return title + " " + version;
        }

        /// <summary>
        /// Clear all the data that is not needed for normal operation
        /// (so everything not needed to play or view details/settings)
        /// This was usefull for the regular repo.json now the effect is minimal as it is only
        /// applied to installed mods
        /// </summary>
        public void ClearUnusedData()
        {
            //Do NOT clear this for private mods
            //There is no way to get it back whiout downloading the entire private mods array again
            if (!isPrivate)
            {
                notes = null;
                fullDataLoaded = false;
                foreach (ModPackage pkg in packages)
                {
                    if (!devMode)
                    {
                        pkg.notes = null;
                    }
                    pkg.filelist = null;
                    pkg.files = null;
                    pkg.checkNotes = null;
                }
            }
        }

        /// <summary>
        /// Returns of List of <ModDependency> with unsastified dependencies.
        /// The package list will only contain the missing packages if a valid
        /// semantic version is found, but it is missing packages.
        /// Includes mods, tcs and engines
        /// </summary>
        /// <param name="overrideSettings"></param>
        /// <param name="filterdeps"></param>
        /// <returns>List of ModDependency or empty list</returns>
        public List<ModDependency> GetMissingDependenciesList(bool overrideSettings = false, bool filterdeps = false)
        {
            var dependencies = GetModDependencyList(overrideSettings, filterdeps);
            List<ModDependency> missingDependencyList = new List<ModDependency>();

            if (dependencies != null)
            {
                foreach (var dep in dependencies)
                {
                    /* Dont search mods if it is a official engine build */
                    if (dep.id != "FSO")
                    { 
                        var bestMod = dep.SelectMod();

                        if (bestMod != null)
                        {
                            /* Ok we got a valid mod, lets check the packages */
                            if (dep.packages != null)
                            {
                                var missingPkg = new List<string>();
                                foreach (var pkg in dep.packages)
                                {
                                    if (!bestMod.IsPackageInstalled(pkg))
                                    {
                                        missingPkg.Add(pkg);
                                    }
                                }

                                if (missingPkg.Count > 0)
                                {
                                    /* A missing package is detected, add a copy of the dependency to the list, with the new package list. */
                                    var missingDep = new ModDependency();
                                    missingDep.id = dep.id;
                                    missingDep.version = dep.version;
                                    missingDep.packages = missingPkg;
                                    missingDep.originalDependency = dep;
                                    missingDependencyList.Add(missingDep);
                                }
                            }
                        }
                        else
                        {
                            /* No mod of the same id is installed, meets the minimum version required or this id is an engine build  */
                            if (dep.SelectBuild() == null)
                            {
                                missingDependencyList.Add(dep);
                            }
                        }
                    }
                    else
                    {
                        if (dep.SelectBuild() == null)
                        {
                            missingDependencyList.Add(dep);
                        }
                    }
                }
            }

            return missingDependencyList;
        }

        /// <summary>
        /// Returns the modFlag list, takes into account user settings, if any.
        /// </summary>
        /// <param name="overrideSettings"></param>
        /// <returns>modflag list or empty list</returns>
        public List<string> GetModFlagList(bool overrideSettings = false)
        {
            if(modSettings.customModFlags != null && overrideSettings == false)
            {
                return modSettings.customModFlags;
            }
            else
            {
                return modFlag;
            }
        }

        /// <summary>
        /// Searchs all packages and returns a ModDependency by id.
        /// Null if not found.
        /// Takes into account user settings, if any.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="overrideSettings"></param>
        /// <returns>ModDependency or null if not found</returns>
        public ModDependency? GetDependency(string id, bool overrideSettings = false)
        {
            var deps = GetModDependencyList(overrideSettings);
            if (deps != null)
            {
                return deps.FirstOrDefault(d => d.id == id);
            }

            return null;
        }

        /// <summary>
        /// Returns a list of <ModDependency> of all packages in the mod.
        /// Returns null if the mod has no dependencies or packages.
        /// Takes into account user settings, if any.
        /// Optional option to filter dependencies
        /// Filter dependencies option is going to resolve duplicated dependency ids into 
        /// a single dependency that includes them all
        /// If mod is devmode it ignores all dependencies from a disabled package
        /// </summary>
        /// <param name="overrideSettings"></param>
        /// <param name="filterDependencies"></param>
        /// <returns>ModDependency list or null</returns>
        public List<ModDependency>? GetModDependencyList(bool overrideSettings = false, bool filterDependencies = false)
        {
            if(modSettings.customDependencies != null && overrideSettings == false)
            {
                if (filterDependencies)
                    return FilterDependencies(modSettings.customDependencies);

                return modSettings.customDependencies;
            }
            else
            {
                var dependencies = new List<ModDependency>();

                if (packages != null)
                {
                    foreach (var package in packages)
                    {
                        if (package.dependencies != null && ( !devMode || package.isEnabled ))
                        {
                            dependencies.AddRange(package.dependencies);
                        }
                    }

                    if (dependencies.Count > 0)
                    {
                        if(filterDependencies)
                            return FilterDependencies(dependencies);

                        return dependencies;
                    }
                }
            }

            return null;
        }

        public (List<Mod>?, List<FsoBuild>?)?  ResolveModDependencies(bool overrideSettings = false)
        {
            //First lets get all dependencies
            var depList = GetModDependencyList(overrideSettings, false);

            if (depList == null)
            {
                return null;
            }

            var modList = new List<Mod>();
            var buildList = new List<FsoBuild>();

            //Second, resolve each one of them individually, fso builds will be ignored
            depList.ForEach(dep =>
            {
                if (dep != null)
                {
                    var mod = dep.SelectMod();
                    if (mod != null)
                    {
                        modList.Add(mod);
                    }
                    else
                    {
                        var build = dep.SelectBuild();
                        if (build != null)
                        {
                            buildList.Add(build);
                        }
                        else
                        {
                            Log.Add(Log.LogSeverity.Warning, "Mod.ResolveModDependencies()", "Unresolved dependency: " + dep + " for mod: " + this);
                        }
                    }
                }
            });

            //Lets group each mod by id
            if (modList.Count() > 0)
            {
                foreach (var group in modList.GroupBy(d => d.id))
                {

                }
            }
            return (modList, buildList);
        }

        /// <summary>
        /// Pre-Process the mod dependency list removing irrelevant or duplicated dependency entries    
        /// In case of conflict both are passed as this is not a critical stop in knet
        /// Version=null means "any" so others must decide if a version is specified
        /// </summary>
        /// <param name="unFilteredDepList"></param>
        /// <returns>List<ModDependency></returns>
        private List<ModDependency> FilterDependencies(List<ModDependency> unFilteredDepList)
        {
            try
            {
                //Stage 1 Copy list eliminating duplicates
                List<ModDependency> noDuplicates = new List<ModDependency>();
                
                foreach (var dep in unFilteredDepList)
                {
                    if (noDuplicates.FirstOrDefault(d => d.id == dep.id && d.version == dep.version) == null)
                    {
                        noDuplicates.Add(dep);
                    }
                }

                //Stage 2 
                //Remove irrelevants
                //If an ID repeats more than one time we need to remove the ones that arent needed
                //For example:
                //Any (null) is irrelevant if anything else is present
                //Equal (==) have the higher priority here, if an equal is set, higher, lower and revisions are irrelevant
                //Revisions (~), have the 2nd highest priority, if a revision is present higher and lower are irrelevant
                //Minor or equal (<=), have the 3nd highest priority
                //Additionally, if multiple cases are present for ~, >= and <= we should only pass the one that includes all others
                //In case of conflict pass all.
                //Version posibilities are:
                //  null      -> any is fine, this does not decides anything
                //  "4.6.7"   -> equal to version
                //  ">=4.6.7" -> all versions over this are fine, meaning we should keep the lower one
                //  "<=4.6.7" -> all versions below this are fine, meaning we should keep the higher one
                //  "~4.6.1"  -> A revision equal or higher inside this minor version, we should keep the higher one

                var idGroup = noDuplicates.GroupBy(d => d.id);
                List<ModDependency> filtered = new List<ModDependency>();

                foreach (var group in idGroup)
                {
                    if (group.Count() > 1)
                    {
                        //More than one per ID
                        List<ModDependency> any = new List<ModDependency>();
                        List<ModDependency> equal = new List<ModDependency>();
                        List<ModDependency> higher = new List<ModDependency>();
                        List<ModDependency> lower = new List<ModDependency>();
                        List<ModDependency> rev = new List<ModDependency>();

                        foreach (var dep in group)
                        {
                            if (dep.version == null)
                            {
                                any.Add(dep);
                            }
                            else if (dep.version.Contains(">="))
                            {
                                higher.Add(dep);
                            }
                            else if (dep.version.Contains("<="))
                            {
                                lower.Add(dep);
                            }
                            else if (dep.version.Contains("~"))
                            {
                                rev.Add(dep);
                            }
                            else
                            {
                                equal.Add(dep);
                            }
                        }


                        //Process the dependencies by priority order 
                        // ==  /  ~  /  <=  /  >=

                        //==
                        //Note: There is no need to compare equals to other equals
                        equal.ForEach(eq => {
                            //Remove any "rev" that can be sastified by this equal
                            rev.ToList().ForEach(r =>
                            {
                                if (SemanticVersion.SastifiesDependency(r.version, eq.version))
                                {
                                    rev.Remove(r);
                                }
                            });
                            //Remove any "lower" that can be sastified by this equal
                            lower.ToList().ForEach(l =>
                            {
                                if (SemanticVersion.SastifiesDependency(l.version, eq.version))
                                {
                                    lower.Remove(l);
                                }
                            });
                            //Remove any "higher" that can be sastified by this equal
                            higher.ToList().ForEach(h =>
                            {
                                if (SemanticVersion.SastifiesDependency(h.version, eq.version))
                                {
                                    higher.Remove(h);
                                }
                            });
                        });

                        //~
                        rev.ToList().ForEach(re => {
                            //Remove any "rev" that can be sastified by this other rev
                            rev.ToList().ForEach(r =>
                            {
                                if (re != r && SemanticVersion.SastifiesDependency(r.version, re.version!.Replace("~", "")))
                                {
                                    rev.Remove(r);
                                }
                            });
                        });
                        rev.ForEach(re => {
                            //Remove any "lower" that can be sastified by this rev
                            lower.ToList().ForEach(l =>
                            {
                                if (SemanticVersion.SastifiesDependency(l.version, re.version!.Replace("~", "")))
                                {
                                    lower.Remove(l);
                                }
                            });
                            //Remove any "higher" that can be sastified by this rev
                            higher.ToList().ForEach(h =>
                            {
                                if (SemanticVersion.SastifiesDependency(h.version, re.version!.Replace("~", "")))
                                {
                                    higher.Remove(h);
                                }
                            });
                        });

                        //<=
                        lower.ToList().ForEach(lo => {
                            //Remove any "lower" that can be sastified by this other lower
                            lower.ToList().ForEach(l =>
                            {
                                if (lo != l && SemanticVersion.SastifiesDependency(l.version, lo.version!.Replace("<=", "")))
                                {
                                    lower.Remove(l);
                                }
                            });
                        });
                        lower.ForEach(lo => {
                            //Remove any "higher" that can be sastified by this lower
                            higher.ToList().ForEach(h =>
                            {
                                if (SemanticVersion.SastifiesDependency(h.version, lo.version!.Replace("<=", "")))
                                {
                                    higher.Remove(h);
                                }
                            });
                        });

                        //>=
                        higher.ToList().ForEach(hi => {
                            //Remove any "higher" that can be sastified by this higher
                            higher.ToList().ForEach(h =>
                            {
                                if (hi != h && SemanticVersion.SastifiesDependency(h.version, hi.version!.Replace(">=", "")))
                                {
                                    higher.Remove(h);
                                }
                            });
                        });


                        //FINISH
                        //Dump everything into filtered deps
                        if (any.Any() && !equal.Any() && !higher.Any() && !lower.Any() && !rev.Any())
                        {
                            filtered.Add(any.First());
                        }
                        if (equal.Any())
                        {
                            foreach (var d in equal)
                                filtered.Add(d);
                        }
                        if (rev.Any())
                        {
                            foreach (var d in rev)
                                filtered.Add(d);
                        }
                        if (higher.Any())
                        {
                            foreach (var d in higher)
                                filtered.Add(d);
                        }
                        if (lower.Any())
                        {
                            foreach (var d in lower)
                                filtered.Add(d);
                        }
                    }
                    else
                    {
                        //Only one per ID
                        var onlyOne = group.FirstOrDefault();
                        if(onlyOne != null)
                        {
                            filtered.Add(onlyOne);
                        }
                    }
                }
                return filtered;
            }catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Mod.FilterDependencies()", ex);
                return unFilteredDepList;
            }
        }

        /// <summary>
        /// Returns the mod cmdline taking into account user setting if any 
        /// </summary>
        /// <param name="ignoreUserSettings"></param>
        /// <returns>mod cmdline or null if not set by mod or user</returns>
        public string? GetModCmdLine(bool ignoreUserSettings = false)
        {
            if(modSettings.customCmdLine!= null && ignoreUserSettings == false)
            {
                return modSettings.customCmdLine;
            }
            return cmdline;
        }

        /// <summary>
        /// Checks if the package name is installed
        /// </summary>
        /// <param name="packageName"></param>
        /// <returns>true/false</returns>
        public bool IsPackageInstalled(string packageName)
        {
            return packages.FirstOrDefault(p=>p.name == packageName) != null ? true : false;
        }

        /// <summary>
        /// Loads all data from the mod.json file
        /// Any new variable must be added here or data will not be loaded
        /// </summary>
        /// <param name="modPath"></param>
        private void ParseJson(string modPath)
        {
            try
            {
                using FileStream jsonFile = File.OpenRead(modPath + Path.DirectorySeparatorChar + "mod.json");
                var tempMod = JsonSerializer.Deserialize<Mod>(jsonFile)!;
                jsonFile.Close();
                installed = tempMod.installed;
                id = tempMod.id;
                title = tempMod.title;
                type = tempMod.type;
                parent = tempMod.parent;
                version = tempMod.version;
                stability = tempMod.stability;
                description = tempMod.description;
                notes = tempMod.notes;
                isPrivate = tempMod.isPrivate;
                logo = tempMod.logo;
                tile = tempMod.tile;
                banner = tempMod.banner;
                releaseThread = tempMod.releaseThread;
                videos = tempMod.videos;
                screenshots = tempMod.screenshots;
                attachments = tempMod.attachments;
                firstRelease = tempMod.firstRelease;
                lastUpdate = tempMod.lastUpdate;
                cmdline = tempMod.cmdline;
                modFlag = tempMod.modFlag;
                devMode = tempMod.devMode;
                customBuild = tempMod.customBuild;
                packages = tempMod.packages;
                owners = tempMod.owners;
                modSource = tempMod.modSource;
                tempMod = null;
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "ModJson.ParseJson", ex);
            }
        }

        /// <summary>
        /// Load all data from mod.ini
        /// </summary>
        /// <param name="modPath"></param>
        private void ParseIni(string modPath)
        {
            try
            {
                var iniParser = new FileIniDataParser();
                var iniFile = iniParser.ReadFile(modPath + Path.DirectorySeparatorChar + "mod.ini");
                var dir = new DirectoryInfo(modPath);
                
                if(dir.Name.Contains(" "))
                {
                    try
                    {
                        Log.Add(Log.LogSeverity.Warning, "Mod.ParseIni", "Local Mod Folder: " + dir.Name + ". Contains spaces in the folder name, this is not supported Knet will attempt to rename it to: " + dir.Name.Replace(" ", "_"));
                        dir.MoveTo(dir.Parent!.FullName+Path.DirectorySeparatorChar+dir.Name.Replace(" ", "_"));
                        fullPath = dir.FullName;
                        folderName = dir.Name;
                    }
                    catch (Exception ex)
                    {
                        Log.Add(Log.LogSeverity.Error, "Mod.ParseIni", ex);
                        Log.Add(Log.LogSeverity.Error, "Mod.ParseIni", "An error has ocurred while renaming the folder, this is not going to work. Mod "+ dir.Name);
                    }
                }

                installed = true;
                isPrivate = false;
                devMode = false;
                modSource = ModSource.local;

                if (iniFile["mod"]["type"] != null && iniFile["mod"]["type"].Replace(";", "") == "tc")
                {
                    type = ModType.tc;
                    parent = null;
                }
                else
                {
                    type = ModType.mod;
                    parent = iniFile["mod"]["parent"] != null ? iniFile["mod"]["parent"].Replace(";", "") : dir.Parent != null ? dir.Parent.Name : null;
                }

                
                id = iniFile["mod"]["id"] != null ? iniFile["mod"]["id"].Replace(";", "") : dir.Name.Replace(" ", "_");

                version = iniFile["mod"]["version"] != null ? iniFile["mod"]["version"].Replace(";", "") : "1.0.0-" + dir.Name.Replace(" ", "").Replace("-",".");

                title = iniFile["launcher"]["modname"] != null ? iniFile["launcher"]["modname"].Replace(";", "") : dir.Name;

                notes = iniFile["launcher"]["notes"];

                description = notes != null ? iniFile["launcher"]["infotext"] + "\n\n" + notes : iniFile["launcher"]["infotext"];

                if (description != null)
                    description = description.Replace(";", "");

                logo = iniFile["launcher"]["image255x112"];

                tile = iniFile["mod"]["tile"] != null ? iniFile["mod"]["tile"].Replace(";", "") : logo != null? logo : iniFile["launcher"]["image182x80"];

                if(tile != null)
                {
                    tile = tile.Replace(";","");
                }

                banner = iniFile["mod"]["banner"] != null ? iniFile["mod"]["banner"].Replace(";", "") : tile;

                releaseThread = iniFile["launcher"]["forum"];

                cmdline = iniFile["mod"]["cmdline"];
                if(cmdline != null)
                {
                    cmdline = cmdline.Replace(";", "");
                }

                packages = new List<ModPackage>();
                modFlag = new List<string>();
                modFlag.Add(id);
                var pkg = new ModPackage();
                pkg.name = "Core";
                pkg.status = "required";
                pkg.folder = "core";
                var deps = new List<ModDependency>();
                bool fsoDepAdded = false;

                if (iniFile["mod"]["dependencylist"] != null)
                {
                    var parts = iniFile["mod"]["dependencylist"].Replace(";", "").Split(",");
                    
                    foreach(var part in parts)
                    {
                        var dep = new ModDependency();
                        if (part.Contains("|"))
                        {
                            dep.version = part.Split("|")[1].Trim();
                            dep.id = part.Split("|")[0].Trim();
                        }
                        else
                        {
                            dep.version = null;
                            dep.id = part.Trim();
                        }
                        if(dep.id == "FSO")
                            fsoDepAdded = true;
                        deps.Add(dep);
                        modFlag.Add(dep.id);
                    }
                }
                else
                {
                    var primaryList = iniFile["multimod"]["primarylist"];
                    var secondaryList = iniFile["multimod"]["secondarylist"] != null ? iniFile["multimod"]["secondarylist"] : iniFile["multimod"]["secondrylist"];

                    if (primaryList != null && primaryList.ToLower().Contains("mediavps") || secondaryList != null && secondaryList.ToLower().Contains("mediavps"))
                    {
                        var mvp = new ModDependency();
                        mvp.version = null;
                        mvp.id = "MVPS";
                        deps.Add(mvp);
                        modFlag.Add("MVPS");
                    }
                    if (primaryList != null && primaryList.ToLower().Contains("fsport_mediavps") || secondaryList != null && secondaryList.ToLower().Contains("fsport_mediavps"))
                    {
                        var portmvp = new ModDependency();
                        portmvp.version = null;
                        portmvp.id = "fsport-mediavps";
                        deps.Add(portmvp);
                        modFlag.Add("fsport-mediavps");
                    }
                    if (primaryList != null && primaryList.ToLower().Contains("fsport") || secondaryList != null && secondaryList.ToLower().Contains("fsport"))
                    {
                        var port = new ModDependency();
                        port.version = null;
                        port.id = "fsport";
                        deps.Add(port);
                        modFlag.Add("fsport");
                    }
                }

                if(!fsoDepAdded)
                {
                    var fso = new ModDependency();
                    fso.version = null;
                    fso.id = "FSO";
                    deps.Add(fso);
                }
                pkg.dependencies = deps.ToArray();
                packages.Add(pkg);
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "ModJson.ParseIni", ex);
            }
        }

        /// <summary>
        /// Reload all data from the mod.json file
        /// </summary>
        public void ReLoadJson()
        {
            ParseJson(fullPath);
        }

        /// <summary>
        /// Saves all data to the json file
        /// </summary>
        public void SaveJson()
        {
            try
            {
                if (fullPath != null)
                {
                    var options = new JsonSerializerOptions
                    {
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                        WriteIndented = true
                    };
                    var json = JsonSerializer.Serialize(this, options);
                    File.WriteAllText(fullPath + Path.DirectorySeparatorChar + "mod.json", json, new UTF8Encoding(false));
                    Log.Add(Log.LogSeverity.Information, "ModJson.SaveJson", "mod.json has been saved to " + fullPath + Path.DirectorySeparatorChar + "mod.json");
                }
                else
                {
                    Log.Add(Log.LogSeverity.Error, "ModJson.SaveJson", "A mod " +id+ " tried to save mod.json to a null filePath.");
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "ModJson.SaveJson", ex);
            }
        }

        /// <summary>
        /// Uses the new API call introduced along with repo_minimal.json to load the missing data from Nebula using the api
        /// Can be called multiple times, the data is only loaded once unless ClearUnusedData() is called on this mod object
        /// Does nothing for installed mods
        /// </summary>
        public async Task LoadFulLNebulaData()
        {
            if(!installed && !fullDataLoaded)
            {
                try
                {
                    Log.Add(Log.LogSeverity.Information, "Mod.LoadFulLNebulaData()", "Loading full Nebula data for mod: " + this);
                    var newData = await Nebula.GetModData(id, version);
                    if (newData != null)
                    {
                        fullDataLoaded = true;
                        screenshots = newData.screenshots;
                        description = newData.description;
                        packages = newData.packages;
                        banner = newData.banner;
                        parent = newData.parent;
                        isPrivate = newData.isPrivate;
                        notes = newData.notes;
                        releaseThread = newData.releaseThread;
                        videos = newData.videos;
                        attachments = newData.attachments;
                        firstRelease = newData.firstRelease;
                        lastUpdate = newData.lastUpdate;
                        cmdline = newData.cmdline;
                        modFlag = newData.modFlag;
                        customBuild = newData.customBuild;
                        owners = newData.owners;
                    }
                }catch(Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "Mod.LoadFulLNebulaData", ex);
                }
            }
        }

        /// <summary>
        /// To use with the List .Sort()
        /// </summary>
        /// <param name="mod1"></param>
        /// <param name="mod2"></param>
        public static int CompareVersion(Mod mod1, Mod mod2)
        {
            //inverted
            return SemanticVersion.Compare(mod1.version, mod2.version);
        }

        /// <summary>
        /// Compares two mods and determines if the metadata is different
        /// Full data must be loaded on both mods for this to work properly
        /// I hate this nonsense, nebula should be updated to accept "lastUpdate" with hours and minutes to get rid of this 
        /// </summary>
        /// <returns>true if metadata is different</returns>
        public static bool IsMetaUpdate(Mod modA, Mod modB)
        {
            try
            {
                //Basic data
                if (modA.id != modB.id)
                    return true;
                if (modA.firstRelease != modB.firstRelease)
                    return true;
                if (modA.lastUpdate != modB.lastUpdate)
                    return true;
                if (modA.releaseThread != modB.releaseThread)
                    return true;
                if (!modA.modFlag.SequenceEqual(modB.modFlag))
                    return true;
                if (modA.description != modB.description)
                    return true;
                if (modA.title != modB.title)
                    return true;
                if (modA.videos == null && modB.videos != null || modA.videos != null && modB.videos == null)
                    return true;
                if (modA.videos != null && modB.videos != null && !modA.videos.SequenceEqual(modB.videos))
                    return true;

                //tile image
                // Disabled tile and banner image verification for metadata update due to how nebula stores the file names (it splits filename hashes into folders using part of the hash) this is not possible
                /*
                if (modA.tile != null && modB.tile != null)
                {
                    if (Path.GetFileName(modA.tile) != Path.GetFileName(modB.tile) && modA.tile != "kn_tile.png" && modB.tile != "kn_tile.png")
                    {
                        return true;
                    }
                }
                else
                {
                    if (modA.tile == null && modB.tile != null || modA.tile != null && modB.tile == null)
                        return true;
                }

                //banner image
                if (modA.banner != null && modB.banner != null)
                {
                    if (Path.GetFileName(modA.banner) != Path.GetFileName(modB.banner) && modA.banner != "kn_banner.png" && modB.banner != "kn_banner.png")
                    {
                        return true;
                    }
                }
                else
                {
                    if (modA.banner == null && modB.banner != null || modA.banner != null && modB.banner == null)
                        return true;
                }
                */

                //Packages
                if (modA.packages == null && modB.packages != null || modA.packages != null && modB.packages == null)
                    return true;

                var lessPkgs = modA.packages!.Count() <= modB.packages!.Count() ? modA.packages! : modB.packages!;
                var morePkgs = modA.packages!.Count() > modB.packages!.Count() ? modA.packages! : modB.packages!;

                foreach (var pkg in lessPkgs)
                {
                    var other = morePkgs.FirstOrDefault(p => p.name == pkg.name);
                    if (other == null)
                    {
                        return true;
                    }
                    else
                    {
                        if (modA.type == ModType.mod || modB.type == ModType.tc)
                        {
                            if (JsonSerializer.Serialize(other.dependencies) != JsonSerializer.Serialize(pkg.dependencies))
                                return true;
                        }
                        else
                        {
                            if (other.environment != pkg.environment)
                                return true;
                            if (JsonSerializer.Serialize(other.executables) != JsonSerializer.Serialize(pkg.executables))
                                return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Mod.IsMetaUpdate()", ex);
            }
            return false;
        }

        /// <summary>
        /// Try to track down the mod pkg that a ModDependency belongs too
        /// </summary>
        /// <param name="dependency"></param>
        /// <returns>Returns the modpkg or null if not found</returns>
        public ModPackage? FindPackageWithDependency(ModDependency? dependency)
        {
            if(dependency != null && packages != null && packages.Any())
            {
                foreach(var pkg in packages)
                {
                    if(pkg.dependencies != null && pkg.dependencies.FirstOrDefault(dep => dep == dependency) != null)
                    {
                        return pkg;
                    }
                }
            }
            return null;
        }
    }

    public class ModMember
    {
        public ModMemberRole role { get; set; }
        public string user { get; set; } = string.Empty;
    }

    public class ModPackage
    {
        public string name { get; set; } = string.Empty; // required
        public string? notes { get; set; } = string.Empty; // optional
        /*
            optional, default: "recommended"
            A feature can be:
            - "required" (always installed with the mod, in fact these are the base files of the mod),
            - "recommended" (automatically selected for installation, but the user can skip them),
            - "optional" (not automatically selected, but user can add them during the install process)
        */
        public string? status { get; set; } // "<required|recommended|optional>"
        public ModDependency[]? dependencies { get; set; } = new ModDependency[0];
        public string? environment { get; set; } // optional, boolean expression like "windows && X86_64 && (sse || sse2)"
        public string? folder { get; set; }
        [JsonPropertyName("is_vp")]
        public bool isVp { get; set; } // optional, whether Knossos should pack the files in a VP on upload, default: false
        public ModFile[]? files { get; set; } = new ModFile[0];
        public ModFilelist[]? filelist { get; set; } = new ModFilelist[0];
        public List<ModExecutable>? executables { get; set; } = new List<ModExecutable>();// optional
        [JsonPropertyName("check_notes")]
        public object? checkNotes { get; set; }

        /* Knet Only */
        /// <summary>
        /// used for pkg display in a checkbox.
        /// NOT Saved in the json
        /// </summary>
        [JsonIgnore]
        public bool isSelected { get; set; } = false;
        /// <summary>
        /// used for display (to enable/disabled chkbox) and to enable/disable the package in devmode
        /// Saved in the json
        /// </summary>
        public bool isEnabled { get; set; } = true;
        /// <summary>
        /// Used to display checkbox tooltip
        /// NOT saved in the Json
        /// </summary>
        [JsonIgnore]
        public string tooltip { get; set; } = string.Empty;
        /// <summary>
        /// Used to indicate a pkg is needed during mod install checkbox selection
        /// Used to change checkbox foreground color during mod install/modify display
        /// </summary>
        [JsonIgnore]
        public bool isRequired { get; set; } = false;
        [JsonIgnore]
        public int buildScore { get; set; } = 0;
    }

    public class ModDependency
    {
        public string id { get; set; } = string.Empty; // required
        public string? version { get; set; } // required, https://getcomposer.org/doc/01-basic-usage.md#package-versions
        public List<string>? packages { get; set; } // optional, specifies which optional and recommended packages are also required

        /// <summary>
        /// Used to store the original dependency reference during mod.GetMissingDependencies
        /// Useful to track down the original pkg this dependency belongs to
        /// mod.FindPackageWithDependency()
        /// Not saved in Json
        /// </summary>
        [JsonIgnore]
        public ModDependency? originalDependency;

        /// <summary>
        /// Returns the best installed mod that meets this dependency by semantic version, null if none.
        /// Also returns null if the ID is a FSO build.
        /// </summary>
        /// <returns>Mod or null</returns>
        public Mod? SelectMod()
        {
            var mods = Knossos.GetInstalledModList(id);
            Mod? validMod = null;

            foreach (var mod in mods)
            {

                try
                {
                    if (SemanticVersion.SastifiesDependency(version, mod.version))
                    {
                        if (validMod == null)
                        {
                            validMod = mod;
                        }
                        else
                        {
                            if (SemanticVersion.Compare(mod.version, validMod.version) > 0)
                            {
                                validMod = mod;
                            }
                        }
                    }
                } catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "Mod.SelectMod()", "Mod: "+mod.title + " " + ex.Message);
                }
            }
            return validMod;
        }

        /// <summary>
        /// Returns the best available mod on nebula that meets this dependency by semantic version, null if none.
        /// Takes an optional list with all mods, if passed it will use that list intead of checking the repo.json again
        /// </summary>
        /// <param name="mods"></param>
        /// <returns>Mod or null</returns>
        public async Task<Mod?> SelectModNebula(List<Mod>? mods = null)
        {
            if(mods == null)
            {
                mods = await Nebula.GetAllModsWithID(id);
            }

            Mod? validMod = null;

            foreach (var mod in mods)
            {
                if (mod.id == id && SemanticVersion.SastifiesDependency(version, mod.version))
                {
                    if (validMod == null)
                    {
                        validMod = mod;
                    }
                    else
                    {
                        if (mod.type == ModType.engine)
                        {
                            var stabilityV = FsoBuild.GetFsoStability(validMod.stability, validMod.id);
                            var stabilityM = FsoBuild.GetFsoStability(mod.stability, mod.id);
                            //inverted stability comparison
                            if (stabilityV > stabilityM || stabilityV == stabilityM && SemanticVersion.Compare(mod.version, validMod.version) > 0)
                            {
                                validMod = mod;
                            }
                        }
                        else
                        {
                            if (SemanticVersion.Compare(mod.version, validMod.version) > 0)
                            {
                                validMod = mod;
                            }
                        }
                    }
                }
            }

            return validMod;
        }

        /// <summary>
        /// Returns the best installed build that meets this dependency by semantic version, null if none.
        /// Optional: Only select builds with at least one valid executabl
        /// </summary>
        /// <returns>FsoBuild or null</returns>
        public FsoBuild? SelectBuild(bool onlyWithValidExecutable = false)
        {
            FsoBuild? validBuild = null;

            foreach (var build in Knossos.GetInstalledBuildsList(id, FsoStability.Stable))
            {
                if ((!onlyWithValidExecutable || onlyWithValidExecutable && build.IsValidBuild()) && SemanticVersion.SastifiesDependency(version, build.version))
                {
                  
                    if (validBuild == null)
                    {
                        validBuild = build;
                    }
                    else
                    {
                        if (SemanticVersion.Compare(build.version, validBuild.version) > 0)
                        {
                            validBuild = build;
                        }
                    }
                }
            }

            if(validBuild == null)
            {
                foreach (var build in Knossos.GetInstalledBuildsList(id, FsoStability.RC))
                {
                    if ((!onlyWithValidExecutable || onlyWithValidExecutable && build.IsValidBuild()) && SemanticVersion.SastifiesDependency(version, build.version))
                    {

                        if (validBuild == null)
                        {
                            validBuild = build;
                        }
                        else
                        {
                            if (SemanticVersion.Compare(build.version, validBuild.version) > 0)
                            {
                                validBuild = build;
                            }
                        }
                    }
                }
            }

            if (validBuild == null)
            {
                foreach (var build in Knossos.GetInstalledBuildsList(id, FsoStability.Nightly))
                {
                    if ((!onlyWithValidExecutable || onlyWithValidExecutable && build.IsValidBuild()) && SemanticVersion.SastifiesDependency(version, build.version))
                    {

                        if (validBuild == null)
                        {
                            validBuild = build;
                        }
                        else
                        {
                            if (SemanticVersion.Compare(build.version, validBuild.version) > 0)
                            {
                                validBuild = build;
                            }
                        }
                    }
                }
            }

            if (validBuild == null)
            {
                foreach (var build in Knossos.GetInstalledBuildsList(id, FsoStability.Custom))
                {
                    if ((!onlyWithValidExecutable || onlyWithValidExecutable && build.IsValidBuild()) && SemanticVersion.SastifiesDependency(version, build.version))
                    {

                        if (validBuild == null)
                        {
                            validBuild = build;
                        }
                        else
                        {
                            if (SemanticVersion.Compare(build.version, validBuild.version) > 0)
                            {
                                validBuild = build;
                            }
                        }
                    }
                }
            }

            return validBuild;
        }

        /// <summary>
        /// returns id + version
        /// </summary>
        public override string ToString()
        {
            return id + " " + version;
        }
    }

    public class ModFile
    {
        public string? filename { get; set; }
        public string? dest { get; set; } // "<destination path>"
        public string[]? checksum { get; set; } // sha256, value
        public Int64 filesize { get; set; } // Size in bytes
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string[]? urls { get; set; } // The URLs are full URLs (they contain the filename).
    }

    public class ModFilelist
    {
        public string? filename { get; set; }  // file path
        public string? archive { get; set; }
        [JsonPropertyName("orig_name")]
        public string? origName { get; set; }  // name in archive 
        public string[]? checksum { get; set; } // sha256, value
    }

    public class ModExecutable
    {
        public string? file { get; set; } // required, path to the executable (*.exe file on Windows), relative to the mod folder
        public string? label { get; set; } // <Fred FastDebug|FRED2|QTFred|QTFred FastDebug|FastDebug|null> optional, should be null for release builds and contain the name for others
        public ModProperties? properties { get; set; }

        [JsonIgnore]
        public int score { get; set; } = 0;
    }

    /// <summary>
    /// This is generated from the enviroment string at runtime
    /// </summary>
    public class ModProperties
    {
        public bool x64 { get; set; } 
        public bool sse2 { get; set; } 
        public bool avx { get; set; } 
        public bool avx2 { get; set; }

        /* Knossos.NET added */
        public bool arm64 { get; set; }
        public bool arm32 { get; set; } 
        public bool other { get; set; }
    }
}
