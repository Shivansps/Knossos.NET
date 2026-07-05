using Knossos.NET.Models;
using System.Collections.Generic;
using System.Linq;

namespace Knossos.NET.Classes
{
    public class ModTag
    {
        public ModTag(string modID, string? filter = null, string? tag = null)
        {
            ModID = modID;
            if(filter != null)
                Filters.Add(filter.ToLower());
            if(tag != null)
                Tags.Add(tag.ToLower());
        }

        public void AddFilter(string filter)
        {
            //special cases:
            //do not add FS2_MOD filter if FS1_MOD exist
            //Remove FS2_MOD if FS1_MOD mod is added
            if (filter.ToLower() == "fs1_mod")
                Filters.Remove("fs2_mod");
            if (filter.ToLower() == "fs2_mod" && FilterExist("fs1_mod"))
                return;
            Filters.Add(filter);
        }

        public void RemoveFilter(string filter)
        {
            Filters.Remove(filter);
        }

        /// <summary>
        /// Check if a filter exist in this modid filterlist
        /// Case insensitive
        /// </summary>
        /// <param name="filter"></param>
        /// <returns>true/false</returns>
        public bool FilterExist(string filter)
        {
            for (int i = 0; i < Filters.Count(); i++)
            {
                if(string.Compare(Filters[i], filter, true) == 0)
                {
                    return true;
                }
            }
            return false;
        }

        public List<string> GetFilters()
        {
            return Filters;
        }

        public void AddTag(string tag)
        {
            Tags.Add(tag);
        }

        /// <summary>
        /// Check if a tag exist in this modid taglist
        /// Case insensitive
        /// </summary>
        /// <param name="tag"></param>
        /// <returns>true/false</returns>
        public bool TagExist(string tag)
        {
            for (int i = 0; i < Tags.Count(); i++)
            {
                if (string.Compare(Tags[i], tag, true) == 0)
                {
                    return true;
                }
            }
            return false;
        }

        public string ModID { get; private set; } = string.Empty;
        private List<string> Filters { get; set; } = new List<string>();
        private List<string> Tags { get; set; } = new List<string>();
    }

    /// <summary>
    /// Handlers for the mod tag system
    /// Each mod id can contain any number of tags
    /// Tags are loaded from nebula and local locals at start
    /// Then we can check here if a mod ID contains a tag
    /// Note:
    /// tags are case insensitive, but ModIDs arent!
    /// </summary>
    public static class ModTags
    {
        private static List<ModTag> modTags = new List<ModTag>();

        /// <summary>
        /// Use _ for spaces and remember these are checked with string.compare so no tags that can contain another
        /// </summary>
        public enum Filters
        {
            Hidden,
            Total_Conversion,
            Retail_FS2,
            FS2_MOD,
            FS1_MOD,
            TC_MOD,
            Utility,
            Dependency,
            VR_MOD,
            Asset_Pack,
            Demo,
            Multiplayer,
            Test
        }

        /// <summary>
        /// Clear the all loaded tags and filters
        /// </summary>
        public static void Clear()
        {
            modTags.Clear();
        }

        /// <summary>
        /// Get a list of all filters loaded, without the mod id
        /// </summary>
        /// <returns>List&lt;string&gt;</returns>
        public static List<string> GetListAllFilters()
        {
            List<string> list = new List<string>();
            for (int i = 0; i < modTags.Count(); i++)
            {
                var filters = modTags[i].GetFilters();
                for (int j = 0; j < filters.Count(); j++)
                {
                    if (!list.Contains(filters[j]))
                        list.Add(filters[j]);
                }
            }
            return list;
        }

        /// <summary>
        /// Checks if a mod id contains a filter
        /// </summary>
        /// <param name="modid"></param>
        /// <param name="filter"></param>
        /// <returns>true/false</returns>
        public static bool IsFilterPresentInModID(string modid, string filter)
        {
            for (int i = 0; i < modTags.Count(); i++)
            {
                if (modTags[i].ModID == modid)
                {
                    return modTags[i].FilterExist(filter);
                }
            }
            return false;
        }

        /// <summary>
        /// Checks if a mod id contains a tag
        /// </summary>
        /// <param name="modid"></param>
        /// <param name="tag"></param>
        /// <returns>true/false</returns>
        public static bool IsTagPresentInModID(string modid, string tag)
        {
            for (int i = 0; i < modTags.Count(); i++)
            {
                if (modTags[i].ModID == modid)
                {
                    return modTags[i].TagExist(tag);
                }
            }
            return false;
        }

        /// <summary>
        /// Adds a mod filter to the list
        /// If the id and filter dosent exist already
        /// </summary>
        /// <param name="id"></param>
        /// <param name="filter"></param>
        public static void AddModFilter(string id, string filter)
        {
            bool found = false;
            for (int i = 0; i < modTags.Count(); i++)
            {
                if (modTags[i].ModID == id)
                {
                    if(!modTags[i].FilterExist(filter))
                    {
                        modTags[i].AddFilter(filter);
                        found = true;
                        break;
                    }
                }
            }
            if(!found)
            {
                modTags.Add(new ModTag(id, filter));
            }
        }

        /// <summary>
        /// Removes a mod filter
        /// </summary>
        /// <param name="id"></param>
        /// <param name="filter"></param>
        public static void RemoveModFilter(string id, string filter)
        {
            for (int i = 0; i < modTags.Count(); i++)
            {
                if (modTags[i].ModID == id)
                {
                    if (modTags[i].FilterExist(filter))
                    {
                        modTags[i].RemoveFilter(filter);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Adds a mod tag to the list
        /// If the id and tag dosent exist already
        /// </summary>
        /// <param name="id"></param>
        /// <param name="tag"></param>
        public static void AddModTag(string id, string tag)
        {
            bool found = false;
            for (int i = 0; i < modTags.Count(); i++)
            {
                if (modTags[i].ModID == id)
                {
                    if (!modTags[i].TagExist(tag))
                    {
                        modTags[i].AddTag(tag);
                        found = true;
                        break;
                    }
                }
            }
            if (!found)
            {
                modTags.Add(new ModTag(id, null, tag));
            }
        }

        /// <summary>
        /// Generate runtime mod tags for this mod
        /// Tags will be determined based on avalible mod information
        /// </summary>
        /// <param name="mod"></param>
        public static void AddModFiltersRuntime(Mod? mod)
        {
            if(mod == null) return;
            if(mod.id.ToLower() == "fs2")
            {
                AddModFilter(mod.id, Filters.Retail_FS2.ToString());
                return;
            }
            if(mod.parent == null)
            {
                AddModFilter(mod.id,Filters.Total_Conversion.ToString());
            }
            else
            {
                if (mod.parent.ToLower() == "fs2")
                {
                    if(mod.GetDependency("fsport", true) != null)
                    {
                        AddModFilter(mod.id, Filters.FS1_MOD.ToString()); // only going to work for installed mods
                    }
                    else
                    {
                        AddModFilter(mod.id, Filters.FS2_MOD.ToString());
                    }
                }
                else
                {
                    AddModFilter(mod.id, Filters.TC_MOD.ToString());
                }
            }
        }

    }
}
