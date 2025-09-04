using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using TextCopy;

#nullable enable

public static class Program
{
    private static readonly Dictionary<string, NativeExtension> s_loadedExtensions = new();
    private static readonly Dictionary<string, Action<string>> s_commandHandlers = new(StringComparer.OrdinalIgnoreCase);
    private static NotifyIcon? s_notifyIcon;

    [STAThread]
    public static void Main(string[] args)
    {
        // Initialize the notification icon
        s_notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Visible = false
        };

        Console.Title = Environment.Is64BitProcess ? "Arma 3 Extension Tester (x64)" : "Arma 3 Extension Tester (x86)";
        Console.WriteLine($"// {Console.Title}");
        Console.WriteLine("// Type 'exit' to close, or 'help' for commands.");

        SetupCommands();

        if (args.Length > 0 && File.Exists(args[0]))
        {
            ScriptRunner.Execute(args[0]);
        }

        while (true)
        {
            Console.Write("> ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;

            if (input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            ProcessCommand(input);
        }

        foreach (var ext in s_loadedExtensions.Values)
        {
            ext.Dispose();
        }
        s_notifyIcon?.Dispose(); // Clean up the notification icon
    }

    private static void ShowNotification(string title, string message)
    {
        if (s_notifyIcon == null) return;
        s_notifyIcon.Visible = true;
        s_notifyIcon.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);
        // A timer to hide the icon after the tip is gone would be ideal in a real app,
        // but for this console tool, we can leave it.
    }

