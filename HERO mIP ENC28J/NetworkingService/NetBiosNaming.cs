// This file is part of mIP - the Managed TCP/IP Stack.
// Hosted on Codeplex: http://mip.codeplex.com
// mIP is free software licensed under the Apache License 2.0
// © Copyright 2012 ValkyrieTech, LLC

using System;
//using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using System.Diagnostics;

namespace Networking
{
    /// <summary>
    /// Handles on the Naming part of Netbios over TCP
    /// </summary>
    internal class NetBiosNaming
    {
        private static byte[] scratch = new byte[104] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x45, 0x00, 0x00, 0x5a, 0x00, 0x05, 0x00, 0x00, 0xff, 0x11, 0x37, 0x8d, 0xc0, 0xa8, 0x01, 0x5a, 0xc0, 0xa8, 0x01, 0x56, 0x00, 0x89, 0x00, 0x89, 0x00, 0x46, 0x00, 0x00, 0xdd, 0x1a, 0x85, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x20, 0x45, 0x4f, 0x45, 0x46, 0x46, 0x45, 0x45, 0x45, 0x46, 0x46, 0x45, 0x4a, 0x45, 0x4f, 0x45, 0x50, 0x43, 0x41, 0x43, 0x41, 0x43, 0x41, 0x43, 0x41, 0x43, 0x41, 0x43, 0x41, 0x43, 0x41, 0x41, 0x41, 0x00, 0x00, 0x20, 0x00, 0x01, 0x00, 0x00, 0x0f, 0x0f, 0x00, 0x06, 0x60, 0x00, 0xc0, 0xa8, 0x01, 0x5a };

        internal static void HandlePacket(byte[] payload)
        {
            if ((payload[44] >> 3) == 0) // opcode == 0
            {
                byte[] nbName = new byte[32];
                
                Array.Copy(payload, 55, nbName, 0, 32);
                if (Adapter.VerboseDebugging) Debug.WriteLine("Netbios name query for: " + DecodeNetbiosName(nbName));

                //if (payload.BytesEqual(55, NetBiosNaming.EncodeNetbiosName(Adapter.Name), 0, 32))
                // Flexible NetBios Name matching
                if (DecodeNetbiosName(nbName).Trim().ToLower() == Adapter.Name || DecodeNetbiosName(nbName).Trim().ToLower() == Adapter.Name + ".local")
                    SendNetbiosReply(nbName, Utility.ExtractRangeFromArray(payload, 6, 6), Utility.ExtractRangeFromArray(payload, 26, 4), Utility.ExtractRangeFromArray(payload, 42, 2));
            }
        }

        /// <summary>
        /// Netbios Name Reply
        /// </summary>
        private static void SendNetbiosReply(byte[] nbName, byte[] destinationMac, byte[] destinationIP, byte[] transactionID)
        {
            scratch.Overwrite(0, destinationMac);
            scratch.Overwrite(6, Adapter.MacAddress);
            scratch.Overwrite(26, Adapter.IPAddress); 
            scratch.Overwrite(30, destinationIP);
            scratch.Overwrite(42, transactionID);
            scratch.Overwrite(55, nbName);  // Need to use the same name in the response as the query!?  //NetBiosNaming.EncodeNetbiosName(Adapter.Name));
            scratch.Overwrite(100, Adapter.IPAddress);
            
            // Calculate the IPv4 header checksum
            scratch.Overwrite(24, new byte[] { 0x00, 0x00 }); // clear header checksum, so that calc excludes the checksum itself
            scratch.Overwrite(24, scratch.InternetChecksum(20, 14)); // header checksum

            Adapter.nic.SendFrame(scratch);
        }

        internal static byte[] EncodeNetbiosName(string Name)
        {
            byte[] result = new byte[32];
            char c;

            for (int i = 0; i < 15; i++)
            {
                c = i < Name.Length ? Name[i] : ' ';
                result[i * 2] = (byte)(((byte)(c) >> 4) + 65);
                result[(i * 2) + 1] = (byte)(((byte)(c) & 0x0f) + 65);
            } result[30] = 0x41;

            result[31] = 0x41;
            return result;
        }

        static string DecodeNetbiosName(byte[] NbName)
        {
            string result = string.Empty;
            for (int i = 0; i < 15; i++)
            {
                byte b1 = NbName[i * 2];
                byte b2 = NbName[(i * 2) + 1];
                char c = (char)(((b1 - 65) << 4) | (b2 - 65));
                result += c;
            }

            return result;
        }

    }
}
