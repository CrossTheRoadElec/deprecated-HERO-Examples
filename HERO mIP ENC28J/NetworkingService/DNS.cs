// This file is part of mIP - the Managed TCP/IP Stack.
// Hosted on Codeplex: http://mip.codeplex.com
// mIP is free software licensed under the Apache License 2.0
// © Copyright 2012 ValkyrieTech, LLC

using System;
//using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using System.Text;
using System.Collections;
using System.Diagnostics;
using System.Threading;

namespace Networking
{
    /// <summary>
    /// Domain name lookup of IP address
    /// </summary>
    public static class DNS
    {
        //TODO: Implement this...  DHCP already implements UDP, so this should borrow from that and eventually DHCP probably could use this class
        private static byte[] scratch = new byte[54] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x45, 0x00, 0x00, 0x38, 0x78, 0xc0, 0x00, 0x00, 0x80, 0x11, 0x3d, 0x50, 0xc0, 0xa8, 0x01, 0x56, 0xc0, 0xa8, 0x01, 0xfe, 0xc7, 0x47, 0x00, 0x35, 0x00, 0x24, 0x9f, 0xc9, 0x00, 0x02, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        private static object lockObj = new Object();
        private static AutoResetEvent dnsWaitHandle = new AutoResetEvent(false);
        private static string syncDnsLookupQuery = string.Empty;
        private static byte[] syncDnsLookupResult = null;
        private const int cacheMax = 5;

        private static ArrayList dnsCache = new ArrayList();

        public delegate void DNSLookupReceivedEventHandler(string domainName, byte[] ipAddress);

        /// <summary>
        /// Fires when the phy goes up or down
        /// </summary>
        public static event DNSLookupReceivedEventHandler OnDNSLookupEvent;

        /// <summary>
        /// Take care of a packet of DNS stuff
        /// </summary>
        /// <param name="payload"></param>
        internal static void HandlePacket(byte[] payload)
        {

            if (Adapter.VerboseDebugging) Debug.WriteLine("DNS Response");

            //TODO: parse out the first IP Address from the answer and add it to a hashtable with the expiration and the name as the key (ie. google.com)
            //TODO: also, fire a DNS Entries Updated event the the latest entry as a parameter

            if (OnDNSLookupEvent == null && syncDnsLookupQuery == string.Empty) return;  // nobody is listening!  

            ushort ipHeaderLength = (ushort)((payload[14] & 0x0f) * 4);

            bool isResponse = (payload[24 + ipHeaderLength] & (1 << 7)) != 0;  // DNS Response ?
            if (!isResponse) return;
            if (payload[26 + ipHeaderLength] != 0x00 || payload[27 + ipHeaderLength] != 0x01) return;  // Only 1 question is allowed, because that is all we ever ask...
            if (payload[28 + ipHeaderLength] == 0x00 && payload[29 + ipHeaderLength] == 0x00) return;  // We need at least 1 answer, so anything other than 0 is fine...

            // Parse out the Query Name (and use it in all the answers)
            var name = DecodeDnsName(payload, 34 + ipHeaderLength);

            //TODO: This sucks.  It should calculate the start by parsing the Queries first
            var startOfAnswers = 40 + ipHeaderLength + name.Length;
            var answerCount = Utility.ExtractRangeFromArray(payload, 48, 2).ToShort();

            var answers = ParseAnswers(payload, startOfAnswers, answerCount);

            foreach (var answer in answers)
            {
                if (answer.Type == 1)
                {
                    answer.Name = name;

                    if (dnsCache.Contains(answer))
                        dnsCache[dnsCache.IndexOf(answer)] = answer;
                    else
                        dnsCache.Add(answer);

                    if (name == syncDnsLookupQuery)
                    {
                        syncDnsLookupResult = answer.Value;
                        dnsWaitHandle.Set();
                    }
                    else
                    {
                        OnDNSLookupEvent.Invoke(name, answer.Value);
                    }

                    RemoveExpiredAnswers();

                    return;
                }
            }

            //var addy = new byte[4] { payload[52 + ipHeaderLength + name.Length],
            //                         payload[53 + ipHeaderLength + name.Length],
            //                         payload[54 + ipHeaderLength + name.Length],
            //                         payload[55 + ipHeaderLength + name.Length] };
            
            //Debug.WriteLine("Fire New DNS Event!  " + name + " = " + addy.ToAddress());

            //if (OnDNSLookupEvent != null) OnDNSLookupEvent.Invoke(name, addy);
        }

