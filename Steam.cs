using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.Discovery;

namespace SLB
{
    static class Steam
    {
        static SteamClient steamClient;
        static CallbackManager manager;

        static SteamUser steamUser;
        static SteamMatchmaking steamMatchmaking;
        static SteamUserStats steamUserStats;

        static bool isRunning;
        static bool loggedIn;

        public static string user, pass, loginkey;

        static string authCode, twoFactorAuth;

        // linfo for status message
        static int playerCount;
        static LobbyCounts lobbyCounts;
        static List<LobbyInfo> lobbyInfos;
        

        // timer for updating message
        static Timer messageTimer;


        // ASRT's appid
        public const int APPID = 212480;

        // wait times in milliseconds
        const int MESSAGE_WAIT = 10000;
        const int CALLBACK_WAIT = 100;
        const int STEAM_TIMEOUT = 20000;

        public static void Run()
        {
            var cellid = 0u;
            // if we've previously connected and saved our cellid, load it
            if (File.Exists("cellid.txt"))
            {
                if (!uint.TryParse(File.ReadAllText( "cellid.txt"), out cellid))
                {
                    Console.WriteLine("Error parsing cell id from cellid.txt. Continuing with cellid 0.");
                    cellid = 0;
                }
                else
                {
                    Console.WriteLine("Using persisted cell ID {0}", cellid);
                }
            }
            var configuration = SteamConfiguration.Create(b => b.WithCellID(cellid).WithServerListProvider(new FileStorageServerListProvider("servers_list.bin")));

            // create our steamclient instance
            steamClient = new SteamClient(configuration);
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

            // this callback is triggered when the steam servers wish for the client to store the sentry file
            manager.Subscribe<SteamUser.UpdateMachineAuthCallback>(OnMachineAuth);

            // this callback is triggered when the steam servers wish for the client to store the login key
            manager.Subscribe<SteamUser.LoginKeyCallback>(OnLoginKey);

            // message update timer
            messageTimer = new Timer(OnTimerTick, null, MESSAGE_WAIT, -1);

            isRunning = true;

            Console.WriteLine("Connecting to Steam...");
            // initiate the connection
            SteamConnect();

            while (isRunning)
            {
                // in order for the callbacks to get routed, they need to be handled by the manager
                manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(CALLBACK_WAIT));
            }
        }

        static void SteamConnect()
        {
            // if we've previously connected and saved our login key, load it
            if (File.Exists("loginkey.txt"))
            {
                string[] lines = File.ReadAllLines("loginkey.txt");
                if (lines.Length == 2)
                {
                    user = lines[0];
                    loginkey = lines[1];
                    Console.WriteLine("Using persisted login key.");
                }
                else
                {
                    Console.WriteLine("Error parsing login key from loginkey.txt.");
                }
            }
            // if tirst time, get logon details
            if (string.IsNullOrEmpty(user))
            {
                user = Web.InputRequest("Enter Steam username.");
                pass = Web.InputRequest("Enter Steam password.");
            }
            steamClient.Connect();
        }

