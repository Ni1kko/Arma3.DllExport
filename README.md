# Arma3.DllExport - [Download](https://www.nuget.org/packages/Arma3.DllExport/)
Simplify C# extensions for ARMA

```PM> Install-Package Arma3.DllExport```

```csharp
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
            output.Append("TestExtension v1.0 - LOADED!");
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
```