        private static void RemoveExpiredAnswers()
        {
            // removes expired entries as long as there are at most cacheMax entries

            for (int i = dnsCache.Count - 1; i >= 0 && dnsCache.Count >= cacheMax; i--)
            {
                if ((dnsCache[i] as DnsAnswer).IsExpired()) dnsCache.RemoveAt(i);
            }
        }

        /// <summary>
        /// Parse answers into a typed array
        /// </summary>
        /// <param name="paylod"></param>
        /// <returns></returns>
        private static DnsAnswer[] ParseAnswers(byte[] payload, int start, uint count)
        {
            if (Adapter.VerboseDebugging) Debug.WriteLine("DNS Answers");

            var result = new DnsAnswer[count];
            ushort currentSize = 0;
            var position = start;

            try
            {
                for (ushort i = 0; i < count; i++)
                {
                    currentSize = Utility.ExtractRangeFromArray(payload, position + 10, 2).ToShort();

                    result[i] = new DnsAnswer()
                    {
                        Type = Utility.ExtractRangeFromArray(payload, position + 2, 2).ToShort(),
                        Expiration = Microsoft.SPOT.Hardware.PowerState.Uptime.Add(new TimeSpan(TimeSpan.TicksPerSecond * Utility.ExtractRangeFromArray(payload, position + 6, 4).ToInt())),
                        Value = Utility.ExtractRangeFromArray(payload, position + 12, currentSize)
                    };
    
                    position += currentSize + 12;  // move pointer to start of next answer
                }                

            }
            catch 
            {
                Debug.WriteLine("Warning: Failed to parse DNS answers"); 
                return new DnsAnswer[0];  // return an empty array...
            }  

            return result;
        }

        /// <summary>
        /// Does a DNS lookup.  This is a synchronous call, so it will block you program until the timeout or the response is recieved.
        /// If the timeout happens, the result returned will be a null.  Also, if an IP address is passed in, it will just be returned back to you, since no lookup is necessary.  
        /// </summary>
        /// <param name="dnsName">the name you need to look up.  Like "bing.com"</param>
        /// <param name="timeout">How long to wait for a response from the DNS server before giving up.  Units are seconds</param>
        /// <returns>The IP Address of the result, or a null if a timeout happens</returns>
        public static byte[] Lookup(string dnsName, short timeout = 3)
        {
            dnsName = dnsName.Trim();
            if (dnsName.Length == 0) return null;

            foreach (DnsAnswer anAnswer in dnsCache)
            {
                if (anAnswer.Name == dnsName && !anAnswer.IsExpired())
                    return anAnswer.Value;
            }

            lock (lockObj)
            {
                syncDnsLookupQuery = dnsName;
                syncDnsLookupResult = dnsName.ToBytes(); // if the dnsname is already an IP address, just convert to bytes and return that! 

                if (syncDnsLookupResult == null)
                {
                    //dnsWaitHandle.Reset();
                    //Debug.WriteLine("1 " + DateTime.Now.ToString());

                    LookupAsync(dnsName);

                    // Block here until connection is made or timeout happens!  
                    if (!dnsWaitHandle.WaitOne(timeout * 1000, true) && Adapter.DomainNameServer2 != null)
                    {
                        Debug.WriteLine("Primary DNS Failed, trying Secondary... " + DateTime.Now.ToString());

                        // we timed out :(
                        // let's try the secondary server?  
                        LookupAsync(dnsName, true);

                        dnsWaitHandle.WaitOne(timeout * 1000, true);
                        Debug.WriteLine("2 " + DateTime.Now.ToString());

                    }

                //Debug.WriteLine("Connection Open => " + this.IsOpen);
                }

                if (syncDnsLookupResult == null)
                {
                    // Let's use even an expired DNS Entry!  
                    foreach (DnsAnswer anAnswer in dnsCache)
                    {
                        if (anAnswer.Name == dnsName)
                        {
                            Debug.WriteLine("Using an Expired DNS Entry!  ");
                            return anAnswer.Value;
                        }
                    }

                    throw new Exception("Domain Name lookup for " + dnsName + " failed. ");                
                }


                return syncDnsLookupResult;
            }
        }


