using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord.Net;
using Discord.Rest;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;

namespace SLB
{
    static class Discord
    {
        private static DiscordSocketClient discordSocketClient;
        private static CommandService commandService;
        private static IServiceProvider serviceProvider;
        public static string token;
        public static bool loggedIn;
        private static Dictionary<ulong, Tuple<ITextChannel, List<IUserMessage>>> currentStatusMessages;
        private static int lastMessageCount;
        private static DateTime channelUpdateTime;

        private const string CLOCK_FORMAT = "dd/MM/yy HH:mm:ss";
        private static readonly Color LOBBY_COLOUR = Color.Gold;
        private const int ALLOCATED_MESSAGES = 6;
        private const bool SHOW_CUSTOM_GAMES = false;
        private const bool FULL_LOBBY_JOINABLE = true;


        public static void Run()
        {
            discordSocketClient = new DiscordSocketClient(new DiscordSocketConfig { ExclusiveBulkDelete = true });
            commandService = new CommandService();
            serviceProvider = new ServiceCollection()
                .AddSingleton(discordSocketClient)
                .AddSingleton(commandService)
                .BuildServiceProvider();

            currentStatusMessages = new Dictionary<ulong, Tuple<ITextChannel, List<IUserMessage>>>();

            if (File.Exists("token.txt"))
            {
                token = File.ReadAllText("token.txt");
                Console.WriteLine("Using persisted bot token.");
            }
            if (string.IsNullOrEmpty(token))
            {
                token = Web.InputRequest("Enter bot token.");
            }

            discordSocketClient.Log += DiscordSocketClient_Log;
            discordSocketClient.LoggedIn += DiscordSocketClient_LoggedIn;
            discordSocketClient.LoggedOut += DiscordSocketClient_LoggedOut;
            RegisterCommandsAsync().GetAwaiter().GetResult();
            LoginAsync().GetAwaiter().GetResult();
        }

