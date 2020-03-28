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

    public static class LobbyTools
    {
        static readonly string[] LOBBYTYPES = { "Matchmaking Race", "Matchmaking Arena", "Matchmaking Lucky Dip", "Custom Game"};
        static readonly string[] EVENTS = { "Normal Race", "Battle Race", "Boost Race", "Capture the Chao", "Battle Arena" };
        static readonly string[] TRACKS = { "Seasonal Shrines", "Graffiti City", "Adder's Lair", "Chilly Castle", "Graveyard Gig", "Carrier Zone", "Galactic Parade", "Temple Trouble", "Sanctuary Falls", "Dream Valley", "Race of Ages", "Ocean View", "Samba Studios", "Dragon Canyon", "Burning Depths", "Roulette Road", "Shibuya Downtown", "Egg Hangar", "Sunshine Tour", "Rogue's Landing", "Outrun Bay" };
        static readonly string[] ARENAS = { "Neon Docks", "Battle Bay", "Creepy Courtyard", "Rooftop Rumble", "Monkey Ball Park" };
        static readonly string[] DIFFICULTIES = { "C", "B", "A", "S" };


        public static string GetLobbyType(int lobbyType)
        {
            return (lobbyType >=0 && lobbyType <= LOBBYTYPES.Length) ? LOBBYTYPES[lobbyType] : null;
        }
        public static string GetActivity(int state, int raceProgress, int countdown)
        {
            switch(state)
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
                    if (raceProgress == 0)
                        return "Race starting";
                    else if (raceProgress == 100)
                        return "Race finishing";
                    else
                        return "Racing (" + raceProgress + "%)";
            }
        }

        public static string GetEvent(int lobbyType, int matchMode)
        {
            int eventId = -1;

            switch (lobbyType)
            {
                case 0:
                    eventId = 0;
                    break;

                case 1:
                    if (matchMode < 5)
                        eventId = 4;
                    else
                        eventId = 3;
                    break;

                case 2:
                    matchMode %= 70;

                    if (matchMode > 59)
                    {
                        if (matchMode < 64)
                            eventId = 4;
                        else
                            eventId = 3;
                        break;
                    }
                    else
                    {
                        if (matchMode < 20)
                            eventId = 0;
                        else if (matchMode < 40)
                            eventId = 1;
                        else
                            eventId = 2;
                        break;
                    }

                case 3:
                    if (matchMode < 42)
                        eventId = 0;
                    else if (matchMode < 84)
                        eventId = 1;
                    else if (matchMode < 126)
                        eventId = 2;
                    else if (matchMode < 131)
                        eventId = 3;
                    else
                        eventId = 4;
                    break;
            }

            return EVENTS[eventId];
        }

        public static string[] GetMap(int lobbyType, int matchMode)
        {
            bool mirror = false;
            bool arena = false;
            int trackId = -1;

            switch (lobbyType)
            {
                case 0:
                    if (matchMode < 20)
                        trackId = matchMode;
                    else
                    {
                        trackId = matchMode - 20;
                        mirror = true;
                    }
                    break;

                case 1:
                    if (matchMode > 4)
                        matchMode -= 5;

                    trackId = matchMode;
                    arena = true;
                    break;

                case 2:
                    if (matchMode < 60 || matchMode > 69)
                    {
                        if (matchMode > 69)
                            matchMode -= 10;

                        trackId = matchMode % 20;

                        if (matchMode > 69)
                            mirror = true;
                    }
                    else
                    {
                        trackId = matchMode % 5;
                        arena = true;
                    }
                    break;

                case 3:
                    if (matchMode < 126)
                    {
                        matchMode %= 42;
                        trackId  = matchMode % 21;
                        if (matchMode > 20)
                            mirror = true;
                    }
                    else
                    {
                        matchMode -= 126;
                        trackId = matchMode % 5;
                        arena = true;
                    }
                    break;
                default:
                    return null;
            }

            if (arena)
            {
                return new string[] {"Arena", ARENAS[trackId]};
            }
            else
            {
                return new string[] {"Track", TRACKS[trackId] + (mirror ? " Mirror" : "")};
            }
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