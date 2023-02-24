using System;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord.Net;
using Discord.Rest;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using static SLB.Tools;

namespace SLB
{
    static class Discord
    {
        // environment variables
        private static int ENV_MESSAGE_COUNT => int.Parse(Environment.GetEnvironmentVariable("MESSAGE_COUNT"));

        // constants
        private const bool SHOW_CUSTOM_GAMES = false;
        private const bool FULL_LOBBY_JOINABLE = true;
        private static readonly Color LOBBY_COLOUR = Color.Gold;
        private const int BEST_TIMES_COUNT = 4;

        // Discord client
        private static DiscordSocketClient discordSocketClient;
        private static CommandService commandService;
        private static IServiceProvider serviceProvider;

        public static bool connected;
        public static bool loggedIn;

        // status messages
        private static DateTime channelUpdateTime;
        private static Dictionary<ulong, Tuple<ITextChannel, List<IUserMessage>>> currentStatusMessages;
        private static int lastMessageCount;
        private static HashSet<ulong> visibleCustomGames = new HashSet<ulong>();


        public static async Task Start()
        {
            Console.WriteLine("Discord.Start()");
            discordSocketClient = new DiscordSocketClient();
            commandService = new CommandService();
            serviceProvider = new ServiceCollection()
                .AddSingleton(discordSocketClient)
                .AddSingleton(commandService)
                .BuildServiceProvider();

            currentStatusMessages = new Dictionary<ulong, Tuple<ITextChannel, List<IUserMessage>>>();

            discordSocketClient.Log += Log;
            discordSocketClient.LoggedIn += LoggedIn;
            discordSocketClient.LoggedOut += LoggedOut;
            discordSocketClient.MessageReceived += MessageRecieved;
            discordSocketClient.Disconnected += Disconnected;
            discordSocketClient.Connected += Connected;
            await RegisterCommandsAsync();
            await LoginAsync();
        }

        public static void Stop()
        {
            Console.WriteLine("Discord.Stop()");
            discordSocketClient?.Dispose();
            discordSocketClient = null;
        }       

