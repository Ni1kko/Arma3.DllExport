using System;
using System.Runtime.InteropServices;
using System.Text;

#nullable enable

namespace Arma3.ExtensionTester
{
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
}
