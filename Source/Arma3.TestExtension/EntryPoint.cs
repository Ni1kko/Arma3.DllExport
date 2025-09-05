using Arma3.DllExport;
using System.Text;

namespace TestExtension
{
    public static class EntryPoint
    {
        [ArmaDllExport(ArmaExport.RVExtensionVersion)]
        public static void RVExtensionVersion(StringBuilder output, int outputSize)
        {
            // Write the response directly into the buffer provided by Arma 3.
            output.Append("RVExtensionVersion -  v0.1.0.8");
        }

        [ArmaDllExport(ArmaExport.RVExtension)]
        public static void RVExtension(StringBuilder output, int outputSize, string function)
        {
            // Write the response directly into the buffer.
            switch (function.ToLower())
            {
                case "ping":
                    output.Append("pong");
                    break;

                case "getservertime":
                    output.Append(System.DateTime.Now.ToString("HH:mm:ss"));
                    break;

                default:
                    output.Append("Unknown command");
                    break;
            }
        }
    }
}