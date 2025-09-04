using System;
using Steamworks;

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
                s_initialized = SteamAPI.Init();
            }
            catch (DllNotFoundException e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"// ERROR: {e.Message}. Could not find steam_api64.dll.");
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

            Console.WriteLine("// SteamManager initialized successfully.");
            s_steamId = SteamUser.GetSteamID().m_SteamID;
            return true;
        }

        public static void Shutdown()
        {
            if (s_initialized)
            {
                SteamAPI.Shutdown();
            }
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