        public static async Task UpdateStatus(int playerCount, LobbyCounts lobbyCounts, List<LobbyInfo> lobbyInfos)
        {
            if (!loggedIn)
            {
                await LoginAsync();
                if (!loggedIn)
                {
                    Console.WriteLine("Skipping Discord status message update.");
                    return;
                }
            }

            Console.WriteLine("Updating Discord status messages...");

            // Message storage
            List<string> messages = new List<string>();
            List<Embed> embeds = new List<Embed>();

            // Overview message
            if (playerCount >= 0)
            {
                string statusOverview = string.Format("**__S&ASRT Lobbies — {0} GMT__**", DateTime.Now.ToString(CLOCK_FORMAT));
                statusOverview += string.Format("\n\n**{0}** people are playing S&ASRT.", playerCount);
                statusOverview += "\n" + LobbyCountMessage(lobbyCounts.matchmakingLobbies, lobbyCounts.matchmakingPlayers, "matchmaking");
                statusOverview += "\n" + LobbyCountMessage(lobbyCounts.customGameLobbies, lobbyCounts.customGamePlayers, "custom game");
                statusOverview += "\n";
                foreach (var lobbyInfo in lobbyInfos)
                {
                    if (lobbyInfo.state != -1 && lobbyInfo.type != 3 && (lobbyInfo.playerCount != 10 || FULL_LOBBY_JOINABLE))
                    {
                        statusOverview += "\n**Open the game and click a link below to join!**";
                        break;
                    }
                }
                messages.Add(statusOverview);
                embeds.Add(null);
            }
            else
            {
                string message = "**Not connected to Steam!**";
                if (DateTime.Now.DayOfWeek == DayOfWeek.Tuesday || DateTime.Now.DayOfWeek == DayOfWeek.Wednesday)
                {
                    message += "\n**Steam is likely down for normal Tuesday maintenance.**";
                }
                messages.Add(message);
                embeds.Add(null);
            }

            // Lobby messages
            foreach (var lobbyInfo in lobbyInfos)
            {
                // Skip displaying custom lobbies
                if (lobbyInfo.type == 3 && !SHOW_CUSTOM_GAMES)
                {
                    continue;
                }

                EmbedBuilder builder = new EmbedBuilder();
                builder.WithColor(LOBBY_COLOUR);

                // title
                builder.WithTitle(lobbyInfo.name);

                // description
                if (lobbyInfo.state == -1)
                {
                    builder.WithDescription("Lobby initialising...");
                }
                else if (lobbyInfo.type == 3)
                {
                    builder.WithDescription("Private lobby");
                }
                else if (lobbyInfo.playerCount == 10 && !FULL_LOBBY_JOINABLE)
                {
                    builder.WithDescription("Lobby is full!");
                }
                else
                {
                    builder.WithDescription(string.Format("steam://joinlobby/{0}/{1}", Steam.APPID, lobbyInfo.id));
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

            // get guilds
            IReadOnlyCollection<RestGuild> guilds = null;
            try
            {
                Console.WriteLine("discordSocketClient.Rest.GetGuildsAsync()");
                guilds = await discordSocketClient.Rest.GetGuildsAsync();
            }
            catch (Exception e)
            {
                Console.WriteLine("Guild list retrieval failed!");
                Console.WriteLine(e);
                return;
            }

            // process each guild
            foreach (var guild in guilds)
            {
                // set up the status channel
                bool newChannel = !currentStatusMessages.TryGetValue(guild.Id, out var channelMessagePair);
                if (newChannel)
                {
                    Console.WriteLine("Setting up status channel on {0} ({1})...", guild.Name, guild.Id);
                    try
                    {
                        // look for old status channels
                        Console.WriteLine("guild.GetTextChannelsAsync()");
                        var textChannels = await guild.GetTextChannelsAsync();
                        RestTextChannel statusChannel = null;
                        foreach (var channel in textChannels)
                        {
                            if (channel.Name.EndsWith("-in-matchmaking"))
                            {
                                statusChannel = channel;
                                break;
                            }
                        }

                        List<IUserMessage> statusMessages = new List<IUserMessage>();
                        // reuse old messages if channel exists
                        if (statusChannel != null)
                        {
                            Console.WriteLine("statusChannel.GetMessagesAsync()");
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
                            if (statusMessages.Count < ALLOCATED_MESSAGES)
                            {
                                Console.WriteLine("statusChannel.DeleteMessagesAsync(statusMessages)");
                                await statusChannel.DeleteMessagesAsync(statusMessages);
                                statusMessages.Clear();
                            }
                        }
                        // create status channel if not found
                        else
                        {
                            Console.WriteLine("guild.CreateTextChannelAsync(xx-in-matchmaking)");
                            statusChannel = await guild.CreateTextChannelAsync("xx-in-matchmaking");
                        }
                        // store channel/message pair
                        channelMessagePair = new Tuple<ITextChannel, List<IUserMessage>>(statusChannel, statusMessages);
                        currentStatusMessages.Add(guild.Id, channelMessagePair);
                        Console.WriteLine("Status channel setup complete!");
                    }
                    catch (HttpException e)
                    {
                        Console.WriteLine("Status channel setup failed!");
                        UpdateStatusError(guild.Id, e);
                        continue;
                    }
                }

                // set channel name
                try
                {
                    string name = (lobbyCounts.matchmakingPlayers >= 0 ? lobbyCounts.matchmakingPlayers.ToString() : "xx") + "-in-matchmaking";
                    // only update if the channel name has changed and it has been at least 5 minutes since the last update (Discord rate limit)
                    if (DateTime.Now.Subtract(channelUpdateTime).TotalMinutes >= 5 && !channelMessagePair.Item1.Name.Equals(name))
                    {
                        await channelMessagePair.Item1.ModifyAsync(c => { c.Name = name; });
                        channelUpdateTime = DateTime.Now;
                    }
                }
                catch (HttpException e)
                {
                    Console.WriteLine("Failed to set status channel name on server {0} ({1})", guild.Name, guild.Id);
                    UpdateStatusError(guild.Id, e);
                    continue;
                }

                // send/update messages
                // case true: ensure new channel has messages allocated
                // case false: update/send the appropriate subset of messages
                int count = newChannel ? ALLOCATED_MESSAGES : Math.Max(messages.Count, lastMessageCount);
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
                                Console.WriteLine("channelMessagePair.Item2[i].ModifyAsync(m => { m.Content = message; m.Embed = embed; })");
                                await channelMessagePair.Item2[i].ModifyAsync(m => { m.Content = message; m.Embed = embed; });
                            }
                        }
                        else
                        {
                            // handle overflow by sending new messages
                            Console.WriteLine("channelMessagePair.Item2.Add(await channelMessagePair.Item1.SendMessageAsync(message, embed: embed))");
                            channelMessagePair.Item2.Add(await channelMessagePair.Item1.SendMessageAsync(message, embed: embed));
                        }
                        System.Threading.Thread.Sleep(500); // 0.5 second delay between messages
                    }
                }
                catch (HttpException e)
                {
                    Console.WriteLine("Failed to send/update a message to server {0} ({1})", guild.Name, guild.Id);
                    UpdateStatusError(guild.Id, e);
                    continue;
                }

