namespace SLB
{
    public class LobbyInfo
    {
        public string name;
        public ulong id;
        public int playerCount;
        public int type;
        public int raceProgress;
        public int countdown;
        public int matchMode;
        public int state;
        public int difficulty;
        public bool hidden; // hide this lobby in status messages
        public Mod mod; // lobby is using a mod
    }

    public enum Mod
    {
        CloNoBumpClassic,
        CloNoBumpSupercharged,
    }

    struct LobbyDetails
    {
        public bool unknown1;
        public bool usingScore;
        public byte lobbyType;
        public byte matchMode;
        public byte unknown2;
        public byte unknown3;
        public byte unknown4;
        public byte timerState;
        public byte unknown5;
        public uint unknown6;
        public byte countdownTime;
        public byte difficulty;
        public byte unknown7;
        public byte unknown8;
        public byte progressPercentage;
        public ushort unknown9;
        public string hostName;
        public byte unknown10;
        public byte playerCount;
        public ushort unknown11;
    }

    public static class LobbyTools
    {
        static readonly string[] LOBBYTYPES = { "Matchmaking Race", "Matchmaking Arena", "Matchmaking Lucky Dip", "Custom Game" };
        static readonly string[] EVENTS = { "Normal Race", "Battle Race", "Boost Race", "Capture the Chao", "Battle Arena" };
        static readonly string[] TRACKS = { "Seasonal Shrines", "Graffiti City", "Adder's Lair", "Chilly Castle", "Graveyard Gig", "Carrier Zone", "Galactic Parade", "Temple Trouble", "Sanctuary Falls", "Dream Valley", "Race of Ages", "Ocean View", "Samba Studios", "Dragon Canyon", "Burning Depths", "Roulette Road", "Shibuya Downtown", "Egg Hangar", "Sunshine Tour", "Rogue's Landing", "Outrun Bay" };
        static readonly string[] ARENAS = { "Neon Docks", "Battle Bay", "Creepy Courtyard", "Rooftop Rumble", "Monkey Ball Park" };
        static readonly string[] DIFFICULTIES = { "C", "B", "A", "S" };


        public static string GetLobbyType(int lobbyTypeId)
        {
            return (lobbyTypeId >= 0 && lobbyTypeId <= LOBBYTYPES.Length) ? LOBBYTYPES[lobbyTypeId] : "Unknown";
        }
        public static string GetActivity(int state, int eventId, int raceProgress, int countdown)
        {
            switch (state)
            {
                case 0:
                    return "Waiting";
                case 1:
                    return "Waiting (" + countdown + "s)";
                case 2:
                    return "Voting (" + countdown + "s)";
                case 255:
                    return "Unknown";
                default:
                    if (eventId <= 2)
                    {
                        if (raceProgress == 0)
                            return "Race starting";
                        else if (raceProgress == 100)
                            return "Race finishing";
                        else
                            return "Racing (" + raceProgress + "%)";
                    }
                    else
                    {
                        if (raceProgress == 0)
                            return "Battle starting";
                        else if (raceProgress == 100)
                            return "Battle finishing";
                        else
                            return "Battling (" + (raceProgress * 360) / 100 + "s)";
                    }
            }
        }

        public static int GetEventId(int lobbyType, int matchMode)
        {
            switch (lobbyType)
            {
                case 0:
                    return 0;
                case 1:
                    if (matchMode < 5)
                        return 4;
                    else
                        return 3;
                case 2:
                    matchMode %= 70;

                    if (matchMode > 59)
                    {
                        if (matchMode < 64)
                            return 4;
                        else
                            return 3;
                    }
                    else
                    {
                        if (matchMode < 20)
                            return 0;
                        else if (matchMode < 40)
                            return 1;
                        else
                            return 2;
                    }

                case 3:
                    if (matchMode < 42)
                        return 0;
                    else if (matchMode < 84)
                        return 1;
                    else if (matchMode < 126)
                        return 2;
                    else if (matchMode < 131)
                        return 3;
                    else
                        return 4;
                default:
                    return -1;
            }
        }

        public static (int, bool) GetMapId(int lobbyType, int matchMode)
        {
            switch (lobbyType)
            {
                case 0:
                    if (matchMode < 20)
                    {
                        return (matchMode, false);
                    }
                    else
                    {
                        return (matchMode - 20, true);
                    }
                case 1:
                    if (matchMode > 4)
                        matchMode -= 5;
                    return (matchMode, false);
                case 2:
                    if (matchMode < 60 || matchMode > 69)
                    {
                        if (matchMode > 69)
                            matchMode -= 10;
                        return (matchMode % 20, matchMode > 69);
                    }
                    else
                    {
                        return (matchMode % 5, false);
                    }
                case 3:
                    if (matchMode < 126)
                    {
                        matchMode %= 42;
                        return (matchMode % 21, matchMode > 20);
                    }
                    else
                    {
                        matchMode -= 126;
                        return (matchMode % 5, false);
                    }
                default:
                    return (-1, false);
            }
        }

        public static string GetEventName(int eventId)
        {
            return (eventId >= 0 && eventId <= EVENTS.Length) ? EVENTS[eventId] : "Unknown";
        }

        public static string GetMapName(int eventId, int mapId, bool mirror)
        {
            return eventId <= 2 ? TRACKS[mapId] : ARENAS[mapId] + (mirror ? " mirror" : "");
        }

        public static string GetMapType(int eventId)
        {
            return eventId <= 2 ? "Track" : "Arena";
        }

        public static string GetDifficulty(int lobbyType, int difficulty)
        {
            if (lobbyType != 3)
                difficulty = 3;
            if (difficulty >= 0 && difficulty < 4)
                return DIFFICULTIES[difficulty] + " Class";
            else
                return "Unknown";
        }
    }
}