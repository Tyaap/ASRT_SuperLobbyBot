using System;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.IO.Compression;

namespace SLB
{
    static class Stats
    {
        private static readonly DateTime referenceTime = new DateTime(2021, 1, 1);
        private static List<byte> recordData = new List<byte>();
        private static int nibbles = 0;
        private static int records = 0;
        private const int recordLimit = 5000;


        // data for live MM statistics
        private static readonly Dictionary<DayOfWeek, int[]> MMWeekData = new Dictionary<DayOfWeek, int[]>() 
        {
            { DayOfWeek.Monday, new int[48] },
            { DayOfWeek.Tuesday, new int[48] },
            { DayOfWeek.Wednesday, new int[48] },
            { DayOfWeek.Thursday, new int[48] },
            { DayOfWeek.Friday, new int[48] },
            { DayOfWeek.Saturday, new int[48] },
            { DayOfWeek.Sunday, new int[48] },
        };

        private static DateTime MMAllTimeBestDate = DateTime.MinValue;
        private static int MMAllTimeBestPlayers = 0;

        // timer for printing stats data to logs (used for backup)
        static Timer printDataTimer;

        public static void Run()
        {
            printDataTimer = new Timer(PrintWeekData, null, 60000, 60000);
        }

        private static void RecordNibble(byte nibble)
        {
            nibble &= 0x0F;
            bool nextByte = (nibbles & 1) == 0;
            if (nextByte)
            {
                recordData.Add((byte)(nibble >> 4));
            }
            else
            {
                recordData[nibbles / 2] += nibble;
            }
            nibbles++;
        }

        private static void FlushData()
        {
            // TODO - compress and flush data to database

            // clear data in memory
            recordData.Clear();
            nibbles = 0;
        }

        public static void AddRecord(DateTime timestamp, List<LobbyInfo> lobbyInfos)
        {
            // timestamp
            recordData.AddRange(BitConverter.GetBytes((int)(timestamp - referenceTime).TotalSeconds));

            // lobby player counts
            for(int lobbyType = 0; lobbyType < 4; lobbyType++)
            {
                foreach (LobbyInfo lobbyInfo in lobbyInfos)
                {
                    if (lobbyInfo.type == lobbyType)
                    {
                        RecordNibble((byte)lobbyInfo.playerCount);
                    }
                }
                if (lobbyType < 3)
                {
                    // zero nibble to split up player counts 
                    RecordNibble(0);
                }
            }
            records++;
            if (records == recordLimit)
            {
                FlushData();
                records = 0;
            }
        }

        public static LobbyStats GetLobbyStats(DateTime timestamp, List<LobbyInfo> lobbyInfos)
        {
            LobbyStats lobbyStats = new LobbyStats();

            // lobby counts
            foreach (LobbyInfo lobbyInfo in lobbyInfos)
            {
                if (lobbyInfo.type == 3)
                {
                    lobbyStats.CustomPlayers += lobbyInfo.playerCount;
                    lobbyStats.CustomLobbies++;
                }
                else
                {
                    lobbyStats.MMPlayers += lobbyInfo.playerCount;
                    lobbyStats.MMLobbies++;
                }
            }

            // update MM week data
            int[] dayData = MMWeekData[timestamp.DayOfWeek];
            dayData[timestamp.Hour * 2] += lobbyStats.MMPlayers;
            dayData[timestamp.Hour * 2 + 1]++;
            decimal hourAvgPlayers = (decimal)dayData[timestamp.Hour * 2] / dayData[timestamp.Hour * 2 + 1];

            // MM stats
            if (lobbyStats.MMPlayers > MMAllTimeBestPlayers)
            {
                MMAllTimeBestPlayers = lobbyStats.MMPlayers;
                MMAllTimeBestDate = timestamp;
            }
            lobbyStats.MMAllTimeBestPlayers = MMAllTimeBestPlayers;
            lobbyStats.MMAllTimeBestDate = MMAllTimeBestDate;

            lobbyStats.MMWorstHourAvgPlayers = decimal.MaxValue;
            foreach(var pair in MMWeekData)
            {
                for (int i = 0; i < 24; i++)
                {
                    int count = pair.Value[i * 2 + 1];
                    if (count == 0)
                    {
                        continue;
                    }
                    int sum = pair.Value[i * 2];
                    decimal avgPlayers = (decimal)sum / count;
                    if (avgPlayers > lobbyStats.MMBestHourAvgPlayers)
                    {
                        lobbyStats.MMBestHourAvgPlayers = avgPlayers;
                        lobbyStats.MMBestDay = pair.Key;
                        lobbyStats.MMBestHour = i;
                    }
                    if (avgPlayers < lobbyStats.MMWorstHourAvgPlayers)
                    {
                        lobbyStats.MMWorstHourAvgPlayers = avgPlayers;
                        lobbyStats.MMWorstDay = pair.Key;
                        lobbyStats.MMWorstHour = i;
                    }
                }
            }

            return lobbyStats;
        }


        // used to back up MM stats in logs
        private static void PrintWeekData(object state)
        {
            Console.WriteLine(
                "#### BEGIN MM WEEK DATA ####" +
                "\n" + WeekDataToString(MMWeekData) + "\n" + 
                "#### END MM WEEK DATA ####"
            );
        }

        private static string WeekDataToString(Dictionary<DayOfWeek, int[]> weekData)
        {
            // create a base64 string containing the compresed data
            using (MemoryStream ms = new MemoryStream())
            {
                using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress))
                using (BinaryWriter bw = new BinaryWriter(ds))
                {
                    for (int i = 0; i < 7; i++)
                    {
                        int[] dayData = weekData[(DayOfWeek)i];
                        foreach(int n in dayData)
                        {
                            bw.Write(n);
                        }
                    }
                }
                return Convert.ToBase64String(ms.ToArray());
            }
        }
    }

    public struct LobbyStats
    {
        // lobby counts
        public int MMLobbies;
        public int MMPlayers;
        public int CustomLobbies;
        public int CustomPlayers;

        // MM stats
        public DayOfWeek MMBestDay;
        public int MMBestHour;
        public decimal MMBestHourAvgPlayers;
        public DayOfWeek MMWorstDay;
        public int MMWorstHour;
        public decimal MMWorstHourAvgPlayers;
        public DateTime MMAllTimeBestDate;
        public int MMAllTimeBestPlayers;
    }
}