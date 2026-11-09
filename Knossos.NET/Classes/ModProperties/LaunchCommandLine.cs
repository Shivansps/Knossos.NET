using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Knossos.NET.Classes
{
    /// <summary>Quotes launcher arguments and edits exact FSO switches without losing unrelated arguments.</summary>
    public static class LaunchCommandLine
    {
        public static string Quote(string value, bool windows, bool always = false)
        {
            if (!always && value.Length > 0 && value.All(c => char.IsLetterOrDigit(c) || "_./\\:-+=".Contains(c))) return value;
            if (!windows) return "'" + value.Replace("'", "'\"'\"'") + "'";
            var result = new StringBuilder("\"");
            var slashes = 0;
            foreach (var c in value)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                result.Append(c);
                slashes = 0;
            }
            result.Append('\\', slashes * 2).Append('"');
            return result.ToString();
        }
        public static string QuickArguments(string id, string? version, string executable, string extra, bool windows)
            => "-playmod " + Quote(id, windows) + (string.IsNullOrWhiteSpace(version) ? "" : " -version " + Quote(version, windows)) +
                " -exec " + Quote(executable, windows) + (string.IsNullOrWhiteSpace(extra) ? "" : " -cmdline " + Quote(extra, windows, true));

        private record Token(int Start, int End, string Value, bool Switch);
        private static List<Token> Tokens(string command, bool windows = false)
        {
            var tokens = new List<Token>();
            for (var i = 0; i < command.Length;)
            {
                if (char.IsWhiteSpace(command[i])) { i++; continue; }
                var start = i;
                var value = new StringBuilder();
                char quote = '\0';
                while (i < command.Length && (quote != '\0' || !char.IsWhiteSpace(command[i])))
                {
                    var c = command[i++];
                    if (c == '\\')
                    {
                        var count = 1;
                        while (i < command.Length && command[i] == '\\') { count++; i++; }
                        if (i < command.Length && command[i] == '"')
                        {
                            value.Append('\\', count / 2);
                            if (count % 2 != 0) { value.Append(command[i++]); continue; }
                            c = command[i++];
                        }
                        else { value.Append('\\', count); continue; }
                    }
                    if (c == '"' || !windows && c == '\'')
                    {
                        if (quote == c) quote = '\0';
                        else if (quote == '\0') quote = c;
                        else value.Append(c);
                    }
                    else value.Append(c);
                }
                tokens.Add(new Token(start, i, value.ToString(), command[start] == '-' && value.Length > 1 && char.IsLetter(value[1])));
            }
            return tokens;
        }
        public static (string Executable, string Arguments) SplitCommand(string command)
        {
            var first = Tokens(command, true).FirstOrDefault() ?? throw new ArgumentException("Enter a launcher command first.");
            return (first.Value, command[first.End..].TrimStart());
        }
        public static bool HasFlag(string command, string flag) => Tokens(command).Any(t => t.Switch && t.Value.Equals(flag, StringComparison.OrdinalIgnoreCase));
        public static string SetFlag(string command, string flag, bool enabled)
        {
            if (enabled) return HasFlag(command, flag) ? command : (command.TrimEnd() + " " + flag).TrimStart();
            var tokens = Tokens(command);
            for (var index = tokens.Count - 1; index >= 0; index--)
            {
                var token = tokens[index];
                if (!token.Switch || !token.Value.Equals(flag, StringComparison.OrdinalIgnoreCase)) continue;
                var next = tokens.Skip(index + 1).FirstOrDefault(t => t.Switch);
                var end = next?.Start ?? command.Length;
                command = command[..token.Start] + command[end..];
            }
            return command.Trim();
        }
    }
}
