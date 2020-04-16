namespace SLB
{
    public struct LobbyInfo
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
    }

    public struct LobbyCounts
    {
        public int matchmakingLobbies;
        public int matchmakingPlayers;
        public int customGameLobbies;
        public int customGamePlayers;
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
            return (lobbyTypeId >= 0 && lobbyTypeId <= LOBBYTYPES.Length) ? LOBBYTYPES[lobbyTypeId] : null;
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
                            return "Battling";
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
            bool mirror = false;
            int mapId = -1;

            switch (lobbyType)
            {
                case 0:
                    if (matchMode < 20)
                        mapId = matchMode;
                    else
                    {
                        mapId = matchMode - 20;
                        mirror = true;
                    }
                    break;

                case 1:
                    if (matchMode > 4)
                        matchMode -= 5;

                    mapId = matchMode;
                    break;

                case 2:
                    if (matchMode < 60 || matchMode > 69)
                    {
                        if (matchMode > 69)
                            matchMode -= 10;

                        mapId = matchMode % 20;

                        if (matchMode > 69)
                            mirror = true;
                    }
                    else
                    {
                        mapId = matchMode % 5;
                    }
                    break;

                case 3:
                    if (matchMode < 126)
                    {
                        matchMode %= 42;
                        mapId = matchMode % 21;
                        if (matchMode > 20)
                            mirror = true;
                    }
                    else
                    {
                        matchMode -= 126;
                        mapId = matchMode % 5;
                    }
                    break;
                default:
                    return (-1, false);
            }

            return (mapId, mirror);
        }

        public static string GetEventName(int eventId)
        {
            return (eventId >= 0 && eventId <= EVENTS.Length) ? EVENTS[eventId] : null;
        }

        public static string GetMapName(int eventId, int mapId, bool mirror)
        {
            return eventId <= 2 ? TRACKS[mapId] : ARENAS[mapId] + (mirror ? "" : " mirror");
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
                return null;
        }
    }
}