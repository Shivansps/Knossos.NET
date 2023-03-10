using System;
using System.Text.Json;
using System.IO;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Knossos.NET.Classes;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Text;

namespace Knossos.NET.Models
{
    /*
        The "mod class" variables math the json properties for the mod.json file that is located at the root of the mod folder.
        In order to maintain compatibility with other launchers all properties saved in the mod.json file must be
        in the original type and value range. Adding new data with additional values is ok.
        Most of the comments on the original data structure were copied from https://github.com/ngld/old-knossos/blob/develop/schema.txt
     */

    public class Mod
    {
        public bool installed { get; set; }
        public string id { get; set; } = string.Empty; // required, internal *unique* identifier, should be URL friendly, never shown to the user
        public string title { get; set; } = string.Empty; // required, a UTF-8 compatible string, displayed to the user
        /*
            Tells the client if this mod depends on retail files (mod), 
            contains executables (engine / tool) or is a Total Conversion(tc). 
            ext is not yet finished.
        */
        public string? type { get; set; } // "<mod|tc|engine|tool|ext>",  Folder Structure: <Knossos>\TC\MOD-VER, <Knossos>\TC\TC-VER, <Knossos>\bin\ENGINE-VER, <Knossos>\FS2(TC)\<Retail fs2> + <MOD-VER FOLDERS>
        public string? parent { get; set; } // null if type=tc and tc name if type=mod. Examples TC: FS2, Wing_commander_saga, Solaris, etc. 
        public string version { get; set; } = "0.0.0"; // required, http://semver.org/
        [JsonPropertyName("_private")]
        public bool isPrivate { get; set; } = false;
        public string? stability { get; set; } // "<stable|rc|nightly>"  default: stable. Only applies to type == engine. 
        public string? description { get; set; } // optional, with optional html format, should match the mod.ini's description
        public string? notes { get; set; } // optional, these will be displayed during the installation.
        public string? logo { get; set; } // "<path to image>", default: null. This the old mod.ini logo, legacy support only, 255×112.
        public string? tile { get; set; } // "<path to image>", optional, default: null, Used in the library view. 150×225. Under some unknown condition this can be a https url.
        public string? banner { get; set; } // "<path to image>", optional, default: null, Used in the mod detail view. 1070x300. Under some unknown condition this can be a https url.
        [JsonPropertyName("release_thread")]
        public string? releaseThread { get; set; } // optional url string, default: null, Will display a button in the details view which opens the given link
        public List<string> videos { get; set; } = new List<string>();  // optional, default: [], A list of video links (the links will be loaded in an iframe to display the videos in the mod details view)
        public List<string> screenshots { get; set; } = new List<string>(); // optional, default: [], A list of images to be displayed in the mod details view.
        public List<string> attachments { get; set; } = new List<string>();
        [JsonPropertyName("first_release")]
        public string? firstRelease { get; set; } // "YYYY-MM-DD", optional, default: null, the first release formatted in ISO 8601
        [JsonPropertyName("last_update")]
        public string? lastUpdate { get; set; } // "YYYY-MM-DD", optional, default: null, the latest update formatted in ISO 8601
        public string? cmdline { get; set; } // optional, allows the modder to specify a default cmdline for this mod
        [JsonPropertyName("mod_flag")]
        public List<string> modFlag { get; set; } = new List<string>(); //it should not be used directly, and instead use GetModFlagList()
        [JsonPropertyName("dev_mode")]
        public bool devMode { get; set; } = false; // User is mod owner?
        [JsonPropertyName("custom_build")]
        public string? customBuild { get; set; }
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

        public Mod()
        {
        }

        public Mod(string modPath, string folderName)
        {
            ParseJson(modPath);
            if(type != "engine")
            {
                modSettings.Load(modPath);
            }
            this.fullPath = modPath;
            this.folderName = folderName;
        }

        public override string ToString()
        {
            return title + " " + version;
        }

        /*
            Clear all the data that is not needed for normal operation
            (so everything not needed to play or view details/settings)
        */
        public void ClearUnusedData()
        {
            notes = null;
            attachments.Clear();
            foreach (ModPackage pkg in packages)
            {
                pkg.notes = null;
                pkg.filelist = null;
                pkg.files = null;
                pkg.checkNotes = null;
            }
        }

