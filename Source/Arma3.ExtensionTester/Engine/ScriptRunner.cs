using System;
using System.IO;
using System.Threading;

namespace Arma3.ExtensionTester.Engine
{
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
}
