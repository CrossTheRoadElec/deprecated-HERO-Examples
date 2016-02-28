// This file is part of mIP - the Managed TCP/IP Stack.
// Hosted on Codeplex: http://mip.codeplex.com
// mIP is free software licensed under the Apache License 2.0
// © Copyright 2012 ValkyrieTech, LLC

using System;
using System.Threading;
using System.Diagnostics;
using Microsoft.SPOT.Hardware;
using System.Collections;

namespace Networking
{
    public static class Adapter
    {
        /// <summary>
        /// Dumps tons of stuff to the Debug Console when set to true
        /// </summary>
        public static bool VerboseDebugging = false;

        //private static Thread responderThread = new Thread(MainService);

        internal static readonly byte[] BroadcastMAC = new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff };
        internal static readonly byte[] BlankMAC = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        internal static readonly byte[] BlankIPAddress = new byte[] { 0x00, 0x00, 0x00, 0x00 };
        internal static readonly byte[] BroadcastIPAddress = new byte[] { 0xff, 0xff, 0xff, 0xff };

        internal static ushort EphmeralPort = (ushort)((new Random()).Next(16380) + 49153);  // port should be in Range: 49152 to 65535; see http://en.wikipedia.org/wiki/Ephemeral_port

        internal static ushort IdentificationNum = 0;  // ID number for IPv4 packets

        internal static ManualResetEvent startupHold = new ManualResetEvent(false);
        private static TimeSpan LastInternetCheck = TimeSpan.MinValue;
        private static bool InternetUp = false;

        public delegate void TcpPacketReceivedEventHandler(Packet packet);

        /// <summary>
        /// Fires when the phy goes up or down
        /// </summary>
        public static event TcpPacketReceivedEventHandler OnTcpReceivedPacketEvent;

        public delegate void UdpPacketReceivedEventHandler(Packet packet);

        /// <summary>
        /// Fires when the phy goes up or down
        /// </summary>
        public static event UdpPacketReceivedEventHandler OnUdpReceivedPacketEvent;

        public delegate void HttpPacketReceivedEventHandler(HttpRequest request);

        /// <summary>
        /// Fires when the phy goes up or down
        /// </summary>
        public static event HttpPacketReceivedEventHandler OnHttpReceivedPacketEvent;


        /// <summary>
        /// 
        /// </summary>
        private static Hashtable ListeningPorts = new Hashtable();

        /// <summary>
        /// When a packet is received on the specified port, the Packet Received event will fire
        /// </summary>
        /// <param name="portNumber"></param>
        public static void ListenToPort(ushort portNumber)
        {
            if (!ListeningPorts.Contains(portNumber)) ListeningPorts.Add(portNumber, portNumber.ToBytes());
        }

        /// <summary>
        /// When a packet is received on the specified port, the Packet Received event will fire
        /// </summary>
        /// <param name="portNumber"></param>
        public static bool IsListening(ushort portNumber)
        {
            return ListeningPorts.Contains(portNumber);
        }

        /// <summary>
        /// All packets sent to this port will be discarded/ignored
        /// </summary>
        /// <param name="portNumber"></param>
        public static void StopListeningToPort(ushort portNumber)
        {
            if (ListeningPorts.Contains(portNumber)) ListeningPorts.Remove(portNumber);
        }

        static Timer PollingTimer = new Timer(new TimerCallback(PollNow), null, 5000, 10000);

        internal static bool AreRenewing = false;

        /// <summary>
        /// Device name (for Netbios and mDNS); Do not include the .local on the end.  Just letters.  Also, there is probably a character limit (probably 32), so keep it short.  
        /// </summary>
        public static string Name
        {
            get
            {
                return _name ?? string.Empty;
            }

            private set
            {
                _name = (value == null) ? string.Empty : value.ToLower().Trim();
                //_encodedName = NetBiosNaming.EncodeNetbiosName(value);
            }
        }

        /// <summary>
        /// The Globally Unique MAC address for this device
        /// </summary>
        public static byte[] MacAddress { get; private set; }

        /// <summary>
        /// Automatic/Dynamic IP Address assignment (DHCP); set to TRUE if you want to manually assign an IP address (static)
        /// </summary>
        public static bool DhcpDisabled { get; set; }

        private static byte[] _ip;
        /// <summary>
        /// IP Address of this device.  To assign a static address, Directly set this value as the desired IP address and set DhcpDisabled to TRUE
        /// </summary>
        public static byte[] IPAddress
        {
            get
            {
                return _ip;
            }

            set
            {
                Debug.WriteLine("Setting IP Address to " + value.ToAddress());
                _ip = value;
                //PollNow(null);
            }
        }

