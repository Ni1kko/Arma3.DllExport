using Arma3.DllExport;

namespace TestExtension
{
    // Making the class public and static is a common convention for utility classes like this.
    public static class EntryPoint
    {
        [ArmaDllExport(ArmaExport.RVExtensionVersion)]
        public static string RVExtensionVersion()
        {
            return "TestExtension v1.0 - LOADED!";
        }

        [ArmaDllExport(ArmaExport.RVExtension)]
        public static string RVExtension(string input)
        {
            // Use a switch statement to handle different commands from Arma 3.
            switch (input.ToLower())
            {
                case "ping":
                    return "pong";

                case "getservertime":
                    return System.DateTime.Now.ToString("HH:mm:ss");

                default:
                    return "Unknown command";
            }
        }
    }
}