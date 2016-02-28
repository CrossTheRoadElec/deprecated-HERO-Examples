// This file is part of mIP - the Managed TCP/IP Stack.
// Hosted on Codeplex: http://mip.codeplex.com
// mIP is free software licensed under the Apache License 2.0
// © Copyright 2012 ValkyrieTech, LLC

using System;
using Microsoft.SPOT.Hardware;
using System.Diagnostics;

namespace Networking
{
    /// <summary>
    /// This is PING! 
    /// </summary>
    internal class ICMP
    {
        private static byte[] scratch = new byte[74] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x45, 0x00, 0x00, 0x3c, 0x14, 0xef, 0x00, 0x00, 0x80, 0x01, 0x53, 0xc4, 0xc0, 0xa8, 0x01, 0x56, 0x08, 0x08, 0x08, 0x08, 0x08, 0x00, 0x4d, 0x57, 0x00, 0x01, 0x00, 0x04, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6a, 0x6b, 0x6c, 0x6d, 0x6e, 0x6f, 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69 };
        private static readonly byte[] Request = new byte[1] { 0x08 };
        private static readonly byte[] Reply = new byte[1] { 0x00 };
        //private static object oLock = null;

        public static object[] PacketTypes
        {
            get { throw new NotImplementedException(); }
        }

        public static void HandlePacket(byte[] payload)
        {
            // Handle a new request
            if (payload[34] == ICMP.Request[0])
            {
                // Handle a new request
                SendPING_Reply(Utility.ExtractRangeFromArray(payload, 6, 6), Utility.ExtractRangeFromArray(payload, 26, 4), Utility.ExtractRangeFromArray(payload, 38, 2), Utility.ExtractRangeFromArray(payload, 40, 2));
            }
            else if (payload[34] == ICMP.Reply[0])
            {
                //Parse and do something with the ping result...

                Debug.WriteLine("Received Ping response.");
            }
        }

        /// <summary>
        /// Send PING Request
        /// </summary>
        public static void SendPingRequest(byte[] ipAddress)
        {
            if (Adapter.GatewayMac == null || Adapter.IPAddress == null) return;

            scratch.Overwrite(0, Adapter.GatewayMac);
            scratch.Overwrite(6, Adapter.MacAddress);
            scratch.Overwrite(26, Adapter.IPAddress);
            scratch.Overwrite(30, ipAddress);
            scratch.Overwrite(34, ICMP.Request);

            scratch.Overwrite(24, new byte[] { 0x00, 0x00 }); // clear header checksum, so that calc excludes the checksum itself
            scratch.Overwrite(24, scratch.InternetChecksum(20, 14)); // header checksum

            Adapter.nic.SendFrame(scratch);
        }

        /// <summary>
        /// PING Reply
        /// </summary>
        private static void SendPING_Reply(byte[] destinationMac, byte[] destinationIP, byte[] id, byte[] seq)
        {
            if (Adapter.GatewayMac == null || Adapter.IPAddress == null) return;

            Debug.WriteLine("Sending Response to Ping request");

            scratch.Overwrite(0, destinationMac);
            scratch.Overwrite(6, Adapter.MacAddress);
            scratch.Overwrite(26, Adapter.IPAddress);
            scratch.Overwrite(30, destinationIP);
            scratch.Overwrite(34, ICMP.Reply);
            scratch.Overwrite(38, id);
            scratch.Overwrite(40, seq);

            scratch.Overwrite(36, new byte[2] { 0x00, 0x00 }); // clear ICMP checksum, so that calc excludes the checksum itself
            scratch.Overwrite(36, scratch.InternetChecksum(40, 34)); // header checksum

            // Calculate the IPv4 header checksum
            scratch.Overwrite(24, new byte[2] { 0x00, 0x00 }); // clear header checksum, so that calc excludes the checksum itself
            scratch.Overwrite(24, scratch.InternetChecksum(20, 14)); // header checksum

            Adapter.nic.SendFrame(scratch);

            
        }

    }
}