    private static void SetupCommands()
    {
        s_commandHandlers["help"] = _ => PrintHelp();
        s_commandHandlers["execvm"] = arg => {
            var match = Regex.Match(arg, @"^""([^""]*)""");
            if (match.Success) ScriptRunner.Execute(match.Groups[1].Value);
            else Console.WriteLine("Error: Invalid execVM syntax. Use: execVM \"<script_path>\"");
        };
        s_commandHandlers["freeextension"] = arg => {
            var match = Regex.Match(arg, @"^""([^""]+)""");
            if (match.Success) FreeExtension(match.Groups[1].Value);
            else Console.WriteLine("Error: Invalid freeExtension syntax. Use: freeExtension \"<name>\"");
        };
        s_commandHandlers["sleep"] = arg => {
            if (float.TryParse(arg, out var duration)) ScriptRunner.Sleep(duration);
            else Console.WriteLine("Error: Invalid sleep syntax. Use: sleep <seconds>");
        };
        s_commandHandlers["callextension"] = arg => {
            var match = Regex.Match(arg, @"^""(?<name>[^""]+)""");
            if (match.Success) CallExtensionVersion(match.Groups["name"].Value);
            else Console.WriteLine("Error: Invalid callExtension syntax. Use: callExtension \"<name>\"");
        };

        // Engine Commands using NotifyIcon
        s_commandHandlers["hint"] = arg => {
            var match = Regex.Match(arg, @"^""(.*)""");
            if (match.Success) ShowNotification("Arma 3 Hint", match.Groups[1].Value);
        };
        s_commandHandlers["systemchat"] = arg => {
            var match = Regex.Match(arg, @"^""(.*)""");
            if (match.Success) ShowNotification("System Chat", match.Groups[1].Value);
        };
        s_commandHandlers["diag_log"] = arg => {
            var match = Regex.Match(arg, @"^""(.*)""");
            if (match.Success)
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine($"[DIAG LOG]: {match.Groups[1].Value}");
                Console.ResetColor();
            }
        };
        s_commandHandlers["copytoclipboard"] = arg => {
            var match = Regex.Match(arg, @"^""(.*)""");
            if (match.Success) System.Windows.Forms.Clipboard.SetText(match.Groups[1].Value);
        };
        s_commandHandlers["format"] = arg =>
        {
            try
            {
                var parsed = SqfArrayParser.Parse(arg);
                if (parsed.Count < 1 || !(parsed[0] is string formatStr))
                {
                    Console.WriteLine("Error: format requires a format string as the first argument.");
                    return;
                }

                var formatArgs = parsed.Skip(1).Select(SqfArrayParser.ToSqfString).ToArray();
                var result = string.Format(formatStr.Replace("%", "{").Replace("}", "}}"), formatArgs);
                Console.WriteLine("\"" + result.Replace("\"", "\"\"") + "\"");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during format: {ex.Message}");
            }
        };
    }

    public static void ProcessCommand(string command)
    {
        command = command.Trim();

        // ** THE FIX IS HERE **
        // Strip trailing semicolon if it exists, to support SQF-like syntax
        if (command.EndsWith(";"))
        {
            command = command.Substring(0, command.Length - 1).TrimEnd();
        }

        if (string.IsNullOrEmpty(command) || command.StartsWith("#")) return;

        if (command.StartsWith("//"))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(command);
            Console.ResetColor();
            return;
        }

        try
        {
            var callExtMatch = Regex.Match(command, @"^""(?<name>[^""]+)""\s+callExtension\s+(?<args>.+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (callExtMatch.Success)
            {
                HandleCallExtension(callExtMatch.Groups["name"].Value, callExtMatch.Groups["args"].Value.Trim());
                return;
            }

            var parts = command.Split(new[] { ' ' }, 2);
            var cmd = parts[0];
            var args = parts.Length > 1 ? parts[1].Trim() : string.Empty;

            if (s_commandHandlers.TryGetValue(cmd, out var handler))
            {
                handler(args);
            }
            else
            {
                Console.WriteLine($"Error: Unknown command or syntax: '{command}'");
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"An error occurred: {ex.Message}");
            Console.ResetColor();
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("\n--- Arma 3 Extension Tester Help ---");
        Console.WriteLine("1. Call Extension (Simple): \"<name>\" callExtension \"<function>\"");
        Console.WriteLine("2. Call Extension (Args):   \"<name>\" callExtension [\"<fnc>\", [<arg1>, <arg2>]]");
        Console.WriteLine("3. Get Version (Shortcut):  callExtension \"<name>\"");
        Console.WriteLine("4. Unload Extension:        freeExtension \"<name>\"");
        Console.WriteLine("5. Execute Script:          execVM \"<path_to_script.txt>\"");
        Console.WriteLine("6. Pause Script:            sleep <seconds>");
        Console.WriteLine("\n--- Engine Commands ---");
        Console.WriteLine("   hint \"<message>\"              (Shows a Windows notification)");
        Console.WriteLine("   systemChat \"<message>\"        (Shows a Windows notification)");
        Console.WriteLine("   diag_log \"<message>\"          (Prints a log message to console)");
        Console.WriteLine("   copyToClipboard \"<text>\"    (Copies text to clipboard)");
        Console.WriteLine("   format [\"formatStr\", <arg1>] (Formats a string, e.g., format[\"Hello %1\", \"World\"])");
        Console.WriteLine("\n7. Exit Application:        exit\n");
    }

    private static NativeExtension? GetOrLoadExtension(string name)
    {
        if (s_loadedExtensions.TryGetValue(name.ToLower(), out var ext))
        {
            return ext;
        }
        var archSuffix = Environment.Is64BitProcess ? "_x64" : "_x86";
        string dllPath = File.Exists($"{name}{archSuffix}.dll")
            ? $"{name}{archSuffix}.dll"
            : File.Exists($"{name}.dll") ? $"{name}.dll" : "";

        if (string.IsNullOrEmpty(dllPath))
        {
            Console.WriteLine($"Error: Could not find '{name}{archSuffix}.dll' or '{name}.dll'.");
            return null;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"// Loading extension: {Path.GetFullPath(dllPath)}");
        Console.ResetColor();
        try
        {
            var newExt = new NativeExtension(dllPath);
            s_loadedExtensions[name.ToLower()] = newExt;
            return newExt;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Failed to load extension '{dllPath}'. Reason: {ex.Message}");
            Console.ResetColor();
            return null;
        }
    }

    private static void CallExtensionVersion(string name)
    {
        var ext = GetOrLoadExtension(name);
        if (ext == null) return;
        var sw = Stopwatch.StartNew();
        var (output, wasCalled) = ext.InvokeVersion();
        sw.Stop();
        if (wasCalled) PrintOutput(output, sw.Elapsed.TotalMilliseconds);
        else Console.WriteLine($"Error: Extension '{name}' does not export RVExtensionVersion.");
    }

    private static void HandleCallExtension(string name, string argsString)
    {
        var ext = GetOrLoadExtension(name);
        if (ext == null) return;

        var simpleMatch = Regex.Match(argsString, @"^""(.*)""$");
        if (simpleMatch.Success && !argsString.TrimStart().StartsWith("["))
        {
            var function = simpleMatch.Groups[1].Value;
            var sw = Stopwatch.StartNew();
            var (output, wasCalled) = ext.Invoke(function);
            sw.Stop();
            if (!wasCalled) Console.WriteLine($"Error: Extension '{name}' does not export RVExtension.");
            else PrintOutput(output, sw.Elapsed.TotalMilliseconds);
            return;
        }

        try
        {
            var parsed = SqfArrayParser.Parse(argsString);
            if (parsed.Count == 0 || !(parsed[0] is string function))
                throw new FormatException("Expected format: [\"<function>\", ...]");

            var args = new string[0];
            if (parsed.Count > 1)
            {
                if (parsed[1] is List<object> argList)
                    args = argList.Select(SqfArrayParser.ToSqfString).ToArray();
                else
                    args = parsed.Skip(1).Select(SqfArrayParser.ToSqfString).ToArray();
            }

            var sw = Stopwatch.StartNew();
            var (output, wasCalled) = ext.Invoke(function, args);
            sw.Stop();
            if (!wasCalled) Console.WriteLine($"Error: Extension '{name}' does not export RVExtensionArgs.");
            else PrintOutput(output, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing arguments: {ex.Message}");
        }
    }

    private static void FreeExtension(string name)
    {
        if (s_loadedExtensions.TryGetValue(name.ToLower(), out var ext))
        {
            ext.Dispose();
            s_loadedExtensions.Remove(name.ToLower());
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"// Extension '{name}' unloaded.");
            Console.ResetColor();
        }
        else Console.WriteLine($"Error: Extension '{name}' is not loaded.");
    }

    private static void PrintOutput(string output, double milliseconds)
    {
        var formattedOutput = "\"" + output.Replace("\"", "\"\"") + "\"";
        Console.WriteLine(formattedOutput);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"({milliseconds:F4} ms)");
        Console.ResetColor();
    }
}
public class NativeExtension : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void RVExtensionDelegate(StringBuilder output, int outputSize, [MarshalAs(UnmanagedType.LPStr)] string function);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void RVExtensionArgsDelegate(StringBuilder output, int outputSize, [MarshalAs(UnmanagedType.LPStr)] string function, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] args, int argCount);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void RVExtensionVersionDelegate(StringBuilder output, int outputSize);

    private readonly IntPtr _moduleHandle;
    private readonly RVExtensionDelegate? _rvExtension;
    private readonly RVExtensionArgsDelegate? _rvExtensionArgs;
    private readonly RVExtensionVersionDelegate? _rvExtensionVersion;
    private const int OUTPUT_BUFFER_SIZE = 10240;

    public NativeExtension(string dllPath)
    {
        _moduleHandle = NativeMethods.LoadLibrary(dllPath);
        if (_moduleHandle == IntPtr.Zero) throw new DllNotFoundException($"Could not load library: {dllPath}. Error code: {Marshal.GetLastWin32Error()}");
        _rvExtension = GetDelegate<RVExtensionDelegate>("RVExtension");
        _rvExtensionArgs = GetDelegate<RVExtensionArgsDelegate>("RVExtensionArgs");
        _rvExtensionVersion = GetDelegate<RVExtensionVersionDelegate>("RVExtensionVersion");
    }

    private T? GetDelegate<T>(string procName) where T : Delegate
    {
        var procAddress = NativeMethods.GetProcAddress(_moduleHandle, procName);
        return procAddress == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(procAddress);
    }
    public (string result, bool wasCalled) Invoke(string function)
    {
        if (_rvExtension == null) return ("", false);
        var buffer = new StringBuilder(OUTPUT_BUFFER_SIZE);
        _rvExtension(buffer, buffer.Capacity, function);
        return (buffer.ToString(), true);
    }
    public (string result, bool wasCalled) Invoke(string function, string[] args)
    {
        if (_rvExtensionArgs == null) return ("", false);
        var buffer = new StringBuilder(OUTPUT_BUFFER_SIZE);
        _rvExtensionArgs(buffer, buffer.Capacity, function, args, args.Length);
        return (buffer.ToString(), true);
    }
    public (string result, bool wasCalled) InvokeVersion()
    {
        if (_rvExtensionVersion == null) return ("", false);
        var buffer = new StringBuilder(OUTPUT_BUFFER_SIZE);
        _rvExtensionVersion(buffer, buffer.Capacity);
        return (buffer.ToString(), true);
    }
    public void Dispose() { if (_moduleHandle != IntPtr.Zero) NativeMethods.FreeLibrary(_moduleHandle); }
}
public static class ScriptRunner
{
    private static int s_recursionDepth = 0;
    private const int MAX_DEPTH = 10;
    public static void Execute(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"Error: Script file not found: '{filePath}'");
            return;
        }
        if (s_recursionDepth >= MAX_DEPTH)
        {
            Console.WriteLine("Error: execVM recursion limit reached. Aborting script.");
            return;
        }
        s_recursionDepth++;
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"--- Executing script: {Path.GetFileName(filePath)} ---");
        Console.ResetColor();
        var lines = File.ReadAllLines(filePath);
        foreach (var line in lines) Program.ProcessCommand(line);
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"--- Finished script: {Path.GetFileName(filePath)} ---");
        Console.ResetColor();
        s_recursionDepth--;
    }
    public static void Sleep(float seconds)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"// Sleeping for {seconds} seconds...");
        int totalMilliseconds = (int)(seconds * 1000);
        int interval = 200;
        int progress = 0;
        while (progress < totalMilliseconds)
        {
            int wait = Math.Min(interval, totalMilliseconds - progress);
            Thread.Sleep(wait);
            progress += wait;
            float percentage = (float)progress / totalMilliseconds * 100;
            Console.Write($"\rSuspended... {percentage:0}%");
        }
        Console.WriteLine("\rSuspended... 100%");
        Console.ResetColor();
    }
}

