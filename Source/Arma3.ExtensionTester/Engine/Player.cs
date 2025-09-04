namespace Arma3.ExtensionTester.Engine
{
    public class Player
    {
        public string Name { get; set; }
        public ulong SteamID { get; set; }

        public Player()
        {
            Name = "DummyPlayer";
            SteamID = 0;
        }
    }
}
