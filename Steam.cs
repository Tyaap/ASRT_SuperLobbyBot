using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using static SLB.Tools;

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
        private const int CALLBACK_WAIT = 100;
        private const int MAX_ERRORS = 10;

        // Steam client
        private static SteamClient steamClient;
        private static CallbackManager manager;
        private static SteamUser steamUser;
        private static SteamMatchmaking steamMatchmaking;
        private static SteamUserStats steamUserStats;
        private static bool loggedIn;
        private static bool connected;
        private static Timer callbackTimer;
        private static object callbackTimerLock = new object();

        // info for status message
        private static int playerCount;
        private static LobbyStats lobbyStats;
        private static List<LobbyInfo> lobbyInfos;

        // timer for updating messages
        private static Timer messageTimer;
        private static object messageTimerLock = new object();
        private static int errorCount = 0;



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
            Login();

            // Steam callback timer
            callbackTimer = new Timer(CallbackTimerTick, null, CALLBACK_WAIT, -1);

            // message update timer
            messageTimer = new Timer(MessageTimerTick, null, ENV_MESSAGE_WAIT, -1);
        }

        public static void Stop()
        {
            Console.WriteLine("Steam.Stop()");

            lock (messageTimerLock)
            {
                messageTimer?.Dispose();
                messageTimer = null;
            }

            lock (callbackTimerLock)
            {
                callbackTimer?.Dispose();
                callbackTimer = null;
            }

            Disconnect();      
            while (connected)
            {
                manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(CALLBACK_WAIT));
            }
        }

        public static LobbyInfo FindLobbyInfo(ulong id)
        {
            return Steam.lobbyInfos.Find(x => x.id == id);
        }

        private static void Login()
        {
            Console.WriteLine("Steam.Login()");
            if (!connected)
            {
                steamClient.Connect();
            }
            else if (!loggedIn)
            {
                steamUser.LogOn(new SteamUser.LogOnDetails
                {
                    Username = ENV_STEAM_USER,
                    Password = ENV_STEAM_PASS,
                });
            }
        }

        private static void Disconnect()
        {
            Console.WriteLine("steamClient.Disconnect()");
            if (loggedIn)
            {
                steamUser.LogOff();
            }
            else if (connected)
            {
                steamClient.Disconnect();
            }
        }

        private static void CallbackTimerTick(object state)
        {
            lock (callbackTimerLock)
            {
                if (callbackTimer == null)
                {
                    return; // timer disposed
                }

                try
                {
                    manager.RunWaitCallbacks();
                }
                catch(Exception ex)
                {
                    Console.WriteLine("Steam.CallbackTimerTick() Exception!\n" + ex);
                }

                // reset timer
                callbackTimer.Change(CALLBACK_WAIT, -1);
            }
        }

        private static void MessageTimerTick(object state)
        {
            lock (messageTimerLock)
            {
                if (messageTimer == null)
                {
                    return; // timer disposed
                }

                Console.WriteLine("MessageTimerTick() Discord.loggedIn:{0} Steam.loggedIn:{1}", Discord.loggedIn, loggedIn);
                try
                {
                    // web status
                    Web.message = string.Format(
                        "Discord logged in: {0}\n" +
                        "Steam logged in: {1}\n" +
                        "Super Lobby Bot is {2}",
                    Discord.loggedIn, loggedIn, Discord.loggedIn && loggedIn ? "working! :)" : "not working! :(");

                    DateTime timestamp = DateTime.UtcNow;
                    if (loggedIn && RefreshLobbyInfo().GetAwaiter().GetResult())
                    {
                        // record data
                        Stats.WriteEntryData(timestamp, lobbyInfos);

                        // get lobby stats
                        lobbyStats = Stats.ProcessEntry(timestamp, lobbyInfos);

                        errorCount = 0;
                    }
                    else
                    {
                        playerCount = -1; // indicates lobby info is unavailable
                        if (loggedIn)
                        {
                            errorCount++;
                            if (errorCount == MAX_ERRORS)
                            {
                                Console.WriteLine("MessageTimerTick() Failed to refresh {0} times.", MAX_ERRORS);
                                Console.WriteLine("steamClient.Disconnect()");
                                steamClient.Disconnect();
                                errorCount = 0;
                            }
                        }
                        else
                        {
                            Login();
                        }
                    }

                    // update status messages
                    Discord.UpdateStatus(timestamp, playerCount, lobbyInfos, lobbyStats).GetAwaiter().GetResult();
                }
                catch(Exception ex)
                {
                    Console.WriteLine("Steam.MessageTimerTick() Exception!\n" + ex);
                }

                // reset timer
                messageTimer.Change(ENV_MESSAGE_WAIT, -1);
            }
        }

        private static async Task<bool> RefreshLobbyInfo()
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

        private static void OnConnected(SteamClient.ConnectedCallback callback)
        {
            Console.WriteLine("Steam.OnConnected()");
            connected = true;
            Login();
        }

        private static void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            Console.WriteLine("Steam.OnLoggedOff() UserInitiated:" + callback.UserInitiated);
            loggedIn = false;
            connected = false;
        }

        private static void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            Console.WriteLine("Steam.OnLoggedOn() Result: {0} / {1}", callback.Result, callback.ExtendedResult);
            loggedIn = callback.Result == EResult.OK;
        }

        private static void OnLoggedOff(SteamUser.LoggedOffCallback callback)
        {
            Console.WriteLine("Steam.OnLoggedOff() Result:" + callback.Result);
            loggedIn = false;
        }

        private static async Task ProcessLobbyList(List<SteamMatchmaking.Lobby> lobbies)
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
                    // Console.WriteLine("ProcessLobbyList() Appeared: {0} ({1})", lobbyInfo.name, lobbyInfo.id);
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
                        //Console.WriteLine("ProcessLobbyList() Disappeared: {0} ({1})", lobbyInfos[i].name, lobbyInfos[i].id);
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

        private static LobbyInfo ProcessLobby(SteamMatchmaking.Lobby lobby)
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

        private static LobbyDetails ProcessLobbyDetails(string data)
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
    }
}