        public static async Task UpdateStatus(DateTime timestamp, int playerCount, List<LobbyInfo> lobbyInfos, LobbyStats lobbyStats)
        {
            // check if any lobbies need hiding
            if (!SHOW_CUSTOM_GAMES)
            {
                var visibleCustomGamesNew = new HashSet<ulong>();
                foreach(var lobbyInfo in lobbyInfos)
                {
                    if (lobbyInfo.type != 3)
                    {
                        continue; // don't hide matchmaking lobbies
                    }
                    if (visibleCustomGames.Contains(lobbyInfo.id))
                    {
                        visibleCustomGamesNew.Add(lobbyInfo.id);
                    }
                    else
                    {
                        lobbyInfo.hidden = true;
                    }
                }
                visibleCustomGames = visibleCustomGamesNew;
            }
            
            if (!loggedIn)
            {
                await LoginAsync();
                if (!loggedIn)
                {
                    Console.WriteLine("Discord.UpdateStatus() Not logged in, skipping update.");
                    return;
                }
            }

            // Message storage
            var messages = new List<string>();
            var embeds = new List<Embed>();

            // Overview message
            if (playerCount >= 0)
            {
                string statusOverview = string.Format("**__S&ASRT Lobby Info — <t:{0}:d> <t:{0}:T>__**", DatetimeToUnixTime(timestamp));
                statusOverview += string.Format("\n\n**{0}** people are playing S&ASRT.", playerCount);
                statusOverview += "\n" + LobbyCountMessage(lobbyStats.MMLobbies, lobbyStats.MMPlayers, "matchmaking", "lobby", "lobbies");
                statusOverview += "\n" + LobbyCountMessage(lobbyStats.CGLobbies, lobbyStats.CGPlayers, "custom", "game", "games");
                statusOverview += "\n";
                foreach (var lobbyInfo in lobbyInfos)
                {
                    if (lobbyInfo.state != -1 && !lobbyInfo.hidden && (lobbyInfo.playerCount != 10 || FULL_LOBBY_JOINABLE))
                    {
                        statusOverview += "\n**Open the game and click a link below to join!**";
                        break;
                    }
                }
                statusOverview += "\n** **";
                messages.Add(statusOverview);
            }
            else
            {
                string message = "**Lobby info unavailable!\n**";
                if (DateTime.Now.DayOfWeek == DayOfWeek.Tuesday || DateTime.Now.DayOfWeek == DayOfWeek.Wednesday)
                {
                    message += "**Steam might be down for scheduled maintenance.**";
                }
                messages.Add(message);
            }

            // Overview message - matchmaking stats
            EmbedBuilder builder = new EmbedBuilder();
            builder.WithColor(LOBBY_COLOUR);
            builder.WithTitle("Matchmaking stats");
            builder.WithDescription(string.Format("Since <t:{0}:d> <t:{0}:t>", DatetimeToUnixTime(lobbyStats.StartDate)));

            var bestTimes = lobbyStats.MMBestTimes;
            var nextOccurances = new List<(long, StatsPoint2)>();

            foreach (var bestTime in bestTimes)
            {
                if (bestTime.Ref == -1)
                {
                    continue;
                }
                nextOccurances.Add((
                    DatetimeToUnixTime(NextOccurance(timestamp, bestTime.Ref * Stats.BIN_WIDTH + Stats.INTERVAL)), bestTime));
            }
            nextOccurances.Sort((x, y) => x.Item1.CompareTo(y.Item1));

            string dateList = "";
            string expList = "";

            for(int i = 0; i < BEST_TIMES_COUNT & i < nextOccurances.Count; i++)
            {
                if (i > 0)
                {
                    dateList += "\n";
                    expList += "\n";
                }
                var nextOccurance = nextOccurances[i];
                dateList += string.Format("<t:{0}:F> - <t:{1}:t>", nextOccurance.Item1 - Stats.INTERVAL, nextOccurance.Item1);
                expList += string.Format("{0:0}-{1:0} (mean {2:0.#})", 
                    Math.Floor(nextOccurance.Item2.Min),
                    Math.Ceiling(nextOccurance.Item2.Max),
                    nextOccurance.Item2.Avg);
            }

            builder.AddField("Best times to play", dateList, inline: true);
            builder.AddField("Players (predicted)", expList, inline: true);
            builder.AddField("Most players ever", string.Format("<t:{0}:d> <t:{0}:t> — {1} Players", DatetimeToUnixTime(lobbyStats.MMAllTimeBestDate), lobbyStats.MMAllTimeBestPlayers));
            embeds.Add(builder.Build());

            // Lobby messages
            if (playerCount >= 0)
            {
                foreach (var lobbyInfo in lobbyInfos)
                {
                    if (lobbyInfo.hidden)
                    {
                        continue; // hide this lobby
                    }

                    builder = new EmbedBuilder();
                    builder.WithColor(LOBBY_COLOUR);

                    // title
                    builder.WithTitle(lobbyInfo.name);

                    // description
                    if (lobbyInfo.state == -1)
                    {
                        builder.WithDescription("Lobby initialising...");
                    }
                    else if (lobbyInfo.playerCount == 10 && !FULL_LOBBY_JOINABLE)
                    {
                        builder.WithDescription("Lobby is full!");
                    }
                    else
                    {
                        string desription = string.Format("steam://joinlobby/{0}/{1}", Steam.APPID, lobbyInfo.id);
                        if (lobbyInfo.mod == Mod.CloNoBumpSupercharged)
                        {
                            desription += "\nMod Required: https://github.com/Tyaap/ASRT_CloNoBump_Supercharged/releases";
                        }
                        builder.WithDescription(desription);
                    }

                    // fields
                    builder.AddField("Players", lobbyInfo.playerCount + "/10", true);
                    builder.AddField("Type", LobbyTools.GetLobbyType(lobbyInfo.type), true);
                    if (lobbyInfo.state >= 0)
                    {
                        int eventId = LobbyTools.GetEventId(lobbyInfo.type, lobbyInfo.matchMode);
                        (int mapId, bool mirror) = LobbyTools.GetMapId(lobbyInfo.type, lobbyInfo.matchMode);
                        builder.AddField("Activity", LobbyTools.GetActivity(lobbyInfo.state, eventId, lobbyInfo.raceProgress, lobbyInfo.countdown), true);
                        builder.AddField("Event", LobbyTools.GetEventName(eventId), true);
                        builder.AddField(LobbyTools.GetMapType(eventId), LobbyTools.GetMapName(eventId, mapId, mirror), true);
                        if (lobbyInfo.type == 3)
                        {
                            builder.AddField("Difficulty", LobbyTools.GetDifficulty(lobbyInfo.type, lobbyInfo.difficulty), true);
                        }
                        else
                        {
                            builder.AddField("\u200B", "\u200B", true);
                        }
                    }

                    messages.Add("");
                    embeds.Add(builder.Build());
                }
            }
            
            // get guilds
            IReadOnlyCollection<RestGuild> guilds = null;
            try
            {
                guilds = await discordSocketClient.Rest.GetGuildsAsync();
            }
            catch (HttpException ex)
            {
                Console.WriteLine("Discord.UpdateStatus() Guild list retrieval failed!");
                UpdateStatusError(ex);
                return;
            }

            // process each guild
            foreach (var guild in guilds)
            {
                // set up the status channel
                bool newChannel = !currentStatusMessages.TryGetValue(guild.Id, out var channelMessagePair);
                if (newChannel)
                {
                    Console.WriteLine("Discord.UpdateStatus() Setting up status channel on {0} ({1})", guild.Name, guild.Id);
                    try
                    {
                        // look for old status channels
                        RestTextChannel statusChannel = null;
                        foreach (var channel in await guild.GetTextChannelsAsync())
                        {
                            if (channel.Name.EndsWith("-in-matchmaking"))
                            {
                                statusChannel = channel;
                                break;
                            }
                        }

                        var statusMessages = new List<IUserMessage>();
                        // reuse old messages if channel exists
                        if (statusChannel != null)
                        {
                            await foreach (var discordMessages in statusChannel.GetMessagesAsync())
                            {
                                foreach (IUserMessage discordMessage in discordMessages)
                                {
                                    if (discordMessage.Author.Id == discordSocketClient.CurrentUser.Id)
                                    {
                                        statusMessages.Add(discordMessage);
                                    }
                                }
                            }
                            // ensure the status messages are ordered correctly
                            statusMessages.Sort((x, y) => x.Timestamp.CompareTo(y.Timestamp));

                            // if there are sufficient messages, assume they are being displayed as one message.
                            // insufficient messages, start from scratch to ensure messages displayed as one.
                            if (statusMessages.Count < ENV_MESSAGE_COUNT)
                            {
                                Console.WriteLine("Discord.UpdateStatus() Insufficient messages, starting from scratch...");
                                await statusChannel.DeleteMessagesAsync(statusMessages);
                                statusMessages.Clear();
                            }
                        }
                        // create status channel if not found
                        else
                        {
                            statusChannel = await guild.CreateTextChannelAsync("xx-in-matchmaking");
                        }
                        // store channel/message pair
                        channelMessagePair = new Tuple<ITextChannel, List<IUserMessage>>(statusChannel, statusMessages);
                        currentStatusMessages.Add(guild.Id, channelMessagePair);
                    }
                    catch (HttpException ex)
                    {
                        Console.WriteLine("Discord.UpdateStatus() Status channel setup failed!");
                        UpdateStatusError(ex, guild.Id);
                        continue;
                    }
                }

                // set channel name
                try
                {
                    string name = (lobbyStats.MMPlayers >= 0 ? lobbyStats.MMPlayers.ToString() : "xx") + "-in-matchmaking";
                    // only update if the channel name has changed and it has been at least 5 minutes since the last update (Discord rate limit)
                    if (DateTime.Now.Subtract(channelUpdateTime).TotalMinutes >= 5 && !channelMessagePair.Item1.Name.Equals(name))
                    {
                        await channelMessagePair.Item1.ModifyAsync(c => { c.Name = name; });
                        channelUpdateTime = DateTime.Now;
                    }
                }
                catch (HttpException ex)
                {
                    Console.WriteLine("Discord.UpdateStatus() Failed to set status channel name on server {0} ({1})", guild.Name, guild.Id);
                    UpdateStatusError(ex, guild.Id);
                    continue;
                }

                // send/update messages
                // case true: ensure new channel has messages allocated
                // case false: update/send the appropriate subset of messages
                int count = newChannel ? ENV_MESSAGE_COUNT : Math.Max(messages.Count, lastMessageCount);
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        string message = i < messages.Count ? messages[i] : "** **";
                        Embed embed = i < embeds.Count ? embeds[i] : null;

                        if (i < channelMessagePair.Item2.Count)
                        {
                            if (!channelMessagePair.Item2[i].Content.Equals(message) || string.IsNullOrEmpty(message)) // Empty message -> assume it is an embed, so update it
                            {
                                await channelMessagePair.Item2[i].ModifyAsync(m => { m.Content = message; m.Embed = embed; });
                            }
                        }
                        else
                        {
                            // handle overflow by sending new messages
                            channelMessagePair.Item2.Add(await channelMessagePair.Item1.SendMessageAsync(message, embed: embed));
                        }
                        System.Threading.Thread.Sleep(500); // 0.5 second delay between messages
                    }
                }
                catch (HttpException ex)
                {
                    Console.WriteLine("Discord.UpdateStatus() Failed to send/update a message to server {0} ({1})", guild.Name, guild.Id);
                    UpdateStatusError(ex, guild.Id);
                    continue;
                }

