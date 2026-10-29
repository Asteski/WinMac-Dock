namespace WinMac.Dock;

internal static class ShellKeepOpen
{
    private sealed record Token(string Value, int Start, int End);
    private static IEnumerable<Token> Tokens(string text)
    {
        for (int i = 0; i < text.Length;)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }
            int start = i; bool quoted = false;
            do
            {
                if (text[i] == '"' && (i == 0 || text[i - 1] != '\\')) quoted = !quoted;
                i++;
            } while (i < text.Length && (quoted || !char.IsWhiteSpace(text[i])));
            yield return new(text[start..i].Trim('"'), start, i);
        }
    }
    internal static string Arguments(string executable, string arguments)
    {
        string name = Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
        var tokens = Tokens(arguments).ToArray();
        if (name is "powershell" or "pwsh")
        {
            // Only inspect shell options, never text inside the command/script.
            foreach (var token in tokens)
            {
                if (token.Value.Equals("-NoExit", StringComparison.OrdinalIgnoreCase) || token.Value.Equals("-noe", StringComparison.OrdinalIgnoreCase)) return arguments;
                if (token.Value.Equals("-c", StringComparison.OrdinalIgnoreCase) || token.Value.Equals("-f", StringComparison.OrdinalIgnoreCase)
                    || token.Value.StartsWith("-command", StringComparison.OrdinalIgnoreCase) || token.Value.Equals("-file", StringComparison.OrdinalIgnoreCase)
                    || token.Value.StartsWith("-encodedcommand", StringComparison.OrdinalIgnoreCase)) break;
            }
            return "-NoExit" + (arguments.Length > 0 ? " " + arguments : "");
        }
        if (name == "cmd")
        {
            foreach (var token in tokens)
            {
                if (token.Value.StartsWith("/k", StringComparison.OrdinalIgnoreCase)) return arguments;
                if (token.Value.StartsWith("/c", StringComparison.OrdinalIgnoreCase))
                    return arguments[..token.Start] + "/k" + arguments[(token.Start + 2)..];
                if (!token.Value.StartsWith('/')) break;
            }
            return "/k" + (arguments.Length > 0 ? " " + arguments : "");
        }
        if (name is not ("wt" or "windowsterminal" or "wtd")) return arguments;
        var separator = tokens.FirstOrDefault(token => token.Value == ";" && arguments[token.Start] == ';');
        if (separator != null)
            return Arguments(executable, arguments[..separator.Start].TrimEnd()) + " ; " + Arguments(executable, arguments[separator.End..].TrimStart());
        // Terminal hosts a shell: alter the child command, preserving Terminal
        // profile, window, directory, title and appearance options.
        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            string value = token.Value.ToLowerInvariant();
            if (value is "new-tab" or "nt" or "split-pane" or "sp") continue;
            if (value is "focus-tab" or "ft" or "move-focus" or "mf" or "move-pane" or "mp") return arguments;
            if (value is "-p" or "--profile" or "-d" or "--startingdirectory" or "--title" or "--tabcolor" or "--colorscheme"
                or "-w" or "--window" or "--pos" or "--size" or "--sizeinchars" or "-s" or "--target") { i++; continue; }
            if (value.StartsWith('-')) continue;
            if (value == ";") continue;
            string child = Path.GetFileNameWithoutExtension(token.Value).ToLowerInvariant();
            string rest = arguments[token.End..].TrimStart();
            if (child is "powershell" or "pwsh" or "cmd")
                return arguments[..token.End] + " " + Arguments(child, rest);
            // A direct Terminal command (e.g. wt ping ...) needs a persistent
            // shell around it. Explicit PowerShell/CMD children retain theirs.
            return arguments[..token.Start] + "cmd.exe /k " + arguments[token.Start..];
        }
        return arguments; // No explicit command: keep the profile's interactive shell.
    }
}