                // delete excess messages once the message count falls below the target message allocation
                if (messages.Count <= ALLOCATED_MESSAGES && ALLOCATED_MESSAGES < channelMessagePair.Item2.Count)
                {
                    Console.WriteLine("channelMessagePair.Item1.DeleteMessagesAsync()");
                    await channelMessagePair.Item1.DeleteMessagesAsync(channelMessagePair.Item2.GetRange(ALLOCATED_MESSAGES, channelMessagePair.Item2.Count - ALLOCATED_MESSAGES));
                    channelMessagePair.Item2.RemoveRange(ALLOCATED_MESSAGES, channelMessagePair.Item2.Count - ALLOCATED_MESSAGES);
                }
            }

            lastMessageCount = messages.Count;
        }

        public static void UpdateStatusError(ulong guildId, HttpException e)
        {
            switch (e.DiscordCode)
            {
                case 10003:
                    Console.WriteLine("Channel cound not be found!");
                    currentStatusMessages.Remove(guildId);
                    return;
                case 50001:
                    Console.WriteLine("Bot does not have permission!");
                    currentStatusMessages.Remove(guildId);
                    return;
                default:
                    Console.WriteLine(e);
                    return;
            }
        }

        public static string LobbyCountMessage(int lobbyCount, int playerCount, string lobbyType)
        {
            if (playerCount == 0)
            {
                return string.Format("**There are no {0} lobbies!**", lobbyType);
            }
            else if (playerCount == 1)
            {
                return string.Format("**1** player is in a {0} lobby.", lobbyType);
            }
            else
            {
                return string.Format("**{0}** players are in **{1}** {2} {3}.", playerCount, lobbyCount, lobbyType, lobbyCount > 1 ? "lobbies" : "lobby"); ;
            }
        }

        public static async Task LoginAsync()
        {
            Console.WriteLine("Logging into Discord...");
            try
            {
                await discordSocketClient.LoginAsync(TokenType.Bot, token);
                await discordSocketClient.StartAsync();
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to log into Discord!");
                Console.WriteLine(e);
            }
        }

        private static Task DiscordSocketClient_Log(LogMessage arg)
        {
            Console.WriteLine(arg);
            return Task.CompletedTask;
        }

        private static Task DiscordSocketClient_LoggedIn()
        {
            Console.WriteLine("Logged into Discord!");
            loggedIn = true;

            Console.WriteLine("Saving token file...");
            File.WriteAllText("token.txt", token);
            Console.WriteLine("Saved token file!");

            return Task.CompletedTask;
        }

        private static Task DiscordSocketClient_LoggedOut()
        {
            Console.WriteLine("Logged out of Discord!");
            loggedIn = false;
            return Task.CompletedTask;
        }

        public static async Task RegisterCommandsAsync()
        {
            discordSocketClient.MessageReceived += HandleCommandAsync;
            await commandService.AddModulesAsync(Assembly.GetEntryAssembly(), serviceProvider);
        }

        private static async Task HandleCommandAsync(SocketMessage arg)
        {
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
