using Arma3.DllExport;

namespace TestDll
{
    public class SomeClass
    {
        [ArmaDllExport]
        public static string Invoke(string input, int size)
        {
            return input;
        }
    }
}
