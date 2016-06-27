// This file is part of mIP - the Managed TCP/IP Stack.
// Hosted on Codeplex: http://mip.codeplex.com
// mIP is free software licensed under the Apache License 2.0
// © Copyright 2012 ValkyrieTech, LLC

using System;
using Microsoft.SPOT;

namespace Networking
{
    public static class Extensions
    {
        public static void Overwrite(this byte[] origin, int start, byte[] source)
        {
            foreach (var aByte in source) origin[start++] = aByte;
        }

        /// <summary>
        /// Finds the location of the pattern bytes within the buffer bytes. and returns   
        /// Returns a -1 if the pattern is not found within the buffer
        /// </summary>
        /// <param name="buffer">Bytes to be searched</param>
        /// <param name="pattern">Bytes you are looking for</param>
        /// <returns>Index of the first pattern byte within the buffer array</returns>
        public static int Locate(this byte[] buffer, byte[] pattern)
        {
            // Based on: http://stackoverflow.com/questions/283456/byte-array-pattern-search

            for (int i = 0; i < buffer.Length; i++)
            {
                if (pattern[0] == buffer[i] && buffer.Length - i >= pattern.Length)
                {
                    bool ismatch = true;
                    for (int j = 1; j < pattern.Length && ismatch == true; j++)
                    {
                        if (buffer[i + j] != pattern[j])
                        {
                            ismatch = false;
                            continue;
                        }
                    }

                    if (ismatch) return i;
                }
            }
            
            return -1;
        }

        static Random random = new Random();

        public static byte[] GetRandomBytes(short count)
        {
            byte[] buffer = new byte[count];
            random.NextBytes(buffer);
            return buffer;

            //string result = String.Concat(buffer.Select(x => x.ToString("X2")).ToArray());
            //if (digits % 2 == 0)
            //    return result;
            //return result + random.Next(16).ToString("X");
        }

        ///// <summary>
        ///// Calculate the IPv4 Checksum of the buffer array.  
        ///// </summary>
        ///// <param name="buffer"></param>
        ///// <param name="start"></param>
        ///// <param name="length"></param>
        ///// <returns></returns>
        //public static byte[] InternetChecksum(this byte[] buffer, int length = 0, int start = 0)
        //{
        //    // Based on: http://stackoverflow.com/questions/2188060/calculate-an-internet-aka-ip-aka-rfc791-checksum-in-c-sharp

        //    //byte[] buffer = value;
        //    length = length == 0 ? buffer.Length : length;
        //    int i = start;
        //    UInt32 sum = 0;
        //    UInt32 data = 0;
        //    while (length > 1)
        //    {
        //        data = 0;
        //        data = (UInt32)(((UInt32)(buffer[i]) << 8) | ((UInt32)(buffer[i + 1]) & 0xFF));
        //        sum += data;
                
        //        if ((sum & 0xFFFF0000) > 0)
        //        {
        //            sum = sum & 0xFFFF;
        //            sum += 1;
        //        }

        //        //Debug.WriteLine("Bytes: " + buffer[i].ToString() + ", " + buffer[i + 1].ToString());
        //        //Debug.WriteLine("Sum = " + sum + ", Length = " + length + ", i = " + i);
                
        //        i += 2;
        //        length -= 2;
        //    }

        //    if (length > 0)
        //    {
        //        sum += (UInt32)(buffer[i] << 8);
        //        if ((sum & 0xFFFF0000) > 0)
        //        {
        //            sum = sum & 0xFFFF;
        //            sum += 1;
        //        }
        //    }

        //    sum = ~sum;
        //    sum = sum & 0xFFFF;

        //    var result = new byte[2];

        //    result[0] = (byte)(sum >> 8);
        //    result[1] = (byte)sum;

        //   // result[0] = (byte)((UInt16)sum >> 8);
        //   //result[1] = (byte)(UInt16)sum;            

        //    return result;
        //}

