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
    internal static class LLMNR
    {

        private static byte[] prefix = new byte[54] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x45, 0x00, 0x00, 0x52, 0x08, 0xfd, 0x00, 0x00, 0x80, 0x11, 0xad, 0xa5, 0xc0, 0xa8, 0x01, 0x52, 0xc0, 0xa8, 0x01, 0x56, 0x14, 0xeb, 0xd5, 0x98, 0x00, 0x3e, 0xb9, 0xe0, 0x52, 0xcb, 0x80, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };

        private static object lockObj = new Object();

        /// <summary>
        /// Take care of a packet of Linked-Local Multicast Name Resolution (LLMNR) stuff
        /// </summary>
        /// <param name="payload"></param>
        internal static void HandlePacket(byte[] payload)
        {
            ushort ipHeaderLength = (ushort)((payload[14] & 0x0f) * 4);
            var name = DNS.DecodeDnsName(payload, 34 + ipHeaderLength);  // Name from first Query
            
            bool isQuery = (payload[24 + ipHeaderLength] & (1 << 7)) == 0;  // DNS Query ?
            if (!isQuery) return;

            // This is not a good or valid way to detect the type because there could be multiple answers, but I have bigger fish to fry...
            bool isTypeA = payload[payload.Length - 3] == 0x01;
            if (!isTypeA) return;

            // Validate that this is an LLMNR address 224.0.0.252
            if (payload[10 + ipHeaderLength] != 0xe0 || payload[11 + ipHeaderLength] != 0x00 || payload[12 + ipHeaderLength] != 0x00 || payload[13 + ipHeaderLength] != 0xfc) return;

            //Debug.WriteLine("Local Name Request (LLMNR, Type A) for " + name);
            
            if (name != Networking.Adapter.Name + ".local" && name != Networking.Adapter.Name) return;  // if the name requested does not match ours, exit!

            if (Adapter.VerboseDebugging) Debug.WriteLine("Local Name Request (LLMNR, Type A) for " + name);

            SendLLMNRNameReply(name, Utility.ExtractRangeFromArray(payload, 42, 2), Utility.ExtractRangeFromArray(payload, 6, 6), Utility.ExtractRangeFromArray(payload, 26, 4), Utility.ExtractRangeFromArray(payload, 34, 2));
        }

        /// <summary>
        /// 
        /// </summary>
        private static void SendLLMNRNameReply(string name, byte[] tranID, byte[] destinationMac, byte[] destinationIP, byte[] destinationPort)
        {
            if (Adapter.IPAddress == null || Networking.Adapter.Name == string.Empty) return;

            lock (lockObj)
            {
                prefix.Overwrite(0, destinationMac);
                prefix.Overwrite(6, Adapter.MacAddress);       // Source MAC Address
                prefix.Overwrite(26, Adapter.IPAddress);       // Source IP Address
                prefix.Overwrite(30, destinationIP);
                prefix.Overwrite(36, destinationPort);
                prefix.Overwrite(42, tranID);

                var suffix = new byte[name.Length * 2 + 22];
                var byteName = Utility.CombineArrays(DNS.EncodeDnsName(name), new byte[4] { 0x00, 0x01, 0x00, 0x01 });
                suffix.Overwrite(0, Utility.CombineArrays(byteName, byteName));
                suffix.Overwrite(suffix.Length - 7, new byte[1] { 0x1e });  // Time To Live (30 seconds)
                suffix.Overwrite(suffix.Length - 5, new byte[1] { 0x04 });  // IP Address length
                suffix.Overwrite(suffix.Length - 4, Adapter.IPAddress);

                var result = Utility.CombineArrays(prefix, suffix);

                result.Overwrite(16, ((ushort)(result.Length - 14)).ToBytes());  // Set IPv4 message size

                result.Overwrite(38, ((ushort)(result.Length - 34)).ToBytes());  // Set UDP message size

                result.Overwrite(24, new byte[] { 0x00, 0x00 }); // clear header checksum, so that calc excludes the checksum itself
                result.Overwrite(24, result.InternetChecksum(20, 14)); // header checksum

                result.Overwrite(40, new byte[] { 0x00, 0x00 }); // clear UDP Checksum

                Adapter.nic.SendFrame(result);  // Send the packet out into the ether....!

                Debug.WriteLine("LLMNR Response sent");
            }
        }
    }
}
