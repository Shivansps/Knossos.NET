using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Knossos.NET.Classes
{
    /// <summary>
    /// Helper class to handle semantic versions used by FSO and Knossos.
    /// Just the basics needed to do comparisons without having to include
    /// a 3rd party library for this.
    /// Implements the IComparable interface to use with LINQ .MaxBy();
    /// This is a basic implementation of SemanticVersion intended to be tailored for the needs of FSO mods
    /// </summary>
    public class SemanticVersion : IComparable
    {
        int major;
        int minor;
        int revision;
        string? prerelease;

        /// <summary>
        /// Creates a new Semantic Version class from a version string
        /// If parsing fails it sets, major, minor and revision to "0"
        /// </summary>
        /// <param name="version"></param>
        public SemanticVersion(string version)
        {
            try
            {
                if (version == null)
                {
                    throw new ArgumentNullException("version can not be null");
                }
                if (version.Trim().Length == 0)
                {
                    major = 0;
                    minor = 0;
                    revision = 0;
                    prerelease = null;
                }
                else
                {
                    if(version.Contains("+"))
                        version = version.Replace("+", "-");

                    string[] parts = version.Replace(" ", "").Split('-')[0].Split('.');

                    if (parts.Length == 3)
                    {
                        try
                        {
                            major = int.Parse(parts[0]);
                            minor = int.Parse(parts[1]);
                            revision = int.Parse(parts[2]);
                        }
                        catch
                        {
                            throw new Exception("version has a invalid semantic format: " + version);
                        }
                    }
                    else
                    {
                        throw new Exception("version has a invalid semantic format: " + version);
                    }

                    if (version.Contains("-"))
                    {
                        // 0.0.0-RC1 -> RC1
                        // 0.0.0-RC1-Alpha -> RC1.Alpha
                        prerelease = string.Join(".",version.Replace(" ", "").Split('-').Skip(1)).Trim().ToLower();
                    }
                    else
                    {
                        prerelease = null;
                    }
                }
            }catch(Exception e) 
            {
                major=0;
                minor = 0;
                revision = 0;
                prerelease = null;
                Log.Add(Log.LogSeverity.Error, "SemanticVersion(string version)",e);
            }
        }

        /// <summary>
        /// Compare two semantic versions.
        /// </summary>
        /// <param name="versionA"></param>
        /// <param name="versionB"></param>
        /// <returns>Retuns >=1 if A is superior, 0 if equal or <=-1 if A is inferior.</returns>    
        public static int Compare(string versionA, string versionB)
        {
            return Compare(new SemanticVersion(versionA), new SemanticVersion(versionB));
        }

        /// <summary>
        /// Compare two semantic versions.
        /// </summary>
        /// <param name="versionA"></param>
        /// <param name="versionB"></param>
        /// <returns>Retuns >=1 if A is superior, 0 if equal or <=-1 if A is inferior.</returns>        
        public static int Compare(SemanticVersion versionA, SemanticVersion versionB)
        {
            if(versionA.major != versionB.major)
            {
                return versionA.major - versionB.major;
            }
            else
            {
                if(versionA.minor != versionB.minor)
                {
                    return versionA.minor - versionB.minor;
                }
                else
                {
                    if(versionA.revision != versionB.revision)
                    {
                        return versionA.revision - versionB.revision;
                    }
                    else
                    {
                        if(versionA.prerelease == null && versionB.prerelease != null)
                        {
                            return 1;
                        }

                        if(versionA.prerelease != null && versionB.prerelease == null)
                        {
                            return -1;
                        }

                        /*
                            Pre-Release Comparison
                            -Identifiers consisting of only digits are compared numerically.
                            -Identifiers with letters or hyphens are compared lexically in ASCII sort order.
                            -Numeric identifiers always have lower precedence than non-numeric identifiers.
                            -A larger set of pre-release fields has a higher precedence than a smaller set, if all of the preceding identifiers are equal.
                            -Example: 1.0.0-alpha < 1.0.0-alpha.1 < 1.0.0-alpha.beta < 1.0.0-beta < 1.0.0-beta.2 < 1.0.0-beta.11 < 1.0.0-rc.1 < 1.0.0
                        */

                        if (versionA.prerelease != null && versionB.prerelease != null)
                        {
                            //add "." sepators to numbers if they dont have a preceding number or "."
                            var preReleaseA = PreReleaseSeparateNumbers(versionA.prerelease);
                            var preReleaseB = PreReleaseSeparateNumbers(versionB.prerelease);

                            var arrayA = preReleaseA.Split(".");
                            var arrayB = preReleaseB.Split(".");

                            //We use arrayA as a base
                            for(int i = 0; i < arrayA.Length; i++)
                            {
                                //compare segment to segment
                                if(i < arrayB.Length)
                                {
                                    //This should be enough to resolve equal on all cases
                                    var cmpRes = string.CompareOrdinal(arrayA[i], arrayB[i]);
                                    if (cmpRes != 0)
                                    {
                                        //all digits? 
                                        if (arrayA[i].All(char.IsDigit) && arrayB[i].All(char.IsDigit))
                                        {
                                            return int.Parse(arrayA[i]) - int.Parse(arrayB[i]);
                                        }
                                        else
                                        {
                                            //Same length?
                                            if (arrayA[i].Length == arrayB[i].Length)
                                            {
                                                return cmpRes;
                                            }
                                            else
                                            {
                                                //Make a substring of the larger segment matching the shorter and compare
                                                //if it matches compare array length, otherwise return the comparison
                                                if (arrayA[i].Length > arrayB[i].Length)
                                                {
                                                    cmpRes = string.CompareOrdinal(arrayA[i].Substring(0, arrayB[i].Length), arrayB[i]);
                                                    if (cmpRes == 0)
                                                    {
                                                        return arrayA[i].Length - arrayB[i].Length;
                                                    }
                                                    else
                                                    {
                                                        return cmpRes;
                                                    }
                                                }
                                                else
                                                {
                                                    cmpRes = string.CompareOrdinal(arrayB[i].Substring(0, arrayA[i].Length), arrayA[i]);
                                                    if (cmpRes == 0)
                                                    {
                                                        return arrayA[i].Length - arrayB[i].Length;
                                                    }
                                                    else
                                                    {
                                                        //invert the result because we are comparing b to a
                                                        return cmpRes * -1;
                                                    }
                                                }
                                                
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    //arrayB is shorter, so end the comparison
                                    break;
                                }
                            }

                            //If we are still here that means all compared segments were equal. So return the length difference
                            return arrayA.Length - arrayB.Length;
                        }
                        else
                        {
                            return string.Compare(versionA.prerelease, versionB.prerelease);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Inserts separators to numbers within a Pre-Release string so they can be properly compared
        /// Example:
        /// -RC10 -> -RC.10
        /// -Beta5Version4 -> -Beta.5.Version.4
        /// </summary>
        /// <param name="preReleaseString"></param>
        private static string PreReleaseSeparateNumbers(string preReleaseString)
        {
            var pos = 1;
            while (pos < preReleaseString.Length)
            {
                if (char.IsDigit(preReleaseString[pos]) && preReleaseString[pos - 1] != '.' && !char.IsDigit(preReleaseString[pos - 1]))
                {
                    preReleaseString = preReleaseString.Insert(pos, ".");
                    pos += 2;
                }
                else
                {
                    if (preReleaseString[pos] != '.' && !char.IsDigit(preReleaseString[pos]) && char.IsDigit(preReleaseString[pos - 1]))
                    {
                        preReleaseString = preReleaseString.Insert(pos, ".");
                        pos += 2;
                    }
                    else
                    {
                        pos++; 
                    }
                }
            }
            return preReleaseString;
        }

        /// <summary>
        /// Compares a semantic version string to the version string in the mod dependency to see if it sastifies the requirement.
        /// Version : null -> Any, Version: "4.6.1" -> Only that version, Version: "~4.6.1" -> >=4.6.1 < 4.7.0, Version: ">=4.6.1" -> equal or newer, Version: "<=4.6.1" -> equal or older, Version: ">4.6.1" -> newer, Version: "<4.6.1" -> older, Version: "[4.6.1,5.0.0)" -> NuGet interval (any combination of [/]/(/) brackets), Version: ">=4.6.1 <5.0.0" -> space-separated AND
        /// </summary>
        /// <param name="dependencyVersion"></param>
        /// <param name="version"></param>
        /// <returns>true or false</returns>
        public static bool SastifiesDependency(string? dependencyVersion, string? version)
        {
            if (version == null)
                return false;

            return SastifiesDependency(dependencyVersion, new SemanticVersion(version));
        }

        /*

        */
        /// <summary>
        /// Compares a semantic version to the version string in the mod dependency to see if it sastifies the requirement.
        /// Version : null -> Any, Version: "4.6.1" -> Only that version, Version: "~4.6.1" -> >=4.6.1 < 4.7.0, Version: ">=4.6.1" -> equal or newer, Version: "<=4.6.1" -> equal or older, Version: ">4.6.1" -> newer, Version: "<4.6.1" -> older, Version: "[4.6.1,5.0.0)" -> NuGet interval (any combination of [/]/(/) brackets), Version: ">=4.6.1 <5.0.0" -> space-separated AND
        /// </summary>
        /// <param name="dependencyVersion"></param>
        /// <param name="version"></param>
        /// <returns>returns true or false</returns>
        public static bool SastifiesDependency(string? dependencyVersion, SemanticVersion version)
        {
            try
            {
                /* If dependencyversion is null it means any version will do and the mod will use the newest installed version available */
                if (dependencyVersion == null)
                {
                    return true;
                }

                var parts = NormalizeToSingleOps(dependencyVersion.Trim());
                return parts.Count > 0 && parts.All(p => SatisfiesSingleOp(p, version));
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "SemanticVersion.SastifiesDependency()", ex);
                return false;
            }
        }

        /// <summary>
        /// True if the constraint cannot be represented as a single-operator + version (e.g. NuGet interval
        /// "[1.0,2.0)" or space-separated AND ">=1.0 <2.0"). Used by callers that strip operators or by UI
        /// pickers that can only display a single operator.
        /// </summary>
        public static bool IsComplexConstraint(string? constraint)
        {
            if (string.IsNullOrWhiteSpace(constraint))
                return false;
            var s = constraint.Trim();
            if (s.Length == 0)
                return false;
            if (s[0] == '[' || s[0] == '(')
                return true;
            if (s.Any(char.IsWhiteSpace))
                return true;
            return false;
        }

        /// <summary>
        /// Returns the bare lower-bound version string from any supported constraint form, or null when the
        /// constraint has no lower bound (e.g. "&lt;2.0.0" or "(,2.0]"). Null-safe and exception-safe.
        /// </summary>
        public static string? GetLowerBound(string? constraint)
        {
            if (string.IsNullOrWhiteSpace(constraint))
                return null;
            try
            {
                var parts = NormalizeToSingleOps(constraint.Trim());
                foreach (var p in parts)
                {
                    if (p.StartsWith(">="))
                        return p.Substring(2).Trim();
                    if (p.StartsWith("~"))
                        return p.Substring(1).Trim();
                    if (p.StartsWith(">"))
                        return p.Substring(1).Trim();
                    if (p.StartsWith("<"))
                        continue;
                    return p.Trim();
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Splits a dependency version string into a list of single-operator constraints to be ANDed together.
        /// Recognizes NuGet interval notation, npm hyphen ranges, and npm space-separated ranges; otherwise
        /// returns the input unchanged as a one-element list (the existing single-operator path).
        /// </summary>
        private static List<string> NormalizeToSingleOps(string input)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(input))
                return result;

            //NuGet interval notation: [X,Y], (X,Y), [X,Y), (X,Y], [X,), (X,], (,Y], (,Y), [X]
            if (input[0] == '[' || input[0] == '(')
            {
                var openBracket = input[0];
                var closeBracket = input[input.Length - 1];
                if (closeBracket != ']' && closeBracket != ')')
                    throw new Exception("Invalid NuGet interval, missing closing bracket: " + input);

                var inner = input.Substring(1, input.Length - 2);
                var commaIdx = inner.IndexOf(',');

                if (commaIdx < 0)
                {
                    //No comma -> [X] form (exact match). Both brackets must be square.
                    if (openBracket != '[' || closeBracket != ']')
                        throw new Exception("NuGet exact form requires square brackets: " + input);
                    var v = inner.Trim();
                    if (v.Length == 0)
                        throw new Exception("NuGet exact form requires a version: " + input);
                    result.Add(v);
                    return result;
                }

                var lowStr = inner.Substring(0, commaIdx).Trim();
                var highStr = inner.Substring(commaIdx + 1).Trim();

                if (lowStr.Length > 0)
                    result.Add((openBracket == '[' ? ">=" : ">") + lowStr);
                if (highStr.Length > 0)
                    result.Add((closeBracket == ']' ? "<=" : "<") + highStr);

                return result;
            }

            //npm space-separated AND. Also catches single-op-with-internal-space like ">= 4.6.1".
            if (input.Any(char.IsWhiteSpace))
            {
                var matches = Regex.Matches(input, @"(>=|<=|>|<|~)?\s*\d+(?:\.\d+){0,2}(?:-\S+)?");
                if (matches.Count == 0)
                    throw new Exception("Could not parse range: " + input);
                foreach (Match m in matches)
                    result.Add(Regex.Replace(m.Value, @"\s+", ""));
                return result;
            }

            //Single-operator form, no parsing needed.
            result.Add(input);
            return result;
        }

        /// <summary>
        /// Evaluates a single-operator constraint string (e.g. ">=4.6.1", "~4.6.1", "4.6.1") against a candidate
        /// version. This is the original per-operator logic, extracted unchanged from SastifiesDependency.
        /// </summary>
        private static bool SatisfiesSingleOp(string singleOp, SemanticVersion version)
        {
            if (singleOp.Contains("~"))
            {
                var versionDep = new SemanticVersion(singleOp.Replace("~", ""));
                /* major and minor has to math, revision needs to be equal or superior*/
                if (version.major == versionDep.major && version.minor == versionDep.minor && version.revision >= versionDep.revision)
                {
                    if (Compare(version, versionDep) >= 0)
                    {
                        return true;
                    }
                }
                return false;
            }

            if (singleOp.Contains(">="))
            {
                var versionDep = new SemanticVersion(singleOp.Replace(">=", ""));
                /* major minor and revision needs to be equal or superior*/
                if (version.major >= versionDep.major || version.major == versionDep.major && version.minor >= versionDep.minor || version.major == versionDep.major && version.minor == versionDep.minor && version.revision >= versionDep.revision)
                {
                    if (Compare(version, versionDep) >= 0)
                    {
                        return true;
                    }
                }

                return false;
            }

            if (singleOp.Contains("<="))
            {
                var versionDep = new SemanticVersion(singleOp.Replace("<=", ""));
                /* major minor and revision needs to be equal or inferior*/
                if (version.major <= versionDep.major || version.major == versionDep.major && version.minor <= versionDep.minor || version.major == versionDep.major && version.minor == versionDep.minor && version.revision <= versionDep.revision)
                {
                    if (Compare(version, versionDep) <= 0)
                    {
                        return true;
                    }
                }

                return false;
            }

            if (singleOp.Contains(">"))
            {
                var versionDep = new SemanticVersion(singleOp.Replace(">", ""));
                return Compare(version, versionDep) > 0;
            }

            if (singleOp.Contains("<"))
            {
                var versionDep = new SemanticVersion(singleOp.Replace("<", ""));
                return Compare(version, versionDep) < 0;
            }

            return Compare(version, new SemanticVersion(singleOp)) == 0;
        }

        public static bool operator >(SemanticVersion a, SemanticVersion b)
        {
            return Compare(a, b) > 0;
        }

        public static bool operator >=(SemanticVersion a, SemanticVersion b)
        {
            return Compare(a, b) >= 0;
        }

        public static bool operator <(SemanticVersion a, SemanticVersion b)
        {
            return Compare(a, b) < 0;
        }

        public static bool operator <=(SemanticVersion a, SemanticVersion b)
        {
            return Compare(a, b) <= 0;
        }

        /// <summary>
        /// major . minor . revision - prerelease (if it has one)
        /// </summary>
        public override string ToString()
        {
            if(prerelease != null)
            {
                return major + "." + minor + "." + revision + "-" + prerelease;
            }
            else
            {
                return major + "." + minor + "." + revision;
            }
        }

        public int CompareTo(object? other)
        {
            if (other == null)
                return 1;

            return Compare(this, (SemanticVersion)other);
        }
    }
}