public static class SqfArrayParser
{
    private class NilValue { public override string ToString() => "nil"; }
    private static readonly NilValue Nil = new();

    public static string ToSqfString(object item)
    {
        return item switch
        {
            string s => "\"" + s.Replace("\"", "\"\"") + "\"",
            bool b => b.ToString().ToLower(),
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            List<object> list => "[" + string.Join(",", list.Select(ToSqfString)) + "]",
            _ => "nil"
        };
    }

    public static List<object> Parse(string input)
    {
        var tokenizer = new StringReader(input);
        return ParseArray(tokenizer);
    }

    private static void ConsumeWhitespace(StringReader reader)
    {
        while (char.IsWhiteSpace((char)reader.Peek())) reader.Read();
    }

    private static List<object> ParseArray(StringReader reader)
    {
        var list = new List<object>();
        ConsumeWhitespace(reader);
        if (reader.Read() != '[') throw new FormatException("Array must start with '['.");
        while (true)
        {
            ConsumeWhitespace(reader);
            if (reader.Peek() == ']') break;
            list.Add(ParseValue(reader));
            ConsumeWhitespace(reader);
            if (reader.Peek() == ',') reader.Read();
            else if (reader.Peek() != ']') throw new FormatException("Array elements must be separated by commas or end with ']'.");
        }
        reader.Read();
        return list;
    }

    private static object ParseValue(StringReader reader)
    {
        ConsumeWhitespace(reader);
        var peek = (char)reader.Peek();
        if (peek == '"') return ParseString(reader);
        if (peek == '[') return ParseArray(reader);
        return ParseLiteral(reader);
    }

    private static string ParseString(StringReader reader)
    {
        reader.Read();
        var sb = new StringBuilder();
        while (true)
        {
            var next = reader.Read();
            if (next == -1) throw new FormatException("Unterminated string literal.");
            var c = (char)next;
            if (c == '"')
            {
                if ((char)reader.Peek() == '"') { sb.Append('"'); reader.Read(); }
                else break;
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static object ParseLiteral(StringReader reader)
    {
        var sb = new StringBuilder();
        while (true)
        {
            var peek = reader.Peek();
            if (peek == -1 || char.IsWhiteSpace((char)peek) || (char)peek == ',' || (char)peek == ']') break;
            sb.Append((char)reader.Read());
        }

        var literal = sb.ToString();
        if (literal.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (literal.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        if (double.TryParse(literal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var number)) return number;

        return Nil;
    }
}

internal static class NativeMethods
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern IntPtr LoadLibrary(string lpFileName);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FreeLibrary(IntPtr hModule);
}

