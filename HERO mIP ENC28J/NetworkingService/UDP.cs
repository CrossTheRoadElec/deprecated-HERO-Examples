// This file is part of mIP - the Managed TCP/IP Stack.
// Hosted on Codeplex: http://mip.codeplex.com
// mIP is free software licensed under the Apache License 2.0
// © Copyright 2012 ValkyrieTech, LLC

using System;
using Microsoft.SPOT;
using System.Text;
using Microsoft.SPOT.Hardware;

namespace Networking
{
    public static class UDP
    {
        // Note: It can be very useful to have some UDP and TCP services to test against.  Windows has a few simple services that can be enabled by just turning on the feature
        // http://technet.microsoft.com/en-us/library/cc725973
        // http://www.windowsnetworking.com/articles_tutorials/windows-7-simple-tcpip-services-what-how.html

        //TODO: Implement this...  
        private static byte[] scratch = new byte[42] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x50, 0xe5, 0x49, 0xe4, 0x34, 0x8d, 0x08, 0x00, 0x45, 0x00, 0x00, 0x1d, 0x05, 0x01, 0x00, 0x00, 0x80, 0x11, 0x73, 0xcc, 0xc0, 0xa8, 0x01, 0x52, 0xff, 0xff, 0xff, 0xff, 0x06, 0xf2, 0x06, 0xf2, 0x00, 0x00, 0x5d, 0x88 };

        /// <summary>
        /// Take care of a packet of UDP data
        /// </summary>
        /// <param name="payload"></param>
        internal static void HandlePacket(byte[] payload)
        {
            ushort ipHeaderLength = (ushort)((payload[14] & 0x0f) * 4);
            
            byte[] SourceIP = Utility.ExtractRangeFromArray(payload, 26, 4);
            byte[] DestinationIP = Utility.ExtractRangeFromArray(payload, 30, 4);
            byte[] SourcePort = Utility.ExtractRangeFromArray(payload, 34, 2);
            byte[] DestinationPort = Utility.ExtractRangeFromArray(payload, 36, 2);

            var socket = new Connection() { RemoteIP = SourceIP, RemotePort = SourcePort.ToShort(), LocalPort = DestinationPort.ToShort() };

            ushort udpDataLength = (ushort)((new byte[2] { payload[38], payload[39] }).ToShort() - 8);

            //We got some data!?
            if (udpDataLength > 0)
                Networking.Adapter.FireUdpPacketEvent(Utility.ExtractRangeFromArray(payload, 42, udpDataLength), socket);
        }


        /// <summary>
        /// Seperate send method to share a lock so that we don't mix up packets...
        /// </summary>
        /// <param name="message"></param>
        /// <param name="destinationIP"></param>
        /// <param name="destinationPort"></param>
        /// <param name="sourcePort"></param>
        public static void SendUDPMessage(byte[] message, byte[] destinationIP, ushort destinationPort, ushort sourcePort)
        {
            scratch.Overwrite(0, Adapter.GatewayMac);          // Destination MAC Address
            scratch.Overwrite(6, Adapter.MacAddress);            // Source MAC Address
            scratch.Overwrite(30, destinationIP);   // Destiantion IP Address
            scratch.Overwrite(26, Adapter.IPAddress);       // Source IP Address

            //TODO: this should be a randomly selected port from a range of valid ones...?
            scratch.Overwrite(34, sourcePort.ToBytes());   // Source Port 
            scratch.Overwrite(36, destinationPort.ToBytes());  // Destination Port

     //       scratch.Overwrite(38, ((ushort)(28)).ToBytes());  // Set UDP message size
            scratch.Overwrite(38, ((ushort)(message.Length + 8)).ToBytes());  // Set UDP message size
            scratch.Overwrite(16, ((ushort)((scratch.Length + message.Length) - 14)).ToBytes());  // set the IPv4 Message size

            scratch.Overwrite(24, new byte[] { 0x00, 0x00 }); // clear header checksum, so that calc excludes the checksum itself
            scratch.Overwrite(24, scratch.InternetChecksum(20, 14)); // header checksum

            scratch.Overwrite(40, new byte[] { 0x00, 0x00 }); // clear UDP Checksum

            Adapter.nic.SendFrame(Utility.CombineArrays(scratch, message));  // Send the packet out into the ether....!
        }
    }
}
