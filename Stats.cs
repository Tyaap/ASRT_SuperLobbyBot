using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Npgsql;

namespace SLB
{
    public struct StatsPoint
    {
        public int Sum;
        public int Count;
    }

    public struct StatsPoint2
    {
        public int Ref;
        public double Avg;
        public double Min;
        public double Max;
    }

    static class Stats
    {
        // constants
        public const int BIN_WIDTH = 600;
        public const int INTERVAL = 7200;
        public const int DAY = 86400;
        public const int WEEK = 604800;
        public const int CALC_INTERVAL = 3600;
        public static readonly TimeZoneInfo TIMEZONE = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

        // environment variables
        private static string ENV_CONNECTION_STR
        {
            get
            {
                string databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
                Uri databaseUri = new Uri(databaseUrl);
                string[] userInfo = databaseUri.UserInfo.Split(':');

                NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder
                {
                    Host = databaseUri.Host,
                    Port = databaseUri.Port,
                    Username = userInfo[0],
                    Password = userInfo[1],
                    Database = databaseUri.LocalPath.TrimStart('/'),
                    SslMode = SslMode.Require,
                };

                return builder.ToString();
            }
        }
        private static DateTime StartDate = DateTime.MinValue;
        private static List<byte> Dataset = new List<byte>();
        private static object DataLock = new object();
        private static int Nibbles = 0;
        private static int Entries = 0;


        // data for live MM statistics
        private static StatsPoint[] Bins = new StatsPoint[WEEK / BIN_WIDTH];
        private static StatsPoint2[] MMBestTimes = new StatsPoint2[7]; // one for each day of the week
        private static DateTime MMAllTimeBestDate = DateTime.MinValue;
        private static int MMAllTimeBestPlayers = 0;
        private static DateTime BestTimesCalcTime = DateTime.MinValue;


        private static void WriteNibble(byte nibble)
        {
            nibble &= 0x0F;
            if ((Nibbles & 1) == 0)
            {
                Dataset.Add((byte)(nibble << 4));
            }
            else
            {
                Dataset[Nibbles / 2] += nibble;
            }
            Nibbles++;
        }

