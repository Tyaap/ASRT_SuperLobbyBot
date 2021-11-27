using System;
using System.Collections.Generic;
using System.Text;
using System.Timers;
using System.Threading.Tasks;
using SteamKit2;

namespace SLB
{
    static class Steam
    {
        // environment variables
        private static string ENV_STEAM_USER => Environment.GetEnvironmentVariable("STEAM_USER");
        private static string ENV_STEAM_PASS => Environment.GetEnvironmentVariable("STEAM_PASS");
        private static int ENV_MESSAGE_WAIT => int.Parse(Environment.GetEnvironmentVariable("MESSAGE_WAIT")) * 1000;

        // constants
        public const int APPID = 212480;
        const int CALLBACK_WAIT = 100;
        const int MAX_ERRORS = 10;

        // Steam client
        private static SteamClient steamClient;
        private static CallbackManager manager;
        private static SteamUser steamUser;
        private static SteamMatchmaking steamMatchmaking;
        private static SteamUserStats steamUserStats;
        private static bool loggedIn;
        private static bool connected;
        private static Timer callbackTimer;

        // info for status message
        private static int playerCount;
        private static LobbyStats lobbyStats;
        private static List<LobbyInfo> lobbyInfos;

        // timer for updating messages
        private static Timer messageTimer;
        static int errorCount = 0;



        public static void Start()
        {
            Console.WriteLine("Steam.Start()");
            // create our steamclient instance
            steamClient = new SteamClient();

            // create the callback manager which will route callbacks to function calls
            manager = new CallbackManager(steamClient);

            // get the steamuser handler, which is used for logging on after successfully connecting
            steamUser = steamClient.GetHandler<SteamUser>();
            steamMatchmaking = steamClient.GetHandler<SteamMatchmaking>();
            steamUserStats = steamClient.GetHandler<SteamUserStats>();

            // register a few callbacks we're interested in
            // these are registered upon creation to a callback manager, which will then route the callbacks
            // to the functions specified
            manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
            manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
            manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
            manager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);

            // initiate the connection
            steamClient.Connect();

            // Steam callback timer
            callbackTimer = new Timer(CALLBACK_WAIT) { AutoReset = true };
            callbackTimer.Elapsed += CallbackTimerTick;
            callbackTimer.Start();

            // message update timer
            messageTimer = new Timer(ENV_MESSAGE_WAIT) { AutoReset = true };
            messageTimer.Elapsed += MessageTimerTick;
            messageTimer.Start();
        }