        static async void OnTimerTick(object state)
        {
            // web status
            if (!Web.waitingForResponse) 
            {
                Web.message = string.Format(
                    "Discord logged in: {0}\n" +
                    "Steam logged in: {1}{2}",
                Discord.loggedIn, loggedIn, Discord.loggedIn && loggedIn ? "\nSuper Lobby Bot is active! :)" : "\n");
            }

            if (loggedIn)
            {
                // get number of current players
                Console.WriteLine("Getting number of current players...");
                try
                {
                    var numberOfPlayersCallback = await steamUserStats.GetNumberOfCurrentPlayers(APPID);
                    if (numberOfPlayersCallback.Result == EResult.OK)
                    {
                        Console.WriteLine("Got number of current players!");
                        playerCount = (int)numberOfPlayersCallback.NumPlayers;
                    }
                    else
                    {
                        Console.WriteLine("Failed to get number of current players: {0}", numberOfPlayersCallback.Result);
                        messageTimer.Change(MESSAGE_WAIT, -1);
                        return;
                    }
                }
                catch(TaskCanceledException)
                {
                    Console.WriteLine("Failed to get number of current players: Timeout");
                    messageTimer.Change(MESSAGE_WAIT, -1);
                    return;
                }

                // get lobby list
                Console.WriteLine("Getting lobby list...");
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
                        Console.WriteLine("Got lobby list!");
                        await ProcessLobbyList(getLobbyListCallback.Lobbies);
                    }
                    else
                    {
                        Console.WriteLine("Failed to get lobby list: {0}", getLobbyListCallback.Result);
                        messageTimer.Change(MESSAGE_WAIT, -1);
                        return;
                    }
                }
                catch(TaskCanceledException)
                {
                    Console.WriteLine("Failed to get lobby list: Timeout");
                    messageTimer.Change(MESSAGE_WAIT, -1);
                    return;
                }
            }
            else
            {
                playerCount = -1;
                lobbyCounts = new LobbyCounts();
                lobbyInfos = new List<LobbyInfo>();
            }

            // send discord messages
            if (Discord.loggedIn)
            {
                Discord.UpdateStatus(playerCount, lobbyCounts, lobbyInfos).GetAwaiter().GetResult();
            }

            // restart the timer
            messageTimer.Change(MESSAGE_WAIT, -1);
        }

        static void OnConnected(SteamClient.ConnectedCallback callback)
        {
            Console.WriteLine("Connected to Steam!");
            byte[] sentryHash = null;
            // if we have a saved sentry file, read and sha-1 hash it
            if (File.Exists("sentry.bin"))
            {
                byte[] sentryFile = File.ReadAllBytes("sentry.bin");
                sentryHash = CryptoHelper.SHAHash(sentryFile);
                Console.WriteLine("Using persisted sentry file.");
            }

            Console.WriteLine("Logging '{0}' into Steam...", user);
            steamUser.LogOn(new SteamUser.LogOnDetails
            {
                Username = user,
                Password = pass,

                // we pass the login key to skip password entry
                // this value will be null (which is the default) for our first logon attempt
                LoginKey = loginkey,
                ShouldRememberPassword = true,
                
                // we pass in an additional authcode
                // this value will be null (which is the default) for our first logon attempt
                AuthCode = authCode,

                // if the account is using 2-factor auth, we'll provide the two factor code instead
                // this will also be null on our first logon attempt
                TwoFactorCode = twoFactorAuth,

                // our subsequent logons use the hash of the sentry file as proof of ownership of the file
                // this will also be null for our first (no authcode) and second (authcode only) logon attempts
                SentryFileHash = sentryHash,
            });
        }

        static void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            // after recieving an AccountLogonDenied, we'll be disconnected from steam
            // so after we read an authcode from the user, we need to reconnect to begin the logon flow again

            Console.WriteLine("Disconnected from Steam, reconnecting in 5...");

            loggedIn = false;
            Thread.Sleep(5000);

            SteamConnect();
        }

        static void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            bool isSteamGuard = callback.Result == EResult.AccountLogonDenied;
            bool is2FA = callback.Result == EResult.AccountLoginDeniedNeedTwoFactor;

            if (isSteamGuard || is2FA)
            {
                Console.WriteLine("This account is SteamGuard protected!");

                if ( is2FA )
                {
                    twoFactorAuth = Web.InputRequest("Please enter your 2 factor auth code from your authenticator app.");
                }
                else
                {
                    authCode = Web.InputRequest(string.Format("Please enter the auth code sent to the email at {0}", callback.EmailDomain));
                }
                return;
            }

            if (callback.Result != EResult.OK)
            {
                Console.WriteLine("Unable to logon to Steam: {0} / {1}", callback.Result, callback.ExtendedResult);
                if (callback.Result == EResult.InvalidPassword)
                {
                    user = null;
                    pass = null;
                    loginkey = null;
                    if (File.Exists("loginkey.txt"))
                    {
                        File.Delete("loginkey.txt");
                    }
                }
                if (callback.Result == EResult.RateLimitExceeded)
                {
                    Console.WriteLine("Waiting for 1 hour...");
                    Thread.Sleep(TimeSpan.FromHours(1));
                }
                return;
            }
            loggedIn = true;
            Console.WriteLine("Logged into Steam!");
            
            // save the current cellid somewhere. if we lose our saved server list, we can use this when retrieving
            // servers from the Steam Directory.
            Console.WriteLine("Saving celliid file...");
            File.WriteAllText("cellid.txt", callback.CellID.ToString());
            Console.WriteLine("Saved celliid file!");
        }

        static void OnLoggedOff(SteamUser.LoggedOffCallback callback)
        {
            loggedIn = false;
            Console.WriteLine("Logged off of Steam: {0}", callback.Result);
        }

        static void OnMachineAuth(SteamUser.UpdateMachineAuthCallback callback)
        {
            Console.WriteLine("Saving sentry file...");

            // write out our sentry file
            // ideally we'd want to write to the filename specified in the callback
            // but then this sample would require more code to find the correct sentry file to read during logon
            // for the sake of simplicity, we'll just use "sentry.bin"

            int fileSize;
            byte[] sentryHash;
            using (var fs = File.Open("sentry.bin", FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                fs.Seek(callback.Offset, SeekOrigin.Begin);
                fs.Write(callback.Data, 0, callback.BytesToWrite);
                fileSize = (int)fs.Length;

                fs.Seek(0, SeekOrigin.Begin);
                using (var sha = SHA1.Create())
                {
                    sentryHash = sha.ComputeHash(fs);
                }
            }

            // inform the steam servers that we're accepting this sentry file
            steamUser.SendMachineAuthResponse( new SteamUser.MachineAuthDetails
            {
                JobID = callback.JobID,

                FileName = callback.FileName,

                BytesWritten = callback.BytesToWrite,
                FileSize = fileSize,
                Offset = callback.Offset,

                Result = EResult.OK,
                LastError = 0,

                OneTimePassword = callback.OneTimePassword,

                SentryFileHash = sentryHash,
            } );

            Console.WriteLine("Saved sentry file!");
        }

        static void OnLoginKey(SteamUser.LoginKeyCallback callback)
        {
            Console.WriteLine("Saving loginkey file...");
            File.WriteAllLines("loginkey.txt", new string[] {user, callback.LoginKey});
            steamClient.GetHandler<SteamUser>().AcceptNewLoginKey(callback);
            Console.WriteLine("Saved loginkey file!");
        }

        static async Task ProcessLobbyList(List<SteamMatchmaking.Lobby> lobbies) 
        {
            lobbyCounts = new LobbyCounts();
            if (lobbyInfos == null)
            {
                lobbyInfos = new List<LobbyInfo>();
            }

            foreach (var lobby in lobbies)
            {
                var lobbyInfo = ProcessLobby(lobby);
                // Skip lobbies with invalid type
                if (lobbyInfo.type < 0 || lobbyInfo.type > 3)
                {
                    continue;
                }

                int index = lobbyInfos.FindIndex(l => l.id == lobbyInfo.id);
                // If in the lobby info list, we remove the old info
                if (index >= 0)
                {
                    lobbyInfos.RemoveAt(index);
                }
                else
                {
                    Console.WriteLine("New lobby: {0} ({1})", lobbyInfo.name, lobbyInfo.id);
                }

                if (lobbyInfo.type == 3) 
                {
                    lobbyCounts.customGamePlayers += lobbyInfo.playerCount;
                    lobbyCounts.customGameLobbies ++;
                }
                else
                {
                    lobbyCounts.matchmakingPlayers += lobbyInfo.playerCount;
                    lobbyCounts.matchmakingLobbies ++;
                }

                // Add the new / updated lobby info to the list end
                lobbyInfos.Add(lobbyInfo);  
            }

            // Check cached lobbies that are not in the retrieved list - these are either full or were deleted
            // These will be at the start of the list
            for (int i = lobbyInfos.Count - lobbyCounts.matchmakingLobbies - lobbyCounts.customGameLobbies - 1; i >= 0; i--)
            {
                try
                {
                    var lobbyDataCallback = await steamMatchmaking.GetLobbyData(APPID, lobbyInfos[i].id);
                    if (lobbyDataCallback.Lobby.NumMembers > 0)
                    {
                        var lobbyInfo = ProcessLobby(lobbyDataCallback.Lobby);
                        if (lobbyInfo.type == 3) 
                        {
                            lobbyCounts.customGamePlayers += lobbyInfo.playerCount;
                            lobbyCounts.customGameLobbies ++;
                        }
                        else
                        {
                            lobbyCounts.matchmakingPlayers += lobbyInfo.playerCount;
                            lobbyCounts.matchmakingLobbies ++;
                        }

                        // Update info in the list
                        lobbyInfos[i] = lobbyInfo;
                    }
                    else
                    {
                        Console.WriteLine("Deleted lobby: {0} ({1})", lobbyInfos[i].name, lobbyInfos[i].id);
                        lobbyInfos.RemoveAt(i);
                    }
                }
                catch(TaskCanceledException)
                {
                    Console.WriteLine("Failed to get lobby data for: {0} ({1})");
                }       
            }

            // Sort by number of players
            lobbyInfos.Sort((x,y) => -x.playerCount.CompareTo(y.playerCount));
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
                    byte[] data = Convert.FromBase64String(value);
                    lobbyInfo.matchMode = ExtractByte(data, 8);
                    lobbyInfo.raceProgress = ExtractByte(data, 77);
                    lobbyInfo.countdown = ExtractByte(data, 60) & 63;
                    lobbyInfo.state = ExtractByte(data, 22) & 3;
                    lobbyInfo.difficulty = ExtractByte(data, 62) & 3;
                    lobbyInfo.playerCount = Math.Max(lobby.NumMembers, ExtractByte(data, data.Length*8 - 29) & 15);
                }

                return lobbyInfo;
        }

        public static byte ExtractByte(byte[] bytes, int bitOffset)
        {
            int shortOffset = bitOffset / 8;
            short data = BitConverter.ToInt16(new byte[] { bytes[shortOffset + 1], bytes[shortOffset] }, 0);
            return (byte)(data >> (8 - bitOffset % 8));
        }
    }
}