                // delete excess messages once the message count falls below the target message allocation
                if (messages.Count <= ENV_MESSAGE_COUNT && ENV_MESSAGE_COUNT < channelMessagePair.Item2.Count)
                {
                    Console.WriteLine("Discord.UpdateStatus() Deleting excess messages...");
                    await channelMessagePair.Item1.DeleteMessagesAsync(channelMessagePair.Item2.GetRange(ENV_MESSAGE_COUNT, channelMessagePair.Item2.Count - ENV_MESSAGE_COUNT));
                    channelMessagePair.Item2.RemoveRange(ENV_MESSAGE_COUNT, channelMessagePair.Item2.Count - ENV_MESSAGE_COUNT);
                }
            }

            lastMessageCount = messages.Count;
        }

        private static void UpdateStatusError(HttpException ex, ulong guildId = 0)
        {      
            switch (ex.DiscordCode)
            {
                case DiscordErrorCode.UnknownChannel:
                    Console.WriteLine("Discord.UpdateStatusError() Channel not found!");
                    currentStatusMessages.Remove(guildId);
                    return;
                case DiscordErrorCode.MissingPermissions:
                    Console.WriteLine("Discord.UpdateStatusError() Bot does not have permission!");
                    currentStatusMessages.Remove(guildId);
                    return;
                default:
                    Console.WriteLine("Discord.UpdateStatusError() " + ex);
                    return;
            }
        }

        private static string LobbyCountMessage(int lobbyCount, int playerCount, string lobbyType, string lobbySingle, string lobbyPlural)
        {
            if (playerCount == 0)
            {
                return string.Format("There are **no players** in {0}.", lobbyType);
            }
            else if (playerCount == 1)
            {
                return string.Format("**1** player is in a {0} {1}.", lobbyType, lobbySingle);
            }
            else if (lobbyCount == 1)
            {
                return string.Format("**{0}** players are in a {1} {2}.", playerCount, lobbyType, lobbySingle);
            }
            else
            {
                return string.Format("**{0}** players are in **{1}** {2} {3}.", playerCount, lobbyCount, lobbyType, lobbyPlural);
            }
        }

        private static async Task MessageRecieved(SocketMessage message)
        {
            try
            {
                Match m = Regex.Match(message.Content, @"steam:\/\/joinlobby\/212480\/[0-9]+");
                if (m.Success)
                {
                    Console.WriteLine("Discord.OnMessageRecieved() Got lobby link in message! " + message.Content.Substring(m.Index, m.Length));
                    ulong id = ulong.Parse(message.Content.Substring(m.Index + 25, m.Length - 25));
                    LobbyInfo lobbyInfo = Steam.FindLobbyInfo(id);
                    if (lobbyInfo == null || lobbyInfo.type != 3)
                    {
                        return; // lobby does not exist or is already visible
                    }
                    visibleCustomGames.Add(id);
                    if(currentStatusMessages.TryGetValue((message.Channel as SocketGuildChannel).Guild.Id, out var channelMessagePair))
                    {
                        await message.Channel.SendMessageAsync("Added " + lobbyInfo.name + " to " + channelMessagePair.Item1.Mention + "!");
                    }         
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine("Discord.OnMessageRecieved() Exception!\n" + ex);
            }
        } 

        private static async Task LoginAsync()
        {
            Console.WriteLine("Discord.LoginAsync()");
            try
            {
                await discordSocketClient.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("DISCORD_TOKEN"));
                await discordSocketClient.StartAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Discord.LoginAsync() Exception!\n" + ex);
            }
        }

        private static Task Log(LogMessage arg)
        {
            Console.WriteLine("Discord " + arg);
            return Task.CompletedTask;
        }

        private static Task LoggedIn()
        {
            Console.WriteLine("Discord.LoggedIn()");
            loggedIn = true;
            return Task.CompletedTask;
        }

        private static Task LoggedOut()
        {
            Console.WriteLine("Discord.LoggedOut()");
            loggedIn = false;
            return Task.CompletedTask;
        }

        private static Task Connected()
        {
            Console.WriteLine("Discord.Connected()");
            connected = true;
            return Task.CompletedTask;
        }

        private static Task Disconnected(Exception ex)
        {
            Console.WriteLine("Discord.Disconnected() Exception:\n" + ex);
            connected = false;
            loggedIn = false;
            return Task.CompletedTask;
        }

        private static async Task RegisterCommandsAsync()
        {
            Console.WriteLine("Discord.RegisterCommandsAsync()");
            discordSocketClient.MessageReceived += HandleCommandAsync;
            await commandService.AddModulesAsync(Assembly.GetEntryAssembly(), serviceProvider);
        }

        private static async Task HandleCommandAsync(SocketMessage arg)
        {
            Console.WriteLine("Discord.HandleCommandAsync()");
            var message = arg as SocketUserMessage;
            var context = new SocketCommandContext(discordSocketClient, message);
            if (message.Author.IsBot) return;

            int argPos = 0;
            if (message.HasStringPrefix("!", ref argPos))
            {
                var result = await commandService.ExecuteAsync(context, argPos, serviceProvider);
                if (!result.IsSuccess) Console.WriteLine(result.ErrorReason);
            }
        }
    }
}