        /*
            Returns of List of <ModDependency> with unsastified dependencies.
            The package list will only contain the missing packages if a valid
            semantic version is found, but it is missing packages.
        */
        public List<ModDependency> GetMissingDependenciesList()
        {
            var dependencies = GetModDependencyList();
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
                                    if (!bestMod.CheckPackageInstalled(pkg))
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

        /*
            Returns the modFlag list, takes into account user settings, if any.
        */

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

        /*
            Searchs all packages and returns a ModDependency by id.
            Null if not found.
            Takes into account user settings, if any.
        */

        public ModDependency? GetDependency(string id, bool overrideSettings = false)
        {
            var deps = GetModDependencyList(overrideSettings);
            if (deps != null)
            {
                foreach (var dep in deps)
                {
                    if (dep.id == id)
                    {
                        return dep;
                    }
                }
            }

            return null;
        }

        /*
            Returns a list of <ModJsonDependency> of all packages in the mod.
            Returns null if the mod has no dependencies or packages.
            Takes into account user settings, if any.
        */
        public List<ModDependency>? GetModDependencyList(bool overrideSettings = false)
        {
            if(modSettings.customDependencies != null && overrideSettings == false)
            {
                return modSettings.customDependencies;
            }
            else
            {
                var dependencies = new List<ModDependency>();

                if (packages != null)
                {
                    foreach (var package in packages)
                    {
                        if (package.dependencies != null)
                        {
                            dependencies.AddRange(package.dependencies);
                        }
                    }

                    if (dependencies.Count > 0)
                        return dependencies;
                }
            }

            return null;
        }

        /*
            Returns the mod cmdline taking into account user setting if any 
        */
        public string? GetModCmdLine(bool ignoreUserSettings = false)
        {
            if(modSettings.customCmdLine!= null && ignoreUserSettings == false)
            {
                return modSettings.customCmdLine;
            }
            return cmdline;
        }

        /*
            Checks if the package is installed
            returns true/false
        */
        private bool CheckPackageInstalled(string packageName)
        {
            var isInstalled = false;

            foreach (var pkg in packages)
            {
                if (pkg.name == packageName)
                {
                    isInstalled = true;
                }
            }

            return isInstalled;
        }

        /* Loads all data in the json file */
        private void ParseJson(string modPath)
        {
            try
            {
                using FileStream jsonFile = File.OpenRead(modPath + @"\mod.json");
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
                tempMod = null;
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "ModJson.ParseJson", ex);
            }
        }

        /* Saves all data to the json file */
        public void SaveJson()
        {
            try
            {
                if (fullPath != null)
                {
                    var encoderSettings = new TextEncoderSettings();
                    encoderSettings.AllowRange(UnicodeRanges.All);

                    var options = new JsonSerializerOptions
                    {
                        Encoder = JavaScriptEncoder.Create(encoderSettings),
                        WriteIndented = true
                    };

                    var json = JsonSerializer.Serialize(this, options);
                    File.WriteAllText(fullPath + @"\mod.json", json, Encoding.UTF8);
                    Log.Add(Log.LogSeverity.Information, "ModJson.SaveJson", "mod.json has been saved to " + fullPath + @"\mod.json");
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
    }

    public class ModPackage
    {
        public string name { get; set; } = string.Empty; // required
        public string? notes { get; set; } // optional
        /*
            optional, default: "recommended"
            A feature can be:
            - "required" (always installed with the mod, in fact these are the base files of the mod),
            - "recommended" (automatically selected for installation, but the user can skip them),
            - "optional" (not automatically selected, but user can add them during the install process)
        */
        public string? status { get; set; } // "<required|recommended|optional>"
        public List<ModDependency>? dependencies { get; set; }
        /*
            Knossos.Net will only take the "windows" value from here, i assume the other enviroments are called linux and mac until i can get confirmation.
            Im not sure why this exist at this point, the exec arch is indicated on Executable->Properties, so there should be no reason for having it here too.
            This data must be written to the mod.json regardless.
        */
        public string? environment { get; set; } // optional, boolean expression like "windows && X86_64 && (sse || sse2)"
        public string? folder { get; set; }
        [JsonPropertyName("is_vp")]
        public bool isVp { get; set; } // optional, whether Knossos should pack the files in a VP on upload, default: false
        public List<ModFile>? files { get; set; }
        public List<ModFilelist>? filelist { get; set; }
        public List<ModExecutable>? executables { get; set; } // optional
        [JsonPropertyName("check_notes")]
        public string? checkNotes { get; set; }

        /* Added for Internal use Only */
        [JsonIgnore]
        public bool isSelected { get; set; } = false;
        [JsonIgnore]
        public bool isEnabled { get; set; } = false;
    }

    public class ModDependency
    {
        public string id { get; set; } = string.Empty; // required
        public string? version { get; set; } // required, https://getcomposer.org/doc/01-basic-usage.md#package-versions
        public List<string>? packages { get; set; } // optional, specifies which optional and recommended packages are also required

        /*
            Returns the best installed mod that meets this dependency by semantic version, null if none.
            Also returns null if the ID is a FSO build.
        */
        public Mod? SelectMod()
        {
            var mods = Knossos.GetInstalledModList(id);
            Mod? validMod = null;

            foreach (var mod in mods)
            {
                if(SemanticVersion.SastifiesDependency(version,mod.version))
                {
                    if(validMod == null)
                    {
                        validMod = mod;
                    }
                    else
                    {
                        if(SemanticVersion.Compare(mod.version,validMod.version) > 0)
                        {
                            validMod = mod;
                        }
                    }
                }
            }

            return validMod;
        }

        public FsoBuild? SelectBuild()
        {
            var builds = Knossos.GetInstalledBuildsList(id);
            FsoBuild? validBuild = null;

            foreach (var build in builds)
            {
                if (SemanticVersion.SastifiesDependency(version, build.version))
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

            return validBuild;
        }

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
        public List<string>? urls { get; set; } // The URLs are full URLs (they contain the filename).
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
    }

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