        /// <summary>
        /// IP Address of Primary Domain Name Server (DNS).  
        /// Note: this will be overwritten with one provided by the router if DHCP is enabled and router provides a DNS server.
        /// </summary>
        public static byte[] DomainNameServer { get; set; }

        /// <summary>
        /// Secondary DNS server.  Never automatically assigned.  You have to set this value.  
        /// It will be used if it is populated and the primary DNS server fails.  
        /// </summary>
        public static byte[] DomainNameServer2 { get; set; }

        private static byte[] _gateway = null;
        public static byte[] Gateway
        {
            get { return _gateway; }

            set
            {
                if (value == null)
                {
                    GatewayMac = null;
                }
                else
                {
                    if (_gateway == null || !_gateway.BytesEqual(value))
                    {
                        _gateway = value;
                        //PollNow(null);  // This will update the Gateway Mac
                    }
                }

                _gateway = value;
            }
        }
        internal static byte[] GatewayMac { get; set; }
        public static byte[] SubnetMask { get; set; }
        public static uint LeaseRenewTime { get; internal set; }

        /// <summary>
        /// Interrupt Pin of SPI Bus
        /// </summary>
        internal static Cpu.Pin IntPin { get; private set; }

        /// <summary>
        /// ChipSelect Pin of SPI Bus
        /// </summary>
        internal static Cpu.Pin CSPin { get; private set; }

        /// <summary>
        /// The SPI Bus port
        /// </summary>
        internal static SPI.SPI_module SpiPort { get; private set; }

        internal static ENC28J60Driver nic = null;
        private static string _name;
        //internal static byte[] _encodedName;

        /// <summary>
        /// Start Networking!  If you want to use a static IP, make sure to set it before you start! 
        /// Note: This call may block for up to 15 seconds waiting for the IP Address assignment.  
        /// </summary>
        /// <param name="MacAddress">6-bytes the specify the Globally unique MAC address</param>
        /// <param name="name">Local Name of this device</param>
        /// <param name="spiBus"></param>
        /// <param name="resetPin"></param>
        /// <param name="interruptPin"></param>
        /// <param name="chipSelectPin"></param>
        public static void Start(byte[] MacAddress, string name, SPI.SPI_module spiBus, Cpu.Pin interruptPin, Cpu.Pin chipSelectPin)
        {
            Microsoft.SPOT.Debug.Assert(MacAddress != null, "MAC Address must be set to start networking.");
            Adapter.MacAddress = MacAddress;

            // Setting the Name to null will turn off naming...
            Adapter.Name = name;
            Adapter.SpiPort = spiBus;
            Adapter.IntPin = interruptPin;
            Adapter.CSPin = chipSelectPin;

            //if (responderThread.ThreadState == ThreadState.Running) return;

            //startupHold.Reset();

            //if (responderThread.ThreadState == ThreadState.Unstarted)
            //{
            //    responderThread.Start();
            //}
            //else if (responderThread.ThreadState == ThreadState.Stopped || responderThread.ThreadState == ThreadState.Suspended)
            //{
            //    Debug.WriteLine("Networking Thread being Restarted!  Threadstate: " + responderThread.ThreadState.ToString());
            //    responderThread.Abort();
            //    Thread.Sleep(10);
            //    responderThread = new Thread(MainService);
            //}

            MainService();

            if (!Adapter.DhcpDisabled && (Adapter.DomainNameServer == null || Adapter.Gateway == null || Adapter.IPAddress == null))
            {
                if (!startupHold.WaitOne(10000, true)) Debug.WriteLine("WARNING!  Time out while waiting for DHCP, check your Interface Profile and connections to ENC28J60 Controller");  // wait 10 seconds for DHCP and DNS assignment
            }
            else if (Adapter.DhcpDisabled && Adapter.IPAddress != null && Adapter.Gateway != null && GatewayMac == null)
            {
                if (!startupHold.WaitOne(10000, true)) Debug.WriteLine("WARNING!  Time out while waiting for Gateway to Respond to ARP request");  // wait 10 seconds for DHCP and DNS assignment
            }
            else
            {
                Debug.WriteLine("WARNING!  Networking is not properly configured to start.  ");
            }

        }

