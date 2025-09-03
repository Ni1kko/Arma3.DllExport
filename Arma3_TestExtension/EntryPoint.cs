using Arma3.DllExport;

namespace TestExtension
{
    internal class EntryPoint
    {
        [ArmaDllExport(ArmaExport.RVExtensionVersion)]
        public static string RVExtensionVersion()
        {
            return "TestExtension v1.0 - LOADED!";
        }

        [ArmaDllExport(ArmaExport.RVExtension)]
        public static string RVExtension(string input)
        {
            return input;
        }
    }
}
