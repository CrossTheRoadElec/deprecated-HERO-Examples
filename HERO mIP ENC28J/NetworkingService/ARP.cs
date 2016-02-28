// This file is part of mIP - the Managed TCP/IP Stack.
// Hosted on Codeplex: http://mip.codeplex.com
// mIP is free software licensed under the Apache License 2.0
// © Copyright 2012 ValkyrieTech, LLC

using System;
using Microsoft.SPOT.Hardware;
using System.Diagnostics;

namespace Networking
{
    internal class ARP
    {
        private static byte[] scratch = new byte[42] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x08, 0x06, 0x00, 0x01, 0x08, 0x00, 0x06, 0x04, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x00, 0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] Request = new byte[1] { 0x01 };
        private static readonly byte[] Reply = new byte[1] { 0x02 };
        //private static object oLock = null;

        public static object[] PacketTypes
        {
            get { throw new NotImplementedException(); }
        }

        public static void HandlePacket(byte[] payload)
        {
            //Debug.WriteLine("Received ARP Packet -- " + payload.Length + " bytes");

            //Note: Target Mac is sometimes FFFFFFFFFFFF, or 000000000000, or this device MAC.  So, I am just going to allow all 3...

            // TODO: if the arp was for the identification of the gateway MAC, then parse and store that...
            if (payload[21] == 0x01 && (
                (payload[32] == 0xff && payload[33] == 0xff && payload[34] == 0xff && payload[35] == 0xff && payload[36] == 0xff && payload[37] == 0xff) || 
                (payload[32] == 0x00 && payload[33] == 0x00 && payload[34] == 0x00 && payload[35] == 0x00 && payload[36] == 0x00 && payload[37] == 0x00) ||
                (payload[32] == Adapter.MacAddress[0] && payload[33] == Adapter.MacAddress[1] && payload[34] == Adapter.MacAddress[2] && payload[35] == Adapter.MacAddress[3] && payload[36] == Adapter.MacAddress[4] && payload[37] == Adapter.MacAddress[5])))
            {
                // Handle a new request
                SendARP_Reply(Utility.ExtractRangeFromArray(payload, 6, 6), Utility.ExtractRangeFromArray(payload, 28, 4));
            }
            else if (payload[21] == 0x02)
            {
                // Process the incoming reply
                if (Adapter.Gateway != null && payload[28] == Adapter.Gateway[0] && payload[29] == Adapter.Gateway[1] && payload[30] == Adapter.Gateway[2] && payload[31] == Adapter.Gateway[3])
                {
                    Debug.WriteLine("Updating Gateway Mac from ARP");
                    
                    // This was an ARP from the gateway, let's update the Gateway Mac!
                    if (Adapter.GatewayMac == null && Adapter.DhcpDisabled && Adapter.IPAddress != null)
                    {
                        // When using a static address, we can't do anything until we have the GatewayMac, so once it is set, we release the StartupHold
                        Adapter.GatewayMac = Utility.ExtractRangeFromArray(payload, 22, 6);
                        Adapter.startupHold.Set();  // This will release the Adapter.Start() Method!  (if waiting)
                    }
                    else
                    {
                        Adapter.GatewayMac = Utility.ExtractRangeFromArray(payload, 22, 6);
                    }
                }
            }
            //else
            //{
            //    Debug.WriteLine("Did not sent a reply ARP");
            //}

        }


        /// <summary>
        /// Send Gratuitus ARP
        /// </summary>
        public static void SendARP_Gratuitus()
        {
            scratch.Overwrite(0, Adapter.BroadcastMAC);
            scratch.Overwrite(6, Adapter.MacAddress);
            scratch.Overwrite(21, ARP.Request);
            scratch.Overwrite(22, Adapter.MacAddress);
            scratch.Overwrite(28, Adapter.IPAddress ?? new byte[4] { 0x00, 0x00, 0x00, 0x00 });
            scratch.Overwrite(32, Adapter.BlankMAC);
            scratch.Overwrite(38, Adapter.IPAddress ?? new byte[4] { 0x00, 0x00, 0x00, 0x00 });

            Adapter.nic.SendFrame(scratch);
        }

        /// <summary>
        /// Probing ARP to determine if an IP is in use
        /// </summary>
        public static void SendARP_Probe(byte[] ipAddressToQuery)
        {
            if (ipAddressToQuery == null) return;

            scratch.Overwrite(0, Adapter.BroadcastMAC);
            scratch.Overwrite(6, Adapter.MacAddress);
            scratch.Overwrite(21, ARP.Request);
            scratch.Overwrite(22, Adapter.MacAddress);
            scratch.Overwrite(28, Adapter.IPAddress ?? new byte[4] { 0x00, 0x00, 0x00, 0x00 } );
            scratch.Overwrite(32, Adapter.BroadcastMAC);
            scratch.Overwrite(38, ipAddressToQuery);

            Adapter.nic.SendFrame(scratch);
        }

        /// <summary>
        /// ARP Reply
        /// </summary>
        private static void SendARP_Reply(byte[] destinationMac, byte[] destinationIP)
        {
            scratch.Overwrite(0, destinationMac);
            scratch.Overwrite(6, Adapter.MacAddress);
            scratch.Overwrite(21, ARP.Reply);
            scratch.Overwrite(22, Adapter.MacAddress);
            scratch.Overwrite(28, Adapter.IPAddress ?? new byte[4] { 0x00, 0x00, 0x00, 0x00 });
            scratch.Overwrite(32, destinationMac);
            scratch.Overwrite(38, destinationIP);

            Adapter.nic.SendFrame(scratch);
        }

        ///// <summary>
        ///// Seperate send method to share a lock so that we don't mix up packets...
        ///// </summary>
        ///// <param name="packetType"></param>
        //private static void SendArp(byte[] packetType)
        //{
        //    //TODO: Implement this!  
        //    lock (oLock)
        //    {
        //        scratch.Overwrite(284, packetType);

        //        //Note: Checksum is set to 0 and is note calculated, but could be inserted at this point if necessary

        //        Networking.nic.SendFrame(scratch);
        //    }
        //}

    }
}