        /// <summary>
        /// Start Networking!  If you want to use a static IP, make sure to set it before you start!  
        /// Note: This call may block for up to 15 seconds waiting for the IP Address assignment.  
        /// </summary>
        public static void Start(byte[] MacAddress, string name = "MIP", InterfaceProfile profile = InterfaceProfile.Hero_Socket1_ENC28)
        {
            // Setup connection from Profile
            switch (profile)
            {
                case InterfaceProfile.Hero_Socket1_ENC28:
                    Start(MacAddress, name, SPI.SPI_module.SPI4, (Cpu.Pin)0x02, (Cpu.Pin)0x4A); // PA2=INT, PE10=CS
                    break;
                case InterfaceProfile.Hero_Socket8_ENC28:
                    Start(MacAddress, name, SPI.SPI_module.SPI4, (Cpu.Pin)0x20, (Cpu.Pin)0x34); // PC0=INT, PD14=CS
                    break;
                default:
                    Start(MacAddress, name, SPI.SPI_module.SPI4, (Cpu.Pin)0x02, (Cpu.Pin)0x4A); // PA2=INT, PE10=CS
                    break;
            }
        }


        //public static void Reset()
        //{
        //    nic.Restart();
        //}

        public static void Stop()
        {
            //if (responderThread == null) return;
            Debug.WriteLine("Stopping Network");

            try
            {
                PollingTimer.Change(Timeout.Infinite, Timeout.Infinite);
                Adapter.IPAddress = null;

                //nic.OnLinkChangedEvent = null;
                //nic.OnFrameArrived = null;

                //responderThread.Abort();
            }
            catch { }
        }

        private static void MainService()
        {
            if (nic != null && nic is ENC28J60Driver) return;

            nic = new ENC28J60Driver(Adapter.IntPin, Adapter.CSPin, Adapter.SpiPort);

            // event handler for link status changes (i.e. up/down)
            nic.OnLinkChangedEvent += new ENC28J60Driver.LinkChangedEventHandler(nic_OnLinkChangedEvent);

            // event handler for incoming packet arrival
            nic.OnFrameArrived += new ENC28J60Driver.FrameArrivedEventHandler(nic_OnFrameArrived);

            // NOTE: Don't call any other functions until started
            nic.Start(MacAddress);

            //PollingTimer.Change(10000, 10000);
        }

        static void PollNow(object o)
        {
            //Debug.WriteLine("Poll Now");

            if (nic != null && nic.IsLinkUp)
            {
                if (!Adapter.DhcpDisabled && AreRenewing && IPAddress != null)
                {
                    DHCP.SendMessage(DHCP.Request);
                }
                else if (!Adapter.DhcpDisabled && IPAddress == null)
                {
                    DHCP.SendMessage(DHCP.Discover);
                }

                if (Adapter.IPAddress != null && Adapter.Gateway != null && Adapter.GatewayMac == null) ARP.SendARP_Probe(Adapter.Gateway);

                if (IPAddress != null) ARP.SendARP_Gratuitus();
            }
        }


        /// <summary>
        /// True means you are connected to the Internet.  False means you are not able to reach computers on the Internet, although local computers may be reachable
        /// Note: this call is expensive and synchronous, so don't call it a lot!  Use it Judiciously!  
        /// Also, if you call this more frequently than every 5 seconds, you will get the last cached value if ethernet is connected!
        /// </summary>
        public static bool ConnectedToInternet
        {
            get
            {
                if (!ConnectedToEthernet) return false;

                if (LastInternetCheck > TimeSpan.MinValue && LastInternetCheck > Microsoft.SPOT.Hardware.PowerState.Uptime.Subtract(new TimeSpan(0, 0, 5)))
                {
                    // This prevents hitting the server more than once every 5 seconds.  So someone could put the ConnectedToInternet Property in a loop and it would
                    // not try to hammer the server and potentially get blocked by the server for a DoS attack.  
                    //Debug.WriteLine("Responding with cached value.");

                    return InternetUp;
                }

                LastInternetCheck = Microsoft.SPOT.Hardware.PowerState.Uptime;

                HttpResponse response = null;

                try
                {
                    var r = new HttpRequest("http://www.msftncsi.com/ncsi.txt");
                    r.Headers.Add("Accept", "*/*");  // Add custom properties to the Request Header
                    response = r.Send();
                }
                catch { }  // ignore exceptions!  eek!

                InternetUp = response != null;

                return InternetUp;

                //Debug.WriteLine("Response: " + response.Message);

                // ping something on the internet, or hit msftncsi.com just like Windows PCs do...
                // Great info here: http://blog.superuser.com/2011/05/16/windows-7-network-awareness/
                // Also, since the call can't block, we'll have to hit the server every 10 seconds or so... eek! 
            }
        }

