using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Drawing;
using Arma3.ExtensionTester.Engine;
using Arma3.ExtensionTester.Utils;

#nullable enable

namespace Arma3.ExtensionTester
{
    public static class Program
    {
        private static readonly Dictionary<string, NativeExtension> s_loadedExtensions = new();
        public static readonly Dictionary<string, Action<string>> s_commandHandlers = new(StringComparer.OrdinalIgnoreCase);
        private static NotifyIcon? s_notifyIcon;
        private static Player? s_player;
        private static ScriptingEngine? s_scriptingEngine;

        [STAThread]
        public static void Main(string[] args)
        {
            s_notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                Visible = false
            };

            Console.Title = Environment.Is64BitProcess ? "Arma 3 Extension Tester (x64)" : "Arma 3 Extension Tester (x86)";
            Console.WriteLine($"// {Console.Title}");
            Console.WriteLine($"// Created by: Ni1kko - Make Arma Not War <3");

            SteamManager.Init();
            s_player = new Player();
            if (SteamManager.IsInitialized())
            {
                s_player.SteamID = SteamManager.GetSteamID();
                Console.WriteLine($"// Player initialized. SteamID: {s_player.SteamID}");
            }


            Console.WriteLine("// Type 'exit' to close, or 'help' for commands.");

            s_scriptingEngine = new ScriptingEngine();
            SetupCommands();

            if (args.Length > 0 && File.Exists(args[0]))
            {
                ScriptRunner.Execute(args[0]);
            }

            var inputBuilder = new StringBuilder();
            bool inMultiLineBlock = false;
            string? multiLineVarName = null;

            while (true)
            {
                Console.Write(inMultiLineBlock ? ". " : "> ");
                var line = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (inMultiLineBlock)
                {
                    if (line.Trim() == "};")
                    {
                        inputBuilder.AppendLine(line);
                        var codeBlock = inputBuilder.ToString();

                        var codeBlockContent = codeBlock.Substring(codeBlock.IndexOf('{') + 1);
                        codeBlockContent = codeBlockContent.Substring(0, codeBlockContent.LastIndexOf("};")).Trim();

                        if (multiLineVarName != null)
                        {
                            s_scriptingEngine.SetVariable(multiLineVarName, codeBlockContent);
                            Console.WriteLine($"// Code block assigned to '{multiLineVarName}'");
                        }

                        inMultiLineBlock = false;
                        inputBuilder.Clear();
                        multiLineVarName = null;
                    }
                    else
                    {
                        inputBuilder.AppendLine(line);
                    }
                }
                else
                {
                    var multiLineMatch = Regex.Match(line, @"^(.+?)\s*=\s*{");
                    if (multiLineMatch.Success)
                    {
                        inMultiLineBlock = true;
                        multiLineVarName = multiLineMatch.Groups[1].Value.Trim();
                        inputBuilder.AppendLine("{");
                    }
                    else
                    {
                        ProcessCommand(line);
                    }
                }
            }

            foreach (var ext in s_loadedExtensions.Values)
            {
                ext.Dispose();
            }
            s_notifyIcon?.Dispose();
            SteamManager.Shutdown();
        }

        private static void ShowNotification(string title, string message)
        {
            if (s_notifyIcon == null) return;
            s_notifyIcon.Visible = true;
            s_notifyIcon.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);
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
            s_commandHandlers["getPlayerUID"] = arg =>
            {
                if (s_player != null)
                {
                    Console.WriteLine($"\"{s_player.SteamID}\"");
                }
                else
                {
                    Console.WriteLine("\"\"");
                }
            };
            s_commandHandlers["profileName"] = _ =>
            {
                if (s_player != null)
                {
                    Console.WriteLine($"\"{s_player.Name}\"");
                }
                else
                {
                    Console.WriteLine("\"\"");
                }
            };
            s_commandHandlers["hint"] = arg => {
                var evaluated = s_scriptingEngine?.EvaluateExpression(arg, new Dictionary<string, object>()) as string ?? arg;
                ShowNotification("Arma 3 Hint", evaluated);
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
            s_commandHandlers["isNil"] = arg =>
            {
                var match = Regex.Match(arg, @"^""([^""]*)""");
                if (match.Success)
                {
                    var varName = match.Groups[1].Value;
                    var result = s_scriptingEngine?.GetVariable(varName) == null;
                    Console.WriteLine(result.ToString().ToLower());
                }
                else
                {
                    Console.WriteLine("true");
                }
            };
            s_commandHandlers["call"] = arg =>
            {
                object? variable = s_scriptingEngine?.GetVariable(arg.Trim());
                if (variable is string code)
                {
                    var result = s_scriptingEngine?.Execute(code);
                    if (result != null)
                    {
                        Console.WriteLine(SqfArrayParser.ToSqfString(result));
                    }
                }
                else
                {
                    Console.WriteLine("Error: Variable is not a code block or does not exist.");
                }
            };
        }

        public static void ProcessCommand(string command)
        {
            command = command.Trim();

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
                    // If not a known command, try executing as a script line
                    var result = s_scriptingEngine?.Execute(command);
                    if (result != null)
                    {
                        Console.WriteLine(SqfArrayParser.ToSqfString(result));
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
            Console.WriteLine("1. Call Extension (Simple): \"<name>\" callExtension \"<function>\"");
            Console.WriteLine("2. Call Extension (Args):   \"<name>\" callExtension [\"<fnc>\", [<arg1>, <arg2>]]");
            Console.WriteLine("3. Get Version (Shortcut):  callExtension \"<name>\"");
            Console.WriteLine("4. Unload Extension:        freeExtension \"<name>\"");
            Console.WriteLine("5. Execute Script:          execVM \"<path_to_script.txt>\"");
            Console.WriteLine("6. Pause Script:            sleep <seconds>");
            Console.WriteLine("\n--- Engine Commands ---");
            Console.WriteLine("   getPlayerUID player       (Gets the player's SteamID64)");
            Console.WriteLine("   profileName               (Gets the player's name)");
            Console.WriteLine("   hint \"<message>\"          (Shows a Windows notification)");
            Console.WriteLine("   systemChat \"<message>\"    (Shows a Windows notification)");
            Console.WriteLine("   diag_log \"<message>\"      (Prints a log message to console)");
            Console.WriteLine("   copyToClipboard \"<text>\"(Copies text to clipboard)");
            Console.WriteLine("   format [\"fmt\", <arg1>]  (Formats a string, e.g., format[\"Hello %1\", \"World\"])");
            Console.WriteLine("\n--- Scripting ---");
            Console.WriteLine("   <var> = <value>;                     (e.g., myVar = \"hello\")");
            Console.WriteLine("   <var> = { <code> };                  (Defines a multi-line code block)");
            Console.WriteLine("   call <var>;                          (Executes a code block)");
            Console.WriteLine("   isNil \"<var>\";                       (Checks if a variable is defined)");
            Console.WriteLine("   <var> is a <namespace> var;         (e.g., myVar is a ui var)");
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
}
