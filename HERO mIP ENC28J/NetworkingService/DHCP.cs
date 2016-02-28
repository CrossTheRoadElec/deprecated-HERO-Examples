// This file is part of mIP - the Managed TCP/IP Stack.
// Hosted on Codeplex: http://mip.codeplex.com
// mIP is free software licensed under the Apache License 2.0
// © Copyright 2012 ValkyrieTech, LLC

using System;
using Microsoft.SPOT.Hardware;
using System.Collections;
using System.Threading;
using System.Diagnostics;

namespace Networking
{
    internal static class DHCP
    {
        private static byte[] scratch = new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x08, 0x00, 0x45, 0x00, 0x01, 0x48, 0x00, 0x00, 0x00, 0x00, 0xff, 0x11, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xff, 0xff, 0xff, 0xff, 0x00, 0x44, 0x00, 0x43, 0x01, 0x34, 0x00, 0x00, 0x01, 0x01, 0x06, 0x00, 0x92, 0xff, 0xf9, 0xef, 0x00, 0x0b, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x78, 0xd2, 0xd9, 0xd5, 0xef, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x63, 0x82, 0x53, 0x63, 0x35, 0x01, 0x01, 0x37, 0x06, 0x01, 0x03, 0x06, 0x0f, 0x77, 0xfc, 0x39, 0x02, 0x02, 0xee, 0x3d, 0x07, 0x01 };

        public const int TwoHoursInMilliseconds = 7200 * 1000;

        static Timer RenewTimer = new Timer(new TimerCallback(RenewNow), null, Timeout.Infinite, Timeout.Infinite);

        private static readonly byte[] MagicCookie = new byte[] { 0x63, 0x82, 0x53, 0x63 };
        private static byte[] transactionID = null;

        internal static readonly byte[] Request = new byte[1] { 0x03 };
        internal static readonly byte[] Discover = new byte[1] { 0x01 };
        private static byte[] PendingIpAddress = null;

        public static object[] PacketTypes
        {
            get { throw new NotImplementedException(); }
        }

        /// <summary>
        /// Take care of a packet of DHCP stuff
        /// </summary>
        /// <param name="payload"></param>
        public static void HandlePacket(byte[] payload)
        {
            //Debug.WriteLine("Handling DHCP packet");
            
            // Check Transaction ID!
            if (transactionID == null || payload[46] != transactionID[0] || payload[47] != transactionID[1] || payload[48] != transactionID[2] || payload[49] != transactionID[3]) return;
            
            // To determine the type, we need to find the magic cookie, then find option 0x35h
            // 02 == Offer, 05 == ACK, 06 = NAK
            var options = ParseOptions(payload);

            //Debug.WriteLine("DHCP PKT");

            if (options.Contains("53"))
            {
                //Debug.WriteLine("Rec'd DHCP OFFER - 1");

                if (((byte[])(options["53"]))[0] == 0x02)  // Offer
                {
                    //Debug.WriteLine("Rec'd DHCP OFFER");

                    ushort ipHeaderLength = (ushort)((payload[14] & 0x0f) * 4);
                    PendingIpAddress = Utility.ExtractRangeFromArray(payload, ipHeaderLength + 38, 4);
                    if (options.Contains("54")) Adapter.Gateway = (byte[])options["54"];  // DHCP Server
                    if (options.Contains("6")) Adapter.DomainNameServer = (byte[])options["6"];  // DNS Server
                    if (options.Contains("1")) Adapter.SubnetMask = (byte[])options["1"];  // Subnet
                    if (options.Contains("3")) Adapter.Gateway = (byte[])options["3"];  // Router
                    if (options.Contains("58")) RenewTimer.Change((int)(((byte[])options["58"]).ToInt() * 1050), TwoHoursInMilliseconds); // Got a Renew time
                    if (options.Contains("51")) RenewTimer.Change((int)(((byte[])options["51"]).ToInt() * 750), TwoHoursInMilliseconds); // Got a Lease Time (I am using 750, so we renew after 75% of lease has been consumed)
                    Adapter.GatewayMac = Utility.ExtractRangeFromArray(payload, 6, 6);  // Initial gateway MAC.  Will get confirmed/updated by an ARP Probe

                    SendMessage(DHCP.Request);
                }
                else if (((byte[])options["53"])[0] == 0x05)  // ACK or Acknowledgement
                {
                    // Parse out the Gateway, DNS Servers, IP address, and apply set all the variables with it...

                    //Debug.WriteLine("Rec'd DHCP ACK");

                    if (options.Contains("54")) Adapter.Gateway = (byte[])options["54"];  // DHCP Server
                    if (options.Contains("6")) Adapter.DomainNameServer = (byte[])options["6"];  // DNS Server
                    if (options.Contains("1")) Adapter.SubnetMask = (byte[])options["1"];  // Subnet
                    if (options.Contains("3")) Adapter.Gateway = (byte[])options["3"];  // Router
                    if (options.Contains("58")) RenewTimer.Change((int)(((byte[])options["58"]).ToInt() * 1050), TwoHoursInMilliseconds);  // Got a Renew time
                    if (options.Contains("51")) RenewTimer.Change((int)(((byte[])options["51"]).ToInt() * 750), TwoHoursInMilliseconds);  // Got a Lease Time (I am using 750, so we renew after 75% of lease has been consumed)
                    Adapter.GatewayMac = Utility.ExtractRangeFromArray(payload, 6, 6);  // Initial gateway MAC.  Will get confirmed/updated by an ARP Probe

                    transactionID = null;
                    Adapter.AreRenewing = false;
                    Adapter.IPAddress = PendingIpAddress ?? Adapter.IPAddress;

                    Adapter.startupHold.Set();  // This will release the Adapter.Start() Method!  (if waiting)

                    Debug.WriteLine("DHCP SUCCESS!  We have an IP Address - " + Adapter.IPAddress.ToAddress() + "; Gateway: " + Adapter.Gateway.ToAddress());

                    ARP.SendARP_Probe(Adapter.Gateway);  // Confirm Gateway MAC address
                }
                else if (((byte[])options["53"])[0] == 0x06)  // NACK or Not Acknowledged!
                {
                    Debug.WriteLine("DHCP N-ACK");
                    transactionID = null;
                    Adapter.AreRenewing = false;

                    // We have failed to get an IP address for some reason...!
                    Adapter.IPAddress = null;
                    Adapter.Gateway = null; 
                    Adapter.GatewayMac = null;
                }
            }
        }