        /// <summary>
        /// Calculate the IPv4 Checksum of the buffer array.  
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="start"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static byte[] InternetChecksum(this byte[] buffer, int length = 0, int start = 0, byte[] SourceIP = null, byte[] DestiantionIP = null, byte Protocol = 0x06)
        {
            // Based on: http://stackoverflow.com/questions/2188060/calculate-an-internet-aka-ip-aka-rfc791-checksum-in-c-sharp

            //byte[] buffer = value;
            length = length == 0 ? buffer.Length : length;
            int i = start;
            UInt32 sum = 0;
            UInt32 data = 0;
            byte[] pseudoHeader = null;

            if (SourceIP != null && DestiantionIP != null)
            {
                pseudoHeader = new byte[12] { SourceIP[0], SourceIP[1], SourceIP[2], SourceIP[3], DestiantionIP[0], DestiantionIP[1], DestiantionIP[2], DestiantionIP[3], 0x00, Protocol, (byte)(length >> 8), (byte)(length >> 0) };
                length += pseudoHeader.Length;
                i -= pseudoHeader.Length;
            }
                        
            while (length > 1)
            {
                //TODO: this is hideous and needs to be refactored!
                data = 0;
                if (i < start)
                    data = (UInt32)(((UInt32)(pseudoHeader[(i - start) + pseudoHeader.Length]) << 8) | ((UInt32)(pseudoHeader[(i - start) + pseudoHeader.Length + 1]) & 0xFF));                
                else
                    data = (UInt32)(((UInt32)(buffer[i]) << 8) | ((UInt32)(buffer[i + 1]) & 0xFF));
                
                sum += data;

                if ((sum & 0xFFFF0000) > 0)
                {
                    sum = sum & 0xFFFF;
                    sum += 1;
                }

                //Debug.WriteLine("Bytes: " + buffer[i].ToString() + ", " + buffer[i + 1].ToString());
                //Debug.WriteLine("Sum = " + sum + ", Length = " + length + ", i = " + i);

                i += 2;
                length -= 2;
            }

            if (length > 0)
            {
                sum += (UInt32)(buffer[i] << 8);
                if ((sum & 0xFFFF0000) > 0)
                {
                    sum = sum & 0xFFFF;
                    sum += 1;
                }
            }

            sum = ~sum;
            sum = sum & 0xFFFF;

            var result = new byte[2];

            result[0] = (byte)(sum >> 8);
            result[1] = (byte)sum;

            // result[0] = (byte)((UInt16)sum >> 8);
            //result[1] = (byte)(UInt16)sum;            

            return result;
        }

        ///// <summary>
        ///// Calculate the IPv4 Checksum of the buffer array.  
        ///// </summary>
        ///// <param name="buffer"></param>
        ///// <param name="start"></param>
        ///// <param name="length"></param>
        ///// <returns></returns>
        //public static byte[] InternetChecksum2(this byte[] buffer, int length = 0, int start = 0)
        //{
        //    // Based on: http://stackoverflow.com/questions/2188060/calculate-an-internet-aka-ip-aka-rfc791-checksum-in-c-sharp

        //    //byte[] buffer = value;
        //    length = length == 0 ? buffer.Length : length;
        //    int i = start;
        //    UInt32 sum = 0;
        //    while (i < length-1)
        //    {
        //        sum += (UInt32)(((UInt32)(buffer[i]) << 8) | ((UInt32)(buffer[i + 1]) & 0xFF));

        //        if ((sum & 0xFFFF0000) > 0) sum = sum++ & 0xFFFF;

        //        //Debug.WriteLine("Bytes: " + buffer[i].ToString() + ", " + buffer[i + 1].ToString());
        //        //Debug.WriteLine("Sum = " + sum + ", Length = " + length + ", i = " + i);

        //        i += 2;
        //    }

        //    if (length % 2 == 1)  
        //    {
        //        sum += (UInt32)(buffer[i] << 8);
        //        if ((sum & 0xFFFF0000) > 0) sum = sum++ & 0xFFFF;
        //    }

        //    sum = ~sum;
        //    sum = sum & 0xFFFF;

        //    var result = new byte[2];

        //    result[0] = (byte)(sum >> 8);
        //    result[1] = (byte)sum;

        //    // result[0] = (byte)((UInt16)sum >> 8);
        //    //result[1] = (byte)(UInt16)sum;            

        //    return result;
        //}


