using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
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
        static EventWaitHandle waitHandle;
        private static string token;
        public static bool loggedIn;
        public static Dictionary<RestTextChannel, List<RestUserMessage>> currentStatusMessages;
        public static int lastMessageCount;

        // status message channel name
        const string CHANNEL_NAME = "transformed-lobbies";
        // message clock format
        const string CLOCK_FORMAT = "dd/MM/yy HH:mm";

        static readonly Color LOBBY_COLOUR = Color.Gold;
        const int ALLOCATED_MESSAGES = 30;

        public static void Run()
        {
            discordSocketClient = new DiscordSocketClient();
            commandService = new CommandService();
            serviceProvider = new ServiceCollection()
                .AddSingleton(discordSocketClient)
                .AddSingleton(commandService)
                .BuildServiceProvider();
            waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);

            if (File.Exists("token.txt"))
            {
                token = File.ReadAllText("token.txt");
                Console.WriteLine("Using persisted bot token.");
            }
            else
            {
                token = Web.InputRequest("Enter bot token.");
            }

            discordSocketClient.Log += DiscordSocketClient_Log;
            discordSocketClient.LoggedIn += DiscordSocketClient_LoggedIn;
            discordSocketClient.LoggedOut += DiscordSocketClient_LoggedOut;
            RunBotAsync().GetAwaiter().GetResult();
            // wait for discord login
            waitHandle.WaitOne();
        }

        public static async Task UpdateStatus(int playerCount, int lobbyPlayerCount, List<LobbyInfo> lobbyInfos)
        {
            Console.WriteLine("Updating Discord status messages...");

            // Create new status channels
            if (currentStatusMessages == null)
            {
                currentStatusMessages = new Dictionary<RestTextChannel, List<RestUserMessage>>();
                var guilds = await discordSocketClient.Rest.GetGuildsAsync();
                foreach(var guild in guilds)
                {
                    // Find and delete old status channels
                    var textChannels = await guild.GetTextChannelsAsync();
                    foreach (var channel in textChannels)
                    {
                        if (channel.Name == CHANNEL_NAME)
                        {
                            await channel.DeleteAsync();
                        }
                    }
                    
                    // Create the status channel
                    var statusChannel = await guild.CreateTextChannelAsync(CHANNEL_NAME);
                    List<RestUserMessage> statusMessages = new List<RestUserMessage>();
                    // Allocate status messages
                    for (int i = 0; i < ALLOCATED_MESSAGES; i++)
                    {
                        statusMessages.Add(await statusChannel.SendMessageAsync("** **"));
                    }
                    // Store
                    currentStatusMessages.Add(statusChannel, statusMessages);
                }
            }

            List<string> messages = new List<string>();
            List<Embed> embeds = new List<Embed>();

            // Create overview message
            if (playerCount >= 0)
            {
                string statusOverview = string.Format("__S&ASRT lobby status — {0} GMT__", DateTime.Now.ToString(CLOCK_FORMAT));
                statusOverview += string.Format("\n\n**{0} people are playing S&ASRT.**", playerCount);
                if (lobbyPlayerCount == 0)
                {
                    statusOverview += "\n**There are no lobbies!**";
                }
                else if (lobbyPlayerCount == 1)
                {
                    statusOverview += "\n**1 player is in a lobby.**";
                }
                else
                {
                    statusOverview += string.Format("\n**{0} players are in {1} {2}.**", lobbyPlayerCount, lobbyInfos.Count, lobbyInfos.Count > 1 ? "lobbies" : "lobby");;
                }
                messages.Add(statusOverview);
                embeds.Add(null);
            }
            else
            {
                messages.Add("Bot is not logged into Steam!");
                embeds.Add(null);
            }

            // Create lobby messages
            foreach (var lobbyInfo in lobbyInfos)
            {
                EmbedBuilder builder = new EmbedBuilder();
                builder.WithColor(LOBBY_COLOUR);
                builder.WithTitle(lobbyInfo.name);
                builder.AddField("Players", lobbyInfo.playerCount.ToString(), true);
                if (lobbyInfo.type >= 0)
                {
                    builder.AddField("Type", LobbyTools.GetLobbyType(lobbyInfo.type), true);
                    if (lobbyInfo.type != 3)
                    {
                        builder.WithDescription(string.Format("steam://joinlobby/{0}/{1}", Steam.APPID, lobbyInfo.id));
                    }
                    else
                    {
                        builder.WithDescription("Private lobby");
                    }
                }
                if (lobbyInfo.state >= 0)
                {
                    builder.AddField("Activity", LobbyTools.GetActivity(lobbyInfo.state, lobbyInfo.raceProgress, lobbyInfo.countdown), true);
                    builder.AddField("Event", LobbyTools.GetEvent(lobbyInfo.type, lobbyInfo.matchMode), true);
                    string[] map = LobbyTools.GetMap(lobbyInfo.type, lobbyInfo.matchMode);
                    builder.AddField(map[0], map[1], true);
                    builder.AddField("Difficulty", LobbyTools.GetDifficulty(lobbyInfo.type, lobbyInfo.difficulty),true);
                }
                else
                {
                    builder.WithDescription("Lobby initialising...");
                }
                messages.Add("");
                embeds.Add(builder.Build());
            }

            // Send the messages to Discord.
            foreach (var item in currentStatusMessages)
            {   
                for (int i = 0; i < messages.Count; i++)
                {
                    if (i < item.Value.Count)
                    {
                        await item.Value[i].ModifyAsync(m => {m.Content = messages[i]; m.Embed = embeds[i];});
                    }
                    else
                    {
                        item.Value.Add(await item.Key.SendMessageAsync(messages[i], embed: embeds[i]));
                    }
                }
                if (messages.Count < lastMessageCount)
                {
                    for (int j = messages.Count; j < lastMessageCount; j++)
                    {
                        await item.Value[j].ModifyAsync(m => {m.Content = "** **"; m.Embed = null;});
                    }
                }
            }
            lastMessageCount = lobbyInfos.Count + 1;
            
            Console.WriteLine("Updated Discord status messages!");
        }

        public static async Task RunBotAsync()
        {
            await RegisterCommandsAsync();
            Console.WriteLine("Logon to Discord...");
            await discordSocketClient.LoginAsync(TokenType.Bot, token);
            await discordSocketClient.StartAsync();
        }

        private static Task DiscordSocketClient_Log(LogMessage arg)
        {
            //Console.WriteLine(arg);
            return Task.CompletedTask;
        }

        private static Task DiscordSocketClient_LoggedIn()
        {
            Console.WriteLine("Logged into Discord!");
            loggedIn = true;

            Console.WriteLine("Saving token file...");
            File.WriteAllText("token.txt", token);
            Console.WriteLine("Saved token file!");

            // free the main thread
            waitHandle.Set();
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
