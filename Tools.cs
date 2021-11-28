using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SLB
{
    public static class Tools
    {
        public static string DateTimeWithOffset(DateTime utcTime, TimeZoneInfo timeZone, string format = "dd/MM/yy HH:mm:ss")
        {
            DateTime convertedTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, timeZone);
            string timeStr = convertedTime.ToString(format) + " GMT";
            double offset = timeZone.GetUtcOffset(convertedTime).TotalHours;
            if (offset > 0)
            {
                timeStr += "+" + offset;
            }
            else if (offset < 0)
            {
                timeStr += offset;
            }
            return timeStr;
        }

        public static string HourStr(int hour)
        {
            bool am = hour < 12;
            hour %= 12;
            if (hour == 0)
            {
                hour = 12;
            }
            return hour + (am ? "AM" : "PM");
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