        // write a dataset to the database
        public static void SaveDataset()
        {
            if (Entries == 0)
            {
                return; // nothing to save
            }

            Console.WriteLine("Stats.SaveDataset() Entries:{0} Bytes:{1}", Entries, Dataset.Count);

            byte[] compressedData;
            using (var ms = new MemoryStream())
            {
                lock (DataLock)
                {
                    // compress data
                    using (var ds = new DeflateStream(ms, CompressionMode.Compress))
                    {
                        ds.Write(Dataset.ToArray(), 0, Dataset.Count);
                    }

                    // start new dataset
                    Dataset.Clear();
                    Nibbles = 0;
                    Entries = 0;
                }

                compressedData = ms.ToArray();
            }

            Console.WriteLine("Stats.SaveDataset() Bytes after compression: " + compressedData.Length);

            try
            {
                // save to database
                using (var connection = new NpgsqlConnection(ENV_CONNECTION_STR))
                using (var command = connection.CreateCommand())
                {
                    connection.Open();
                    command.CommandText = 
                        "CREATE TABLE IF NOT EXISTS stats_data (datasets bytea);" +
                        "INSERT INTO stats_data VALUES (@dataset);";
                    command.Parameters.AddWithValue("@dataset", compressedData);
                    command.ExecuteNonQuery();
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine("SaveDataset() Exception!\n" + ex);
            }
        }

        // restore stats using the database
        public static void LoadDatasets()
        {
            Console.WriteLine("Stats.LoadDatasets()");
            int totalEntries = 0;
            int totalDatasets = 0;
            try
            {
                using (var connection = new NpgsqlConnection(ENV_CONNECTION_STR))
                using (var command = connection.CreateCommand())
                {
                    // restore from database
                    connection.Open();
                    command.CommandText = "SELECT datasets FROM stats_data";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            totalDatasets++;
                            using (var s = reader.GetStream(reader.GetOrdinal("datasets")))
                            {
                                int numEntries = LoadDataSet(s, out var startDate, out var endDate);
                                if (numEntries == 0)
                                {
                                    continue;
                                }
                                totalEntries += numEntries;
                                Console.WriteLine("Dataset number:{0} startDate:{1} endDate:{2} numEntries:{3}", totalDatasets, startDate, endDate, numEntries);
                            }      
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine("Stats.LoadDatasets() Exception!\n" + ex);
            }
            Console.WriteLine("Stats.LoadDatasets() totalDatasets:{0} totalEntries:{1}", totalDatasets, totalEntries);
        }

        private static int LoadDataSet(Stream s, out DateTime startDate, out DateTime endDate)
        {
            int numEntries = 0;
            startDate = DateTime.MinValue;
            endDate = DateTime.MinValue;
            try
            {
                using (var ds = new DeflateStream(s, CompressionMode.Decompress))
                using (var br = new BinaryReader(ds))
                {                
                    while (ReadEntryData(br, out var timestamp, out var lobbyInfos))
                    {
                        ProcessEntry(timestamp, lobbyInfos, false);
                        numEntries++;
                        if (startDate == DateTime.MinValue)
                        {
                            startDate = timestamp;
                        }
                        endDate = timestamp;
                    }
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine("Stats.LoadDataSet() Exception!\n" + ex);
            }
            return numEntries;
        }

        // read entry from a decompressed dataset stream
        // lobbyInfos will only contain the type and player count for each lobby (no other info)
        private static bool ReadEntryData(BinaryReader br, out DateTime timestamp, out List<LobbyInfo> lobbyInfos)
        {
            try
            {
                // timestamp
                timestamp = DateTime.FromBinary(br.ReadInt64());
                
                // lobby info
                lobbyInfos = new();
                byte b = 0;
                byte nibble = 0;
                int nibbles = 0;
                int lobbyType = 0;
                while (lobbyType < 4)
                {       
                    if ((nibbles & 1) == 0)
                    {
                        b = br.ReadByte();
                        nibble = (byte)(b >> 4);
                    }
                    else
                    {
                        nibble = (byte)(b & 0xF);
                    }

                    if (nibble == 0)
                    {
                        lobbyType++;
                    }
                    else
                    {
                        lobbyInfos.Add(new LobbyInfo() { type = lobbyType, playerCount = nibble });
                    }
                    nibbles++;
                }
                return true;
            }
            catch
            {
                timestamp = DateTime.MinValue;
                lobbyInfos = null;
                return false;
            }       
        }

        public static void WriteEntryData(DateTime timestamp, List<LobbyInfo> lobbyInfos)
        {
            lock (DataLock)
            {
                // timestamp
                Dataset.AddRange(BitConverter.GetBytes(timestamp.ToBinary()));
                Nibbles += (Nibbles & 1) + 16; // align to next byte and add 8 bytes

                // lobby player counts
                for(int lobbyType = 0; lobbyType < 4; lobbyType++)
                {
                    foreach (LobbyInfo lobbyInfo in lobbyInfos)
                    {
                        if (lobbyInfo.type == lobbyType)
                        {
                            WriteNibble((byte)lobbyInfo.playerCount);
                        }
                    }
                    // zero nibble to split up player counts 
                    WriteNibble(0);
                }
                Entries++;
            }
        }

        // process new entry and return new lobby stats
        // optionally return lobby stats
        public static LobbyStats ProcessEntry(DateTime timestamp, List<LobbyInfo> lobbyInfos, bool returnStats = true)
        {
            // program time - used for MM stats
            var programTime = TimeZoneInfo.ConvertTimeFromUtc(timestamp, TIMEZONE); 

            // start date
            if (StartDate == DateTime.MinValue)
            {
                StartDate = timestamp;
            }

            // lobby counts
            int mmPlayers = 0;
            int mmLobbies = 0;
            int cgPlayers = 0;
            int cgLobbies = 0;
            foreach (LobbyInfo lobbyInfo in lobbyInfos)
            {
                if (lobbyInfo.type == 3)
                {
                    cgPlayers += lobbyInfo.playerCount;
                    cgLobbies++;
                }
                else
                {
                    mmPlayers += lobbyInfo.playerCount;
                    mmLobbies++;
                }
            }

            // update MM data
            int slot = Tools.SunOffsetSecs(timestamp) / BIN_WIDTH;
            Bins[slot].Sum += mmPlayers;
            Bins[slot].Count++;

            if (mmPlayers >= MMAllTimeBestPlayers)
            {
                MMAllTimeBestPlayers = mmPlayers;
                MMAllTimeBestDate = timestamp;
            }

            if (!returnStats)
            {
                return null;
            }

            var lobbyStats = GetMMStats();
            lobbyStats.MMPlayers = mmPlayers;
            lobbyStats.MMLobbies = mmLobbies;
            lobbyStats.CGPlayers = cgPlayers;
            lobbyStats.CGLobbies = cgLobbies;
            return lobbyStats;
        }

        public static LobbyStats GetMMStats()
        {
            if (BestTimesCalcTime < DateTime.Now) // periodically recalculate best time stats
            {
                Console.WriteLine("Stats.GetMMStats() Calculating MM best times...");
                MMBestTimes = CalcBestTimes(Bins);
                Console.WriteLine("Time (GMT)\tPlayers (estimate)");
                foreach (StatsPoint2 time in MMBestTimes)
                {
                    Console.WriteLine("{0} - {1}\t{2:0}-{3:0} (mean {4:0.##})",
                        ReadableTime(time.Ref * BIN_WIDTH, "{0} {1:00}:{2:00}"),
                        ReadableTime(time.Ref * BIN_WIDTH + INTERVAL, "{1:00}:{2:00}"), 
                        Math.Floor(time.Min), Math.Ceiling(time.Max), time.Avg);
                }

                BestTimesCalcTime = DateTime.Now.AddSeconds(CALC_INTERVAL);
            }

            return new LobbyStats()
            {
                StartDate = StartDate,
                MMAllTimeBestPlayers = MMAllTimeBestPlayers,
                MMAllTimeBestDate = MMAllTimeBestDate,
                MMBestTimes = MMBestTimes,
            };
        }

        public static string ReadableTime(int sunOffsetSecs, string format = "{0} {1:00}:{2:00}:{3:00}")
        {
            sunOffsetSecs %= 604800;
            var day = (DayOfWeek)(sunOffsetSecs / 86400);
            int hour = (sunOffsetSecs % 86400) / 3600;
            int minute = (sunOffsetSecs % 3600) / 60;
            int second = sunOffsetSecs % 60;
            return string.Format(format, day, hour, minute, second);
        }

        public static StatsPoint2[] CalcBestTimes(StatsPoint[] bins)
        {
            // best times, one per day of the week
            var bestTimes = new StatsPoint2[7];
            for (int i = 0; i < 7; i++)
            {
                bestTimes[i].Ref = -1;
            }

            int nBins = bins.Length;

            int binInterval = INTERVAL / BIN_WIDTH;
            int sum = 0;
            int count = 0;
            for (int i = 0; i < binInterval; i++)
            {
                sum += bins[i].Sum;
                count += bins[i].Count;
            }

            for (int i = 0; i < nBins; i++)
            {
                if (i > 0)
                {
                    int subIndex = i - 1;
                    int addIndex = i + binInterval - 1;
                    if (addIndex >= nBins)
                    {
                        addIndex -= nBins;
                    }
                    sum -= bins[subIndex].Sum;
                    sum += bins[addIndex].Sum;
                    count -= bins[subIndex].Count;
                    count += bins[addIndex].Count;
                }
                double avg = (double)sum / count;

                int btIndex = (i * BIN_WIDTH) / DAY;
                if (bestTimes[btIndex].Avg < avg)
                {
                    if (btIndex > 0)
                    {
                        if (btIndex != 6)
                        {
                            if (bestTimes[btIndex - 1].Ref != -1 & bestTimes[btIndex - 1].Ref + binInterval > i)
                            {
                                continue; // overlaps with previous day best, skip
                            }
                        }
                        else
                        {
                            if (bestTimes[0].Ref != -1 & bestTimes[0].Ref < i + binInterval - nBins)
                            {
                                continue; // overlaps with next day best, skip
                            }
                        }
                    }
                    bestTimes[btIndex].Avg = avg;
                    bestTimes[btIndex].Ref = i;
                }
            }

            for (int i = 0; i < 7; i++)
            {
                int start = bestTimes[i].Ref;
                if (start == -1)
                {
                    continue; // best time undefined
                }

                bestTimes[i].Min = bestTimes[i].Avg;
                bestTimes[i].Max = bestTimes[i].Avg;
                for (int j = 1; j < binInterval; j++)
                {
                    int slot = start + j;
                    if (slot > nBins)
                    {
                        slot -= nBins;
                    }
                    double avg = (double)bins[slot].Sum / bins[slot].Count;
                    if (bestTimes[i].Max < avg)
                    {
                        bestTimes[i].Max = avg;
                    }
                    if (bestTimes[i].Min > avg)
                    {
                        bestTimes[i].Min = avg;
                    } 
                }
            }

            return bestTimes;
        }
    }

    public class LobbyStats
    {
        // lobby counts
        public int MMLobbies;
        public int MMPlayers;
        public int CGLobbies;
        public int CGPlayers;

        // MM stats
        public DateTime StartDate;
        public StatsPoint2[] MMBestTimes;
        public DateTime MMAllTimeBestDate;
        public int MMAllTimeBestPlayers;
    }
}