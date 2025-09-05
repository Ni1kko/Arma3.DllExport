using System;
using SteamworksSharp;
using SteamworksSharp.Native;

namespace Arma3.ExtensionTester.Utils
{
    public static class SteamManager
    {
        private static bool s_initialized = false;
        private static ulong s_steamId = 0;

        public static bool Init()
        {
            try
            {
                // Per the documentation, initialize the native layer first.
                SteamNative.Initialize();
                // Then, initialize the managed API.
                s_initialized = SteamApi.Initialize();
            }
            catch (DllNotFoundException e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"// ERROR: {e.Message}. Could not find steam_api.dll.");
                Console.ResetColor();
                s_initialized = false;
                return false;
            }


            if (!s_initialized)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("// SteamAPI.Init() failed. Is Steam running?");
                Console.ResetColor();
                return false;
            }

            Console.WriteLine("// SteamManager initialized successfully with SteamworksSharp.");
            s_steamId = SteamApi.SteamUser.GetSteamID();
            return true;
        }

        public static ulong GetSteamID()
        {
            return s_steamId;
        }

        public static bool IsInitialized()
        {
            return s_initialized;
        }
    }
}