        public static void Stop()
        {
            Console.WriteLine("Steam.Stop()");
            callbackTimer?.Stop();
            callbackTimer?.Dispose();
            callbackTimer = null;

            messageTimer?.Stop();
            messageTimer?.Dispose();
            messageTimer = null;

            if (loggedIn)
            {
                steamUser?.LogOff();
            }
            else
            {
                steamClient?.Disconnect();
            }
            
            while (connected)
            {
                manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(CALLBACK_WAIT));
            }
        }

        static void CallbackTimerTick(object caller, ElapsedEventArgs e)
        {
            try
            {
                manager.RunWaitCallbacks();
            }
            catch(Exception ex)
            {
                Console.WriteLine("Steam.CallbackTimerTick() Exception!\n" + ex);
            }
        }

        static async void MessageTimerTick(object caller, ElapsedEventArgs e)
        {
            try
            {
                // web status
                Web.message = string.Format(
                    "Discord logged in: {0}\n" +
                    "Steam logged in: {1}\n" +
                    "Super Lobby Bot is {2}",
                Discord.loggedIn, loggedIn, Discord.loggedIn && loggedIn ? "working! :)" : "not working! :(");

                DateTime timestamp = DateTime.Now;
                if (loggedIn && await RefreshLobbyInfo())
                {
                    // record data
                    Stats.WriteEntryData(timestamp, lobbyInfos);

                    // get lobby stats
                    lobbyStats = Stats.ProcessEntry(timestamp, lobbyInfos);

                    errorCount = 0;
                }
                else
                {
                    playerCount = -1;
                    lobbyStats = new LobbyStats();
                    lobbyInfos = new List<LobbyInfo>();
                    if (loggedIn)
                    {
                        errorCount++;
                        if (errorCount == MAX_ERRORS)
                        {
                            Console.WriteLine("MessageTimerTickError() Failed to refresh {0} times, disconnecting from Steam.", MAX_ERRORS);
                            steamClient.Disconnect();
                            errorCount = 0;
                        }
                    }
                    else
                    {
                        steamClient.Connect();
                    }
                }

                // update status messages
                Discord.UpdateStatus(timestamp, playerCount, lobbyInfos, lobbyStats).GetAwaiter().GetResult();
            }
            catch(Exception ex)
            {
                Console.WriteLine("Steam.MessageTimerTick() Exception!\n" + ex);
            }
        }

        static async Task<bool> RefreshLobbyInfo()
        {
            // get number of current players
            try
            {
                var numberOfPlayersCallback = await steamUserStats.GetNumberOfCurrentPlayers(APPID);
                if (numberOfPlayersCallback.Result == EResult.OK)
                {
                    playerCount = (int)numberOfPlayersCallback.NumPlayers;
                }
                else
                {
                    Console.WriteLine("Steam.RefreshLobbyInfo() Failed to get number of current players ({0})", numberOfPlayersCallback.Result);
                    return false;
                }
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Steam.RefreshLobbyInfo() Failed to get number of current players (Timeout)");
                return false;
            }

            // get lobby list
            try
            {
                var getLobbyListCallback = await steamMatchmaking.GetLobbyList(APPID,
                    new List<SteamMatchmaking.Lobby.Filter>()
                    {
                        new SteamMatchmaking.Lobby.DistanceFilter(ELobbyDistanceFilter.Worldwide),
                        new SteamMatchmaking.Lobby.SlotsAvailableFilter(0),
                    }
                );
                
                if (getLobbyListCallback.Result == EResult.OK)
                {
                    await ProcessLobbyList(getLobbyListCallback.Lobbies);
                }
                else
                {
                    Console.WriteLine("Steam.RefreshLobbyInfo() Failed to get lobby list ({0})", getLobbyListCallback.Result);
                    return false;
                }
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Steam.RefreshLobbyInfo() Failed to get lobby list (Timeout)");
                return false;
            }
            return true;
        }

        static void OnConnected(SteamClient.ConnectedCallback callback)
        {
            Console.WriteLine("Steam.OnConnected()");
            steamUser.LogOn(new SteamUser.LogOnDetails
            {
                Username = ENV_STEAM_USER,
                Password = ENV_STEAM_PASS,
            });
        }

        static void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            Console.WriteLine("Steam.OnLoggedOff() UserInitiated:" + callback.UserInitiated);
            loggedIn = false;
            connected = false;        
        }

        static void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            Console.WriteLine("Steam.OnLoggedOn() Result: {0} / {1}", callback.Result, callback.ExtendedResult);
            loggedIn = callback.Result == EResult.OK;
        }

        static void OnLoggedOff(SteamUser.LoggedOffCallback callback)
        {
            Console.WriteLine("Steam.OnLoggedOff() Result:" + callback.Result);
            loggedIn = false;       
        }

        static async Task ProcessLobbyList(List<SteamMatchmaking.Lobby> lobbies)
        {
            if (lobbyInfos == null)
            {
                lobbyInfos = new List<LobbyInfo>();
            }

            int lobbyCount = 0;
            foreach (var lobby in lobbies)
            {
                var lobbyInfo = ProcessLobby(lobby);
                // Skip lobbies with invalid type
                if (lobbyInfo.type < 0 || lobbyInfo.type > 3)
                {
                    continue;
                }

                int index = lobbyInfos.FindIndex(l => l.id == lobbyInfo.id);
                // If in the lobby info list, remove the old info
                if (index >= 0)
                {
                    lobbyInfos.RemoveAt(index);
                }
                else
                {
                    Console.WriteLine("ProcessLobbyList() Appeared: {0} ({1})", lobbyInfo.name, lobbyInfo.id);
                }

                // Add the new / updated lobby info to the list end
                lobbyInfos.Add(lobbyInfo);
                lobbyCount++;
            }

            // Check cached lobbies that are not in the retrieved list - these are either full or were deleted
            // These will be at the start of the list
            for (int i = lobbyInfos.Count - lobbyCount - 1; i >= 0; i--)
            {
                try
                {
                    var lobbyDataCallback = await steamMatchmaking.GetLobbyData(APPID, lobbyInfos[i].id);
                    if (lobbyDataCallback.Lobby.NumMembers > 0)
                    {
                        var lobbyInfo = ProcessLobby(lobbyDataCallback.Lobby);

                        // Update info in the list
                        lobbyInfos[i] = lobbyInfo;
                    }
                    else
                    {
                        Console.WriteLine("ProcessLobbyList() Disappeared: {0} ({1})", lobbyInfos[i].name, lobbyInfos[i].id);
                        lobbyInfos.RemoveAt(i);
                    }
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("ProcessLobbyList() Failed to get lobby data for: {0} ({1})");
                }
            }

            // Sort by lobby type, then number of players
            lobbyInfos.Sort((x, y) => 
            {
                int cmp = -(x.type == 3).CompareTo(y.type == 3); // custom games first
                if (cmp == 0)
                {
                    cmp =  -x.playerCount.CompareTo(y.playerCount); // more players first
                }
                return cmp;
            });
        }

        public static LobbyInfo ProcessLobby(SteamMatchmaking.Lobby lobby)
        {
            // New empty lobby info
            LobbyInfo lobbyInfo = new LobbyInfo()
            {
                name = "Lobby",
                id = lobby.SteamID.ConvertToUInt64(),
                playerCount = lobby.NumMembers,
                type = -1,
                matchMode = -1,
                raceProgress = -1,
                countdown = -1,
                state = -1,
                difficulty = -1,
            };

            // name
            lobby.Metadata.TryGetValue("name", out lobbyInfo.name);

            // type
            if (lobby.Metadata.TryGetValue("type", out string value) && int.TryParse(value, out lobbyInfo.type))
            {
                lobbyInfo.type -= 1549;
            }

            // Finer details
            if (lobby.Metadata.TryGetValue("lobbydata", out value))
            {
                LobbyDetails details = ProcessLobbyDetails(value);
                lobbyInfo.matchMode = details.matchMode;
                lobbyInfo.raceProgress = details.progressPercentage;
                lobbyInfo.countdown = details.countdownTime;
                lobbyInfo.state = details.timerState;
                lobbyInfo.difficulty = details.difficulty;
                lobbyInfo.playerCount = Math.Min(Math.Max(lobby.NumMembers, details.playerCount), 10);
            }

            // CloNoBump
            if (lobby.Metadata.TryGetValue("CloNoBump", out value))
            {
                lobbyInfo.mod = Mod.CloNoBumpSupercharged;
            }

            return lobbyInfo;
        }

        public static LobbyDetails ProcessLobbyDetails(string data)
        {
            byte[] bytes = Convert.FromBase64String(data);
            LobbyDetails lobbyData = new LobbyDetails() 
            {
                unknown1 = ExtractBits(bytes, 0, 1) == 1,
                usingScore = ExtractBits(bytes, 1, 1) == 1,
                lobbyType = (byte)ExtractBits(bytes, 2, 6),
                matchMode = (byte)ExtractBits(bytes, 8, 8),
                unknown2 = (byte)ExtractBits(bytes, 16, 4),
                unknown3 = (byte)ExtractBits(bytes, 20, 4),
                unknown4 = (byte)ExtractBits(bytes, 24, 3),
                timerState = (byte)ExtractBits(bytes, 27, 3),
                unknown5 = (byte)ExtractBits(bytes, 30, 8),
                unknown6 = (uint)ExtractBits(bytes, 38, 24),
                countdownTime = (byte)ExtractBits(bytes, 62, 6),
                difficulty = (byte)ExtractBits(bytes, 68, 2),
                unknown7 = (byte)ExtractBits(bytes, 70, 4),
                unknown8 = (byte)ExtractBits(bytes, 74, 4),
                progressPercentage = (byte)ExtractBits(bytes, 78, 7),
                unknown9 = (ushort)ExtractBits(bytes, 85, 16),
            };

            byte hostNameLength = (byte)ExtractBits(bytes, 101, 6);
            byte[] hostName = new byte[hostNameLength];
            for (int i = 0; i < hostNameLength; i++)
            {
                hostName[i] = (byte)ExtractBits(bytes, 107 + i * 8, 8);
            }
            lobbyData.hostName = Encoding.UTF8.GetString(hostName);
            lobbyData.unknown10 = (byte)ExtractBits(bytes, 107 + hostNameLength * 8, 4);
            lobbyData.playerCount = (byte)ExtractBits(bytes, 111 + hostNameLength * 8, 4);
            lobbyData.unknown11 = (ushort)ExtractBits(bytes, 115 + hostNameLength * 8, 16);

            return lobbyData;
        }

        public static ulong ExtractBits(byte[] bytes, int bitOffset, int bitLength)
        {
            int byteOffset = bitOffset / 8;
            int bitEnd = bitOffset + bitLength;
            int bitRemainder = bitEnd % 8;
            int byteEnd = bitEnd / 8 + (bitRemainder > 0 ? 1 : 0);
            int byteLength =  byteEnd - byteOffset;

            ulong data;
            List<byte> tmp = new List<byte>(bytes[byteOffset..byteEnd]);
            tmp.Reverse();
            if (byteLength <= 1)
            {
                data = tmp[0];
            }
            else if (byteLength <= 2)
            {
                data = BitConverter.ToUInt16(tmp.ToArray());
            }
            else if (byteLength <= 4)
            {
                if (byteLength == 3)
                {
                    tmp.Add(0);
                }
                data = BitConverter.ToUInt32(tmp.ToArray());
            }
            else
            {
                while (tmp.Count < 8)
                {
                    tmp.Add(0);
                }
                data = BitConverter.ToUInt64(tmp.ToArray());
            }

            if (bitRemainder > 0)
            {
                data >>= (8 - bitRemainder);
            }

            return data & ((1ul << bitLength) - 1);
        }

        public static LobbyInfo FindLobbyInfo(ulong id)
        {
            return Steam.lobbyInfos.Find(x => x.id == id);
        }
    }
}