        /// <summary>
        /// True means that an Ethernet cable is plugged in and you have established a link to something...  False if the cable is unplugged or the network is not there...
        /// </summary>
        public static bool ConnectedToEthernet
        {
            get
            {
                try
                {
                    if (nic == null) return false;
                    return nic.IsLinkUp;
                }
                catch
                {
                    return false;
                }
            }
        }

        // event handler for link status changes
        static void nic_OnLinkChangedEvent(ENC28J60Driver sender, DateTime time, bool isUp)
        {
            Debug.WriteLine("Link is now " + (isUp ? "up :)" : "down :("));

            if (isUp && (Adapter.IPAddress == null || !DhcpDisabled))
            {
                PollingTimer.Change(500, 10000);  // Get DHCP addresses now that link is up
            }
            else if (isUp && Adapter.IPAddress != null && DhcpDisabled && Adapter.Gateway != null)
            {
                PollingTimer.Change(500, 10000);  // Get Gateway when setup for static ip and link comes up
            }
            else if (!isUp && !DhcpDisabled)
            {
                // Link is down, so when it comes back up, if we were using DHCP, we want to renew the address.  
                AreRenewing = true;
                PollingTimer.Change(500, 7000);
            }
        }

        // event handler for new ethernet frame arrivals
        static void nic_OnFrameArrived(ENC28J60Driver sender, byte[] frame, DateTime timeReceived)
        {
            //if (buf[0] == 0x01)
            //{
            //    Debug.WriteLine("Probable Multicast Message Detected - " + buf.Length.ToString());
            //    if (buf[29] == 0x65) Debug.WriteLine("IP ending in 101");
            //}

            //if (buf[29] == 0x65)
            //{
            //    Debug.WriteLine("IP ending in 101 - size: " + buf.Length.ToString());

            //    if (buf.Length == 541)
            //        Debug.WriteLine("What is this? ");
            //}

            //var packetID = Guid.NewGuid().ToString();

            if (frame == null) return;

            //Debug.WriteLine("Memory: " + Microsoft.SPOT.Debug.GC(false).ToString() + ", packetID = " + packetID + ", age = " + timeReceived.Subtract(DateTime.Now).Seconds + "s, size = " + frame.Length + ", addys = " + Utility.ExtractRangeFromArray(frame, 0, 14).ToAddress());



            if (frame[13] == 0x06 && frame[12] == 0x08)  // ARP Packet Type
            {
                if (IPAddress != null)
                {
                    // If message request, and IP matches ours, we need to respond!
                    if (frame[41] == IPAddress[3] && frame[40] == IPAddress[2] && frame[39] == IPAddress[1] && frame[38] == IPAddress[0])
                    {
                        ARP.HandlePacket(frame);
                    }
                    else if (frame[21] == 0x02 && frame[31] == IPAddress[3] && frame[30] == IPAddress[2] && frame[29] == IPAddress[1] && frame[28] == IPAddress[0])
                    {
                        Debug.WriteLine("Possible IP Address Conflict Detected");
                        Adapter.Stop();  // IP Address Conflict!
                                         //TODO: if DHCP is enabled, don't stop the networking!  Just reset and get a new IP!!!!
                    }
                }
            }
            else if (frame[13] == 0x00 && frame[12] == 0x08)  // Handle IP packets
            {
                if (frame[23] == 0x01)  // Protocol 1 -- PING
                {
                    // Debug.WriteLine("Received ICMP (Ping) Packet -- " + frame.Length + " bytes");

                    ICMP.HandlePacket(frame);
                }
                else if (frame[23] == 0x11)  // Protocol 17 -- UDP
                {
                    if (frame[37] == 0x44 && !DhcpDisabled && frame[36] == 0x00)  // DHCP port 68  -- Order of conditions to short-circuit earlier!
                    {
                        //Debug.WriteLine("Received DHCP Packet -- " + frame.Length + " bytes");

                        DHCP.HandlePacket(frame);
                    }
                    else if (frame[37] == 0x89 && frame[36] == 0x00 && Name != null && Name != string.Empty && IPAddress != null)  // NetBIOS port 137 and name is set
                    {
                        //Debug.WriteLine("Received NBNS Packet -- " + frame.Length + " bytes");

                        // Uncomment the line below to enable Netbios over TCP Name resolution
                        NetBiosNaming.HandlePacket(frame);
                    }
                    else if (frame[35] == 0x35 && frame[34] == 0x00)  // DNS Source Port of 53 (0x35h)
                    {
                        //Debug.WriteLine("Received DNS Packet -- " + frame.Length + " bytes");

                        DNS.HandlePacket(frame);
                    }
                    else if (frame[37] == 0xe9 && frame[36] == 0x14 && frame[35] == 0xe9 && frame[34] == 0x14 && Name != null && Name != string.Empty && IPAddress != null) // mDNS Source and Destination Port of 5353 or LLMNR Destination Port of 5355
                    {
                        //Debug.WriteLine("Received MDNS Packet -- " + frame.Length + " bytes");

                        MDNS.HandlePacket(frame);
                    }
                    else if (frame[37] == 0xeb && frame[36] == 0x14 && Name != null && Name != string.Empty && IPAddress != null)
                    {
                        // Debug.WriteLine("Received LLMNR Packet -- " + frame.Length + " bytes");

                        LLMNR.HandlePacket(frame);
                    }
                    else if (OnUdpReceivedPacketEvent != null && IPAddress != null)  // Check Listening ports
                    {
                        //Debug.WriteLine("Received UDP Packet -- " + frame.Length + " bytes");

                        foreach (byte[] aPort in ListeningPorts.Values)
                            if (aPort[0] == frame[36] && aPort[1] == frame[37]) UDP.HandlePacket(frame);
                    }
                }
                else if (frame[23] == 0x06 && IPAddress != null)  // Protocol 6 -- TCP
                {
                    //Debug.WriteLine("Received TCP Packet -- " + frame.Length + " bytes");

                    foreach (byte[] aPort in ListeningPorts.Values)
                    {
                        if (aPort[0] == frame[36] && aPort[1] == frame[37])
                        {
                            TCP.HandlePacket(frame);
                            return;
                        }
                    }

                    // Handle a response from a currently open connection
                    ulong conID = TCP.GenerateConnectionID(frame);
                    if (TCP.Connections.Contains(conID))
                        TCP.HandlePacket(frame);
                    //else
                    //TODO: Send a RST as a response to a closed port.  

                    //var port = (new byte[2] { frame[36], frame[37] }).ToShort();

                    //foreach (Connection aCon in TCP.Connections)
                    //{
                    //    if (aCon.LocalPort == port) 
                    //        TCP.HandlePacket(frame); 
                    //    return;
                    //}

                }
            }

            // All other packets are ignored... like throwing back a fish :)

            //Debug.WriteLine("Memory: " + Microsoft.SPOT.Debug.GC(false).ToString() + ", packetID = " + packetID);


            //Microsoft.SPOT.Debug.EnableGCMessages(true);
        }

