using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Arma3.ExtensionTester.Engine
{
    public class ScriptingEngine
    {
        private readonly Dictionary<string, object> _missionNamespace = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, object> _uiNamespace = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, object> _serverNamespace = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, object> _parsingNamespace = new(StringComparer.OrdinalIgnoreCase);

        public object Execute(string code, Dictionary<string, object> localVars = null)
        {
            if (localVars == null)
            {
                Dictionary<string, object> dictionary = new(StringComparer.OrdinalIgnoreCase);
                localVars = dictionary;
            }

            var lines = code.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            object lastResult = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.EndsWith(";"))
                {
                    line = line.Substring(0, line.Length - 1).TrimEnd();
                }

                // Handle variable assignment
                var assignmentMatch = Regex.Match(line, @"^(_?[a-zA-Z0-9]+)\s*=\s*(.*)");
                if (assignmentMatch.Success)
                {
                    string varName = assignmentMatch.Groups[1].Value;
                    string valueStr = assignmentMatch.Groups[2].Value;

                    object value = EvaluateExpression(valueStr, localVars);

                    if (varName.StartsWith("_"))
                    {
                        localVars[varName] = value;
                    }
                    else
                    {
                        _missionNamespace[varName] = value; // Default to mission namespace
                    }
                    lastResult = value;
                    continue;
                }

                lastResult = EvaluateExpression(line, localVars);
            }

            return lastResult;
        }

        public object EvaluateExpression(string expression, Dictionary<string, object> localVars)
        {
            expression = expression.Trim();

            // Simple string literal
            if (expression.StartsWith("\"") && expression.EndsWith("\""))
            {
                return expression.Substring(1, expression.Length - 2).Replace("\"\"", "\"");
            }

            // Look up variable
            if (localVars.ContainsKey(expression))
            {
                return localVars[expression];
            }
            if (_missionNamespace.ContainsKey(expression))
            {
                return _missionNamespace[expression];
            }

            // Handle format["..."]
            var formatMatch = Regex.Match(expression, @"^format\[""([^""]*)""(?:,\s*(.*))?\]");
            if (formatMatch.Success)
            {
                string formatString = formatMatch.Groups[1].Value;
                string argsString = formatMatch.Groups[2].Value;
                var args = new List<object>();
                if (!string.IsNullOrEmpty(argsString))
                {
                    var argNames = argsString.Split(',');
                    foreach (var argName in argNames)
                    {
                        args.Add(EvaluateExpression(argName.Trim(), localVars));
                    }
                }
                return string.Format(formatString.Replace("%1", "{0}").Replace("%2", "{1}").Replace("%3", "{2}"), [.. args]);
            }

            // Call a command (like hint)
            var commandMatch = Regex.Match(expression, @"^([a-zA-Z_]+)\s+(.*)");
            if (commandMatch.Success)
            {
                string command = commandMatch.Groups[1].Value;
                string args = commandMatch.Groups[2].Value;
                if (Program.s_commandHandlers.TryGetValue(command, out var handler))
                {
                    handler(args);
                    return null; // hint doesn't return a value
                }
            }


            return expression; // Return as string if not understood
        }

        public void SetVariable(string fullVarName, object value)
        {
            var parts = fullVarName.Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 && parts[1].Equals("is", StringComparison.OrdinalIgnoreCase) && parts[2].Equals("a", StringComparison.OrdinalIgnoreCase) && parts[4].Equals("var", StringComparison.OrdinalIgnoreCase))
            {
                string varName = parts[0];
                string ns = parts[3];
                GetNamespace(ns)[varName] = value;
            }
            else
            {
                _missionNamespace[fullVarName] = value;
            }
        }

        public object GetVariable(string name)
        {
            if (_missionNamespace.TryGetValue(name, out var value))
            {
                return value;
            }
            return null;
        }

        public Dictionary<string, object> GetNamespace(string name)
        {
            return name.ToLower() switch
            {
                "ui" => _uiNamespace,
                "server" => _serverNamespace,
                "parsing" => _parsingNamespace,
                _ => _missionNamespace,
            };
        }
    }
}