        public static void RenewNow(object o)
        {
            Debug.WriteLine("Time to Renew the IP Address! ");
            RenewTimer.Change(Timeout.Infinite, TwoHoursInMilliseconds);
            transactionID = transactionID ?? Extensions.GetRandomBytes(4);  // Make up some transaction ID
            Adapter.AreRenewing = true;
        }

        /// <summary>
        /// Find Magic cookie and parse the options into a hashtable
        /// </summary>
        /// <param name="paylod"></param>
        /// <returns></returns>
        private static Hashtable ParseOptions(byte[] payload)
        {
            var result = new Hashtable();
            byte currentOption = 0x00;
            int currentSize = 0;
            var current = payload.Locate(MagicCookie) + MagicCookie.Length;

            try
            {
                while (payload[current] != 0xff)
                {
                    currentOption = payload[current++];
                    currentSize = payload[current++];

                    result.Add(currentOption.ToString(), Utility.ExtractRangeFromArray(payload, current, currentSize));
                    current += currentSize;
                }
            }
            catch {}  // ignore parsing errors?  This is ok for now but we'll need to revisit this later...  I hate concealing errors!

            return result;
        }

        /// <summary>
        /// Seperate send method to share a lock so that we don't mix up packets...
        /// </summary>
        /// <param name="packetType"></param>
        internal static void SendMessage(byte[] packetType)
        {
            
            lock (MagicCookie)
            {
                scratch.Overwrite(0, Adapter.BroadcastMAC);          // Destination MAC Address
                scratch.Overwrite(6, Adapter.MacAddress);            // Source MAC Address
                scratch.Overwrite(30, Adapter.BroadcastIPAddress);   // Destiantion IP Address
                scratch.Overwrite(54, Adapter.BlankIPAddress);       // Source IP Address
                scratch.Overwrite(70, Adapter.MacAddress);           // Source MAC Address Again inside the DHCP section                  
                scratch.Overwrite(284, packetType);

                byte[] options = new byte[13 + (Adapter.Name == string.Empty ? 0 : Adapter.Name.Length + 2)];
                options.Overwrite(0, Adapter.MacAddress);

                transactionID = transactionID ?? Extensions.GetRandomBytes(4);  // Make up some transaction ID

                if (packetType == DHCP.Discover)
                {
                   // Debug.WriteLine("Composing Discover Message");

                    PendingIpAddress = null;
                    //scratch.Overwrite(306, suffix);  // write the Discover suffix
                    options.Overwrite(6, new byte[6] { 0x33, 0x04, 0x00, 0x76, 0xa7, 0x00 });  // Request an IP lease time of 90 days
                }
                else if (packetType == DHCP.Request && PendingIpAddress != null && Adapter.Gateway != null)
                {
                    //Debug.WriteLine("Composing Request Message");

                    options.Overwrite(6, new byte[2] { 0x32, 0x04 });  // Set the option prefix for IP Address
                    options.Overwrite(8, PendingIpAddress);            // Set the option Value
                    options.Overwrite(12, new byte[2] { 0x36, 0x04 });  // Set the option prefix for Gateway
                    options.Overwrite(14, Adapter.Gateway);          // Set the option value

                    if (Adapter.GatewayMac != null && Adapter.Gateway != null && Adapter.IPAddress != null)
                    {
                        // This is for the renewals
                        //TODO: Test to make sure this works!  
                        //scratch.Overwrite(0, Adapter.GatewayMac);
                        //scratch.Overwrite(30, Adapter.Gateway);
                        scratch.Overwrite(54, Adapter.IPAddress);
                    }
                }
                else
                {
                    Debug.WriteLine("Odd DHCP situation... should we be concerned?");

                    return;
                }

                if (Adapter.Name != string.Empty)
                {
                    // Add Hostname option to Discover and Request messages
                    options.Overwrite(options.Length - (Adapter.Name.Length + 3), new byte[1] { 0x0c });
                    options.Overwrite(options.Length - (Adapter.Name.Length + 2), DNS.EncodeDnsName(Adapter.Name));
                }

                options.Overwrite(options.Length-1, new byte[1] { 0xFF });  // End of option section marker

                scratch.Overwrite(46, transactionID);  // Write the transaction ID

                var result = Utility.CombineArrays(scratch, options);

                result.Overwrite(16, ((ushort)(result.Length - 14)).ToBytes());  // Set IPv4 message size

                result.Overwrite(38, ((ushort)(result.Length - 34)).ToBytes());  // Set UDP message size

                result.Overwrite(24, new byte[] { 0x00, 0x00 }); // clear header checksum, so that calc excludes the checksum itself
                result.Overwrite(24, result.InternetChecksum(20, 14)); // header checksum

                result.Overwrite(40, new byte[] { 0x00, 0x00 }); // clear UDP Checksum

                Adapter.nic.SendFrame(result);  // Send the packet out into the ether....!
            }
        }
    }
}
