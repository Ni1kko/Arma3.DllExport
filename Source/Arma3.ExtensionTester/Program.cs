using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;


namespace Arma3.ExtensionTester
{
    public static class Program
    {
        // A dictionary to keep track of loaded extensions by name.
        private static readonly Dictionary<string, NativeExtension> s_loadedExtensions = new();

        // Main entry point of the application.
        public static void Main(string[] args)
        {
            Console.Title = Environment.Is64BitProcess ? "Arma 3 Extension Tester (x64)" : "Arma 3 Extension Tester (x86)";
            Console.WriteLine($"// {Console.Title}");
            Console.WriteLine("// Type 'exit' to close, or 'help' for commands.");

            // If a file is dragged onto the .exe, its path will be in args.
            if (args.Length > 0 && File.Exists(args[0]))
            {
                ScriptRunner.Execute(args[0]);
            }

            // Start the interactive command loop.
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

            // Clean up by unloading all extensions before exiting.
            foreach (var ext in s_loadedExtensions.Values)
            {
                ext.Dispose();
            }
        }

        // Processes a single command line string.
        public static void ProcessCommand(string command)
        {
            command = command.Trim();
            if (string.IsNullOrEmpty(command)) return;

            // Display comments from scripts
            if (command.StartsWith("//"))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(command);
                Console.ResetColor();
                return;
            }

            // Ignore blank lines and lines starting with #
            if (command.StartsWith("#")) return;