        /// <summary>
        /// Lookup the IP Addess of the passed in domain name.  Note: DNS address must be set.  
        /// </summary>
        /// <param name="packetType"></param>
        public static void LookupAsync(string dnsName, bool useSecondaryDnsServer = false)
        {           
            if (Adapter.DomainNameServer == null) throw new Exception("Domain Name Server is not set.  If you are using DHCP, you must wait until Adapter.DomainNameServer is populated before you attempt to make a DNS call.");
            if (Adapter.GatewayMac == null) throw new Exception("Gateway MAC is not set.  If you are using DHCP, you must wait until Adapter.GatewayMac is populated before you attempt to make a DNS call.");

            scratch.Overwrite(0, Adapter.GatewayMac);          // Destination MAC Address
            scratch.Overwrite(6, Adapter.MacAddress);            // Source MAC Address
            scratch.Overwrite(30, (useSecondaryDnsServer && Adapter.DomainNameServer2 != null) ? Adapter.DomainNameServer2 : Adapter.DomainNameServer);   // Destiantion IP Address
            scratch.Overwrite(26, Adapter.IPAddress);       // Source IP Address

            lock (lockObj)
             {
                scratch.Overwrite(42, Extensions.GetRandomBytes(2));  // Make up some transaction ID, Write the transaction ID

                var result = Utility.CombineArrays(scratch, Utility.CombineArrays(EncodeDnsName(dnsName), new byte[4] { 0x00, 0x01, 0x00, 0x01 }));

                result.Overwrite(16, ((ushort)(result.Length - 14)).ToBytes());  // Set IPv4 message size

                result.Overwrite(38, ((ushort)(result.Length - 34)).ToBytes());  // Set UDP message size

                result.Overwrite(24, new byte[] { 0x00, 0x00 }); // clear header checksum, so that calc excludes the checksum itself
                result.Overwrite(24, result.InternetChecksum(20, 14)); // header checksum

                result.Overwrite(40, new byte[] { 0x00, 0x00 }); // clear UDP Checksum

                Adapter.nic.SendFrame(result);  // Send the packet out into the ether....!
            }
        }

        internal static byte[] EncodeDnsName(string name)
        {
            name = name.ToLower();
            var parts = name.Split('.');
            var result = new byte[name.Length + 2];
            int i = 0;

            foreach (var aPart in parts)
            {
                result[i++] = (byte)(aPart.Length >> 0);


                for (int x = 0; x < aPart.Length; ++x)
                //foreach (var aChar in aPart)
                {
                    char aChar = aPart[i];
                    result[i++] = Encoding.UTF8.GetBytes(aChar.ToString())[0];
                }
            }

            return result;
        }

        internal static string DecodeDnsName(byte[] buffer, int start)
        {
            int i = start;
            var result = new byte[0];

            while (buffer[i] != 0x00)
            {
                if (i != start) result = Utility.CombineArrays(result, new byte[1] { 0x2e });
                result = Utility.CombineArrays(result, Utility.ExtractRangeFromArray(buffer, i + 1, buffer[i]));
                i = start + result.Length + 1;
            }

            return new string(UTF8Encoding.UTF8.GetChars(result));
        }
    }

    internal class DnsAnswer
    {
        public string Name { get; set; }
        public ushort Type { get; set; }  // Type A is an ip address, Type CNAME is a Canonical name for an alias
        public TimeSpan Expiration { get; set; }  // The system uptime of the expiration
        public byte[] Value { get; set; }

        public override bool Equals(object answer)
        {
            return this.Name == (answer as DnsAnswer).Name;
        }

        internal bool IsExpired()
        {
            return Microsoft.SPOT.Hardware.PowerState.Uptime > Expiration;
        }
    }
}
