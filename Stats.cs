using System;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.IO.Compression;
using Npgsql;

namespace SLB
{
    static class Stats
    {
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
                    TrustServerCertificate = true,
                };

                return builder.ToString();
            }
        }

        private static DateTime StartDate = DateTime.Now;
        private static List<byte> Dataset = new List<byte>();
        private static object DataLock = new object();
        private static int Nibbles = 0;
        private static int Entries = 0;


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

        private static void WriteNibble(byte nibble)
        {
            nibble &= 0x0F;
            bool even = (Nibbles & 1) == 0;
            if (even)
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
            Console.WriteLine("Stats.FlushData()");
            Console.WriteLine("Attempting to flush {0} entries ({1} bytes)", Entries, Dataset.Count);
            using (MemoryStream ms = new MemoryStream())
            {
                lock (DataLock)
                {
                    // compress data
                    using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress))
                    {
                        ds.Write(Dataset.ToArray(), 0, Dataset.Count);
                    }

                    // start new dataset
                    Dataset.Clear();
                    Nibbles = 0;
                    Entries = 0;
                }

                try
                {
                    // save to database
                    using (NpgsqlConnection connection = new NpgsqlConnection(ENV_CONNECTION_STR))
                    using (NpgsqlCommand command = connection.CreateCommand())
                    {
                        connection.Open();
                        command.CommandText = 
                            "CREATE TABLE IF NOT EXISTS stats_data (datasets bytea);" +
                            "INSERT INTO stats_data VALUES (@dataset);";
                        command.Parameters.AddWithValue("@dataset", ms.ToArray());
                        command.ExecuteNonQuery();
                    }
                }
                catch(Exception e)
                {
                    Console.WriteLine("Database save exception!\n" + e);
                }    
            }
        }

        // restore stats using the database
        public static void Restore()
        {
            Console.WriteLine("Stats.Restore()");
            int datasets = 0; 
            int entries = 0;
            try
            {
                using (NpgsqlConnection connection = new NpgsqlConnection(ENV_CONNECTION_STR))
                using (NpgsqlCommand command = connection.CreateCommand())
                {
                    // restore from database
                    connection.Open();
                    command.CommandText = "SELECT datasets FROM stats_data";
                    using (NpgsqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            datasets++;
                            using (Stream s = reader.GetStream(reader.GetOrdinal("datasets")))
                            using (DeflateStream ds = new DeflateStream(s, CompressionMode.Decompress))
                            {
                                while(ReadEntryData(ds, out DateTime timestamp, out List<LobbyInfo> lobbyInfos))
                                {
                                    ProcessEntry(timestamp, lobbyInfos, false);
                                    entries++;
                                    if (entries == 1)
                                    {
                                        StartDate = timestamp;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch(Exception e)
            {
                Console.WriteLine("Stats.Restore() Database exception!\n" + e); 
            }

            Console.WriteLine("Stats.Restore() Loaded {0} entries from {1} datasets.", entries, datasets);
        }

        // read entry from a decompressed dataset stream
        // lobbyInfos will only contain the type and player count for each lobby (no other info)
        private static bool ReadEntryData(Stream stream, out DateTime timestamp, out List<LobbyInfo> lobbyInfos)
        {
            try
            {
                lobbyInfos = new List<LobbyInfo>();
                using (BinaryReader br = new BinaryReader(stream, System.Text.Encoding.Default, true))
                {
                    // timestamp
                    timestamp = DateTime.FromBinary(br.ReadInt64());
                    
                    // lobby counts
                    int nibbles = 0;
                    byte b = 0;
                    int lobbyType = 0;
                    do
                    {
                        bool even = (nibbles & 1) == 0;        
                        if (even)
                        {
                            b = br.ReadByte();
                        }
                        byte nibble = (byte)(even ? b >> 4 : b & 0xF);

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
                    while (lobbyType < 4);
                }
                return true;
            }
            catch
            {
                timestamp = DateTime.MinValue;
                lobbyInfos = new List<LobbyInfo>();
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
        // optionally return mm stats
        public static LobbyStats ProcessEntry(DateTime timestamp, List<LobbyInfo> lobbyInfos, bool mmStats = true)
        {
            LobbyStats lobbyStats = new LobbyStats() { StartDate = StartDate };

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

            // update MM data
            int[] dayData = MMWeekData[timestamp.DayOfWeek];
            dayData[timestamp.Hour * 2] += lobbyStats.MMPlayers;
            dayData[timestamp.Hour * 2 + 1]++;
            if (lobbyStats.MMPlayers >= MMAllTimeBestPlayers)
            {
                MMAllTimeBestPlayers = lobbyStats.MMPlayers;
                MMAllTimeBestDate = timestamp;
            }

            if (!mmStats)
            {
                // skip MM stats
                return lobbyStats;
            }

            // MM stats
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
                    if (avgPlayers >= lobbyStats.MMBestHourAvgPlayers)
                    {
                        lobbyStats.MMBestHourAvgPlayers = avgPlayers;
                        lobbyStats.MMBestDay = pair.Key;
                        lobbyStats.MMBestHour = i;
                    }
                    if (avgPlayers <= lobbyStats.MMWorstHourAvgPlayers)
                    {
                        lobbyStats.MMWorstHourAvgPlayers = avgPlayers;
                        lobbyStats.MMWorstDay = pair.Key;
                        lobbyStats.MMWorstHour = i;
                    }
                }
            }

            return lobbyStats;
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
        public DateTime StartDate;
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