            try
            {
                // Simple commands first
                if (command.Equals("help", StringComparison.OrdinalIgnoreCase))
                {
                    PrintHelp();
                    return;
                }

                var lowerCommand = command.ToLower();

                // execVM "<script>"
                if (lowerCommand.StartsWith("execvm"))
                {
                    var match = Regex.Match(command, @"execVM\s+""([^""]+)""", RegexOptions.IgnoreCase);
                    if (match.Success) ScriptRunner.Execute(match.Groups[1].Value);
                    else Console.WriteLine("Error: Invalid execVM syntax. Use: execVM \"<script_path>\"");
                    return;
                }

                // freeExtension "<name>"
                if (lowerCommand.StartsWith("freeextension"))
                {
                    var match = Regex.Match(command, @"freeExtension\s+""([^""]+)""", RegexOptions.IgnoreCase);
                    if (match.Success) FreeExtension(match.Groups[1].Value);
                    else Console.WriteLine("Error: Invalid freeExtension syntax. Use: freeExtension \"<name>\"");
                    return;
                }

                // sleep <seconds>
                if (lowerCommand.StartsWith("sleep"))
                {
                    var match = Regex.Match(command, @"sleep\s+([0-9\.]+)", RegexOptions.IgnoreCase);
                    if (match.Success && float.TryParse(match.Groups[1].Value, out var duration))
                    {
                        ScriptRunner.Sleep(duration);
                    }
                    else
                    {
                        Console.WriteLine("Error: Invalid sleep syntax. Use: sleep <seconds>");
                    }
                    return;
                }

                // The main 'callExtension' command has several forms, so we parse it more carefully.
                // Regex to capture: "extName" callExtension <the rest of the line>
                var callExtMatch = Regex.Match(command, @"^""(?<name>[^""]+)""\s+callExtension\s+(?<args>.+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (callExtMatch.Success)
                {
                    var extName = callExtMatch.Groups["name"].Value;
                    var argsStr = callExtMatch.Groups["args"].Value.Trim();
                    CallExtension(extName, argsStr);
                }
                // Shorthand for version check: callExtension "<name>"
                else
                {
                    var versionMatch = Regex.Match(command, @"^callExtension\s+""(?<name>[^""]+)""", RegexOptions.IgnoreCase);
                    if (versionMatch.Success)
                    {
                        CallExtensionVersion(versionMatch.Groups["name"].Value);
                    }
                    else
                    {
                        Console.WriteLine($"Error: Unknown command or syntax: '{command}'");
                    }
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
            Console.WriteLine("1. Call Extension (simple):");
            Console.WriteLine("   \"<name>\" callExtension \"<function>\"");
            Console.WriteLine("\n2. Call Extension (with args):");
            Console.WriteLine("   \"<name>\" callExtension [\"<function>\", [\"<arg1>\", \"<arg2>\"]]");
            Console.WriteLine("\n3. Get Extension Version (shortcut):");
            Console.WriteLine("   callExtension \"<name>\"");
            Console.WriteLine("\n4. Unload Extension:");
            Console.WriteLine("   freeExtension \"<name>\"");
            Console.WriteLine("\n5. Execute Script:");
            Console.WriteLine("   execVM \"<path_to_script.txt>\"");
            Console.WriteLine("\n6. Pause Script (script only):");
            Console.WriteLine("   sleep <seconds>");
            Console.WriteLine("\n7. Exit Application:");
            Console.WriteLine("   exit\n");
        }

        private static NativeExtension GetOrLoadExtension(string name)
        {
            if (s_loadedExtensions.TryGetValue(name.ToLower(), out var ext))
            {
                return ext;
            }

            // Arma convention is to look for _x64 or _x86 suffixes first.
            var archSuffix = Environment.Is64BitProcess ? "_x64" : "_x86";
            var archDllName = $"{name}{archSuffix}.dll";
            var plainDllName = $"{name}.dll";

            string dllPath = "";
            if (File.Exists(archDllName))
            {
                dllPath = archDllName;
            }
            else if (File.Exists(plainDllName))
            {
                dllPath = plainDllName;
            }
            else
            {
                Console.WriteLine($"Error: Could not find '{archDllName}' or '{plainDllName}'.");
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

            if (wasCalled)
            {
                PrintOutput(output, sw.Elapsed.TotalMilliseconds);
            }
            else
            {
                Console.WriteLine($"Error: Extension '{name}' does not export RVExtensionVersion.");
            }
        }

        private static void CallExtension(string name, string argsString)
        {
            var ext = GetOrLoadExtension(name);
            if (ext == null) return;

            // Case 1: Simple call -> "function"
            var simpleMatch = Regex.Match(argsString, @"^""([^""]*)""$");
            if (simpleMatch.Success)
            {
                var function = simpleMatch.Groups[1].Value;
                var sw = Stopwatch.StartNew();
                var (output, wasCalled) = ext.Invoke(function);
                sw.Stop();

                if (!wasCalled)
                {
                    Console.WriteLine($"Error: Extension '{name}' does not export RVExtension.");
                    return;
                }
                PrintOutput(output, sw.Elapsed.TotalMilliseconds);
                return;
            }

            // Case 2: Array call -> ["function", ["arg1", "arg2"]]
            try
            {
                var parsed = SqfArrayParser.Parse(argsString);
                if (parsed.Count < 1 || parsed.Count > 2 || !(parsed[0] is string))
                {
                    throw new FormatException("Expected format: [\"<function>\"] or [\"<function>\", [<args>]]");
                }

                var function = (string)parsed[0];
                var args = new string[0];

                if (parsed.Count == 2)
                {
                    if (parsed[1] is List<object> argList)
                    {
                        args = argList.Cast<string>().ToArray();
                    }
                    else
                    {
                        throw new FormatException("Second element must be an array of strings.");
                    }
                }

                var sw = Stopwatch.StartNew();
                var (output, wasCalled) = ext.Invoke(function, args);
                sw.Stop();

                if (!wasCalled)
                {
                    Console.WriteLine($"Error: Extension '{name}' does not export RVExtensionArgs.");
                    return;
                }
                PrintOutput(output, sw.Elapsed.TotalMilliseconds);
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
            else
            {
                Console.WriteLine($"Error: Extension '{name}' is not loaded.");
            }
        }

        private static void PrintOutput(string output, double milliseconds)
        {
            // Format output like Arma: wrap in quotes and escape internal quotes.
            var formattedOutput = "\"" + output.Replace("\"", "\"\"") + "\"";
            Console.WriteLine(formattedOutput);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"({milliseconds:F4} ms)");
            Console.ResetColor();
        }
    }


    /// <summary>
    /// Manages loading a native DLL and calling its exported Arma 3 extension functions.
    /// </summary>
    public class NativeExtension : IDisposable
    {
        // These delegates match the function signatures required by Arma 3.
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void RVExtensionDelegate(StringBuilder output, int outputSize, [MarshalAs(UnmanagedType.LPStr)] string function);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void RVExtensionArgsDelegate(StringBuilder output, int outputSize, [MarshalAs(UnmanagedType.LPStr)] string function, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] args, int argCount);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void RVExtensionVersionDelegate(StringBuilder output, int outputSize);

        private readonly IntPtr _moduleHandle;
        private readonly RVExtensionDelegate _rvExtension;
        private readonly RVExtensionArgsDelegate _rvExtensionArgs;
        private readonly RVExtensionVersionDelegate _rvExtensionVersion;

        private const int OUTPUT_BUFFER_SIZE = 10240; // Standard buffer size for Arma extensions.

        public NativeExtension(string dllPath)
        {
            _moduleHandle = NativeMethods.LoadLibrary(dllPath);
            if (_moduleHandle == IntPtr.Zero)
            {
                throw new DllNotFoundException($"Could not load library: {dllPath}. Error code: {Marshal.GetLastWin32Error()}");
            }

            // Load pointers to the functions. It's okay if some are null; we'll check before calling.
            _rvExtension = GetDelegate<RVExtensionDelegate>("RVExtension");
            _rvExtensionArgs = GetDelegate<RVExtensionArgsDelegate>("RVExtensionArgs");
            _rvExtensionVersion = GetDelegate<RVExtensionVersionDelegate>("RVExtensionVersion");
        }

        private T GetDelegate<T>(string procName) where T : Delegate
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

        public void Dispose()
        {
            if (_moduleHandle != IntPtr.Zero)
            {
                NativeMethods.FreeLibrary(_moduleHandle);
            }
        }
    }

    /// <summary>
    /// A very basic parser for SQF-like array syntax.
    /// Example: ["func1", ["arg1", "arg2"]]
    /// </summary>
    public static class SqfArrayParser
    {
        public static List<object> Parse(string input)
        {
            var tokenizer = new StringReader(input);
            return ParseArray(tokenizer);
        }

        private static void ConsumeWhitespace(StringReader reader)
        {
            while (char.IsWhiteSpace((char)reader.Peek()))
            {
                reader.Read();
            }
        }

        private static List<object> ParseArray(StringReader reader)
        {
            var list = new List<object>();
            ConsumeWhitespace(reader);
            if (reader.Read() != '[') throw new FormatException("Array must start with '['.");

            while (reader.Peek() != ']')
            {
                ConsumeWhitespace(reader);
                if (reader.Peek() == ']') break;

                list.Add(ParseValue(reader));

                ConsumeWhitespace(reader);
                if (reader.Peek() == ',')
                {
                    reader.Read(); // Consume comma
                }
                else if (reader.Peek() != ']')
                {
                    throw new FormatException("Array elements must be separated by commas.");
                }
            }

            reader.Read(); // Consume ']'
            return list;
        }

        private static object ParseValue(StringReader reader)
        {
            ConsumeWhitespace(reader);
            if (reader.Peek() == '"') return ParseString(reader);
            if (reader.Peek() == '[') return ParseArray(reader);
            throw new FormatException("Unsupported value type in array.");
        }

        private static string ParseString(StringReader reader)
        {
            reader.Read(); // Consume opening '"'
            var sb = new StringBuilder();
            while (true)
            {
                int next = reader.Read();
                if (next == -1) throw new FormatException("Unterminated string literal.");

                char c = (char)next;
                if (c == '"')
                {
                    // Handle escaped quotes ("")
                    if ((char)reader.Peek() == '"')
                    {
                        sb.Append('"');
                        reader.Read(); // Consume the second quote
                    }
                    else
                    {
                        break; // End of string
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Handles script execution for the execVM command.
    /// </summary>
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
            foreach (var line in lines)
            {
                Program.ProcessCommand(line);
            }

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
            int interval = 200; // Update progress every 200ms
            int progress = 0;

            while (progress < totalMilliseconds)
            {
                int wait = Math.Min(interval, totalMilliseconds - progress);
                Thread.Sleep(wait);
                progress += wait;
                float percentage = (float)progress / totalMilliseconds * 100;
                Console.Write($"\rSuspended... {percentage:0}%");
            }

            Console.WriteLine("\rSuspended... 100%"); // Clear the line
            Console.ResetColor();
        }
    }

    /// <summary>
    /// P/Invoke definitions for kernel32.dll functions needed to interact with native DLLs.
    /// </summary>
    internal static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FreeLibrary(IntPtr hModule);
    }
}
