using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SLB
{
    public static class Tools
    {
        public static string HourStr(int hour)
        {
            bool am = hour < 12;
            hour %= 12;
            if (hour == 0)
            {
                hour = 12;
            }
            return hour + (am ? "am" : "pm");
        }

        public static DateTime NextOccurance(DateTime refTime, DayOfWeek day, int hour)
        {
            int dayOffset = day - refTime.DayOfWeek;
            if (dayOffset < 0 || dayOffset == 0 && hour < refTime.Hour)
            {
                dayOffset += 7;
            }
            int hourOffset = hour - refTime.Hour;

            return refTime.AddDays(dayOffset).AddHours(hourOffset);
        }

        public static DateTime NextOccurance(DateTime refTime, int sunOffsetSecs)
        {
            int day = sunOffsetSecs / 86400;
            int hour = (sunOffsetSecs % 86400) / 3600;
            int min = (sunOffsetSecs % 3600) / 60;
            int sec = sunOffsetSecs % 60;

            int dayOffset = day - (int)refTime.DayOfWeek;
            if (dayOffset < 0 || dayOffset == 0 && hour < refTime.Hour)
            {
                dayOffset += 7;
            }
            int hourOffset = hour - refTime.Hour;
            int minOffset = min - refTime.Minute;
            int secOffset = sec - refTime.Second;

            return refTime.AddDays(dayOffset).AddHours(hourOffset).AddMinutes(minOffset).AddSeconds(secOffset);
        }

        public static long DatetimeToUnixTime(DateTime date)
        {
            var dateTimeOffset = new DateTimeOffset(date);
            var unixDateTime = dateTimeOffset.ToUnixTimeSeconds();
            return unixDateTime;
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

        public static string MemoryInfo()
        {
            Process process = Process.GetCurrentProcess();
            return "Memory Usage\n" + 
                "Current: " + PrettifyByte(process.WorkingSet64) + "\n" +
                "Maximum: " + PrettifyByte(process.PeakWorkingSet64);
        }

        private static string PrettifyByte(long allocatedMemory)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            while (allocatedMemory >= 1024 && order < sizes.Length - 1)
            {
                order++;
                allocatedMemory = allocatedMemory / 1024;
            }
            return $"{allocatedMemory:0.##} {sizes[order]}";
        }
    }  
}