        internal static void FireTcpPacketEvent(byte[] packet, uint seqNumber, Connection socket)
        {
            if (OnTcpReceivedPacketEvent != null)
                OnTcpReceivedPacketEvent.Invoke(new Packet(PacketType.TCP) { SequenceNumber = seqNumber, Content = packet, Socket = socket });
        }

        internal static void FireHttpPacketEvent(byte[] packet, Connection socket)
        {
            try
            {
                if (OnHttpReceivedPacketEvent != null)
                    OnHttpReceivedPacketEvent.Invoke(new HttpRequest(packet, socket));
            }
            catch
            {
                //TODO: throwing an exception is expensive and could waste precious resources.  A simple Validation of the message could be very effective.  
                // if an http message is malformed, or it just isn't http, it will get caught here
                Debug.WriteLine("A bad Request was received and ignored. ");
            }
        }

        internal static void FireUdpPacketEvent(byte[] packet, Connection socket)
        {
            OnUdpReceivedPacketEvent.Invoke(new Packet(PacketType.UDP) { Content = packet, Socket = socket });
        }
    }

    /// <summary>
    /// Profiles for how the Processor/board is connected to the Networking Controller
    /// </summary>
    public enum InterfaceProfile
    {
        /// <summary>
        /// The Gadgeteer ENC28 module connected to HERO on Socket 1
        /// </summary>
        Hero_Socket1_ENC28,
        /// <summary>
        /// The Gadgeteer ENC28 module connected to HERO on Socket 8
        /// </summary>
        Hero_Socket8_ENC28
    };
}
