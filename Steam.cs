using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
        static bool twoFactorReconnect = false;

        public static string user, pass, loginkey;

        static string authCode, twoFactorAuth;

        // linfo for status message
        static int playerCount;
        static LobbyStats lobbyStats;
        public static List<LobbyInfo> lobbyInfos;


        // timer for updating message
        static Timer messageTimer;

        // ASRT's appid
        public const int APPID = 212480;

        // wait times in milliseconds
        public static int message_wait = 10000;
        const int CALLBACK_WAIT = 100;


        public static void Run()
        {
            var cellid = 0u;
            // if we've previously connected and saved our cellid, load it
            if (File.Exists("cellid.txt"))
            {
                if (!uint.TryParse(File.ReadAllText("cellid.txt"), out cellid))
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
            messageTimer = new Timer(OnTimerTick, null, message_wait, -1);

            isRunning = true;

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
            Console.WriteLine("Connecting to Steam...");
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

            DateTime timestamp = DateTime.Now;
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
                        messageTimer.Change(message_wait, -1);
                        return;
                    }
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("Failed to get number of current players: Timeout");
                    steamClient.Disconnect(); // failing this simple request likely means we are disconnected from Steam
                    messageTimer.Change(message_wait, -1);
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
                        messageTimer.Change(message_wait, -1);
                        return;
                    }
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("Failed to get lobby list: Timeout");
                    messageTimer.Change(message_wait, -1);
                    return;
                }
            }
            else
            {
                // display disconnection message and attempt reconnection
                playerCount = -1;
                lobbyStats = new LobbyStats();
                lobbyInfos = new List<LobbyInfo>();
                SteamConnect();
            }

            // record data
            Stats.AddRecord(timestamp, lobbyInfos);

            // get lobby stats
            lobbyStats = Stats.GetLobbyStats(timestamp, lobbyInfos);

            // update status messages
            Discord.UpdateStatus(timestamp, playerCount, lobbyInfos, lobbyStats).GetAwaiter().GetResult();

            // restart the timer
            messageTimer.Change(message_wait, -1);
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
            loggedIn = false;
            Console.WriteLine("Disconnected from Steam!");
            // after recieving an AccountLogonDenied, we'll be disconnected from steam
            // so after we read an authcode from the user, we need to reconnect to begin the logon flow again
            if (twoFactorReconnect)
            {
                Console.WriteLine("Reconnecting in 5 seconds.");
                Thread.Sleep(5000);
                SteamConnect();
                twoFactorReconnect = false;
            }
        }

        static void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            bool isSteamGuard = callback.Result == EResult.AccountLogonDenied;
            bool is2FA = callback.Result == EResult.AccountLoginDeniedNeedTwoFactor;

            if (isSteamGuard || is2FA)
            {
                Console.WriteLine("This account is SteamGuard protected!");

                if (is2FA)
                {
                    twoFactorAuth = Web.InputRequest("Please enter your 2 factor auth code from your authenticator app.");
                }
                else
                {
                    authCode = Web.InputRequest(string.Format("Please enter the auth code sent to the email at {0}", callback.EmailDomain));
                }
                twoFactorReconnect = true;
                return;
            }

            if (callback.Result != EResult.OK)
            {
                Console.WriteLine("Unable to logon to Steam: {0} / {1}", callback.Result, callback.ExtendedResult);
                if (callback.Result == EResult.RateLimitExceeded)
                {
                    Console.WriteLine("Login is rate limited, waiting for 1 hour...");
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
            steamUser.SendMachineAuthResponse(new SteamUser.MachineAuthDetails
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
            });

            Console.WriteLine("Saved sentry file!");
        }

        static void OnLoginKey(SteamUser.LoginKeyCallback callback)
        {
            Console.WriteLine("Saving loginkey file...");
            File.WriteAllLines("loginkey.txt", new string[] { user, callback.LoginKey });
            steamClient.GetHandler<SteamUser>().AcceptNewLoginKey(callback);
            Console.WriteLine("Saved loginkey file!");
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
                    Console.WriteLine("New lobby: {0} ({1})", lobbyInfo.name, lobbyInfo.id);
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
                        Console.WriteLine("Deleted lobby: {0} ({1})", lobbyInfos[i].name, lobbyInfos[i].id);
                        lobbyInfos.RemoveAt(i);
                    }
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("Failed to get lobby data for: {0} ({1})");
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
    }
}