        /// <summary>
        /// Convert to a 8-byte unsigned integer
        /// </summary>
        /// <param name="array"></param>
        /// <returns></returns>
        public static ulong ToLong(this byte[] array)
        {
            int pos = array.Length * 8;
            ulong result = 0;
            foreach (byte by in array)
            {
                pos -= 8;
                result |= (ulong)by << pos;
            }
            return result;
        }

        /// <summary>
        /// Convert to a 4-byte unsigned integer
        /// </summary>
        /// <param name="array"></param>
        /// <returns></returns>
        public static uint ToInt(this byte[] array)
        {
            return (uint)ToLong(array);
        }

        /// <summary>
        /// Convert to a 2-byte unsigned integer
        /// </summary>
        /// <param name="array"></param>
        /// <returns></returns>
        public static ushort ToShort(this byte[] array)
        {
            return (ushort)ToLong(array);
        }

        public static byte[] ToBytes(this ushort num)
        {
            return new byte[] { (byte)(num >> 8), (byte)(num >> 0) };
        }

        public static byte[] ToBytes(this uint num)
        {
            return new byte[] { (byte)(num >> 24), (byte)(num >> 16), (byte)(num >> 8), (byte)(num >> 0) };
        }

        private const string digits = "0123456789.-";

        public static bool IsNumeric(this string value)
        {
            //foreach (var aChar in value)
            for (int i = 0; i < value.Length; ++i)
            {
                char aChar = value[i];
                if (digits.IndexOf(aChar) == -1) return false;
            }

            return true;
        }

        /// <summary>
        /// Validates and converts a string IP Address (like "192.168.1.1") to a byte array.
        /// </summary>
        /// <param name="ipAddress"></param>
        /// <returns>IP Address representation as bytes.  Returns null if validation fails.  </returns>
        public static byte[] ToBytes(this string ipAddress)
        {
            if (ipAddress.IndexOf('.') > 0)
            {
                var dnsParts = ipAddress.Split('.');

                // if the dnsname is already an IP address, just convert to bytes and return that!  
                if (dnsParts.Length == 4 && dnsParts[3].IsNumeric() && dnsParts[2].IsNumeric() && dnsParts[1].IsNumeric() && dnsParts[0].IsNumeric())
                    return new byte[4] { (byte)(ushort.Parse(dnsParts[0]) >> 0), (byte)(ushort.Parse(dnsParts[1]) >> 0), (byte)(ushort.Parse(dnsParts[2]) >> 0), (byte)(ushort.Parse(dnsParts[3]) >> 0) };
            }

            return null;
        }

        const string hexDigits = "0123456789ABCDEF";

        public static string ToHexString(this byte[] buffer)
        {
            // Based on: http://stackoverflow.com/questions/311165/how-do-you-convert-byte-array-to-hexadecimal-string-and-vice-versa-in-c

            var chars = new char[buffer.Length * 2];

            for (short y = 0, x = 0; y < buffer.Length; ++y, ++x)
            {
                chars[x] = hexDigits[(buffer[y] & 0xF0) >> 4];
                chars[++x] = hexDigits[(buffer[y] & 0x0F)];
            }

            return new string(chars);
        }

        /// <summary>
        /// Standard dot separated integers, like 192.168.1.255
        /// </summary>
        /// <param name="array"></param>
        /// <returns></returns>
        public static string ToAddress(this byte[] array)
        {
            string result = string.Empty;
            
            if (array != null)
                foreach (byte aByte in array)
                    result += ((uint)aByte).ToString() + ".";

            return result.TrimEnd('.');
        }

        public static bool BytesEqual(this byte[] Array1, byte[] Array2)
        {
            return Array1.BytesEqual(0, Array2, 0, Array1.Length);
        }

        public static bool BytesEqual(this byte[] Array1, int Start1, byte[] Array2, int Start2, int Count)
        {
            bool result = true;
            for (int i = 0; i < Count; i++)
            {
                if (Array1[i + Start1] != Array2[i + Start2])
                {
                    result = false;
                    break;
                }
            }
            return result;
        }

    }
}

// This code allows Extension methods to work in .NET MF 4.1
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class ExtensionAttribute : Attribute
    {
    }
}
