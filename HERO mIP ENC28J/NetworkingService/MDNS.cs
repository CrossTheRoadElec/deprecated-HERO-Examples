// This file is part of mIP - the Managed TCP/IP Stack.
// Hosted on Codeplex: http://mip.codeplex.com
// mIP is free software licensed under the Apache License 2.0
// © Copyright 2012 ValkyrieTech, LLC

using System;
using Microsoft.SPOT.Hardware;
using System.Text;
using System.Collections;
using System.Diagnostics;

namespace Networking
{
    /// <summary>
    /// Multicast DNS allows for local naming to work (supports LLMNR and MDNS)
    /// </summary>
    internal static class MDNS
    {
        private static byte[] prefix = new byte[54] { 0x01, 0x00, 0x5e, 0x00, 0x00, 0xfb, 0x00, 0x0, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x45, 0x00, 0x00, 0x74, 0x01, 0xc5, 0x00, 0x00, 0xff, 0x11, 0x16, 0xb0, 0xc0, 0xa8, 0x01, 0x00, 0xe0, 0x00, 0x00, 0xfb, 0x14, 0xe9, 0x14, 0xe9, 0x00, 0x60, 0xa3, 0x75, 0x00, 0x00, 0x84, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        private static byte[] suffix = new byte[31] { 0x00, 0x01, 0x80, 0x01, 0x00, 0x00, 0x00, 0x78, 0x00, 0x04, 0xc0, 0xa8, 0x01, 0x00, 0xc0, 0x0c, 0x00, 0x2f, 0x80, 0x01, 0x00, 0x00, 0x00, 0x78, 0x00, 0x05, 0xc0, 0x0c, 0x00, 0x01, 0x40 };

        private static object lockObj = new Object();

        /// <summary>
        /// Take care of a packet of Multicast DNS stuff
        /// </summary>
        /// <param name="payload"></param>
        internal static void HandlePacket(byte[] payload)
        {
            ushort ipHeaderLength = (ushort)((payload[14] & 0x0f) * 4);
            var name = DNS.DecodeDnsName(payload, 34 + ipHeaderLength);  // Name from first Query

            if (Adapter.VerboseDebugging) Debug.WriteLine("Local Name Request (MDNS) for " + name);

            bool isQuery = (payload[24 + ipHeaderLength] & (1 << 7)) == 0;  // DNS Query ?
            if (!isQuery) return;

            // Validate that this is MDNS address 224.0.0.251
            if (payload[10 + ipHeaderLength] != 0xe0 || payload[11 + ipHeaderLength] != 0x00 || payload[12 + ipHeaderLength] != 0x00 || payload[13 + ipHeaderLength] != 0xfb) return;


            if (name != Networking.Adapter.Name + ".local") return;  // if the name requested does not match ours, exit!

            // Wow, if we made it past all that, we should send a reply...    
            SendMDNSNameReply();
        }

        /// <summary>
        /// 
        /// </summary>
        private static void SendMDNSNameReply()
        {
            if (Adapter.IPAddress == null || Networking.Adapter.Name == string.Empty) return;

            prefix.Overwrite(0, new byte[6] { 0x01, 0x00, 0x5e, 0x00, 0x00, 0xfb });
            prefix.Overwrite(6, Adapter.MacAddress);       // Source MAC Address
            prefix.Overwrite(26, Adapter.IPAddress);       // Source IP Address

            lock (lockObj)
             {
                // Set the all-important name and IP address -- that's the whole purpose here...!
                suffix.Overwrite(10, Adapter.IPAddress);
                var result = Utility.CombineArrays(prefix, Utility.CombineArrays(DNS.EncodeDnsName(Networking.Adapter.Name + ".local"), suffix));

                result.Overwrite(16, ((ushort)(result.Length - 14)).ToBytes());  // Set IPv4 message size

                result.Overwrite(38, ((ushort)(result.Length - 34)).ToBytes());  // Set UDP message size

                result.Overwrite(24, new byte[] { 0x00, 0x00 }); // clear header checksum, so that calc excludes the checksum itself
                result.Overwrite(24, result.InternetChecksum(20, 14)); // header checksum

                result.Overwrite(40, new byte[] { 0x00, 0x00 }); // clear UDP Checksum

                Debug.WriteLine("Sending MDNS name message! ");

                Adapter.nic.SendFrame(result);  // Send the packet out into the ether....!
            }
        }
    }
}
