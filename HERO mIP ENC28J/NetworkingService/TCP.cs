// This file is part of mIP - the Managed TCP/IP Stack.
// Hosted on Codeplex: http://mip.codeplex.com
// mIP is free software licensed under the Apache License 2.0
// © Copyright 2012 ValkyrieTech, LLC

using System;
using Microsoft.SPOT.Hardware;
using System.Collections;
using System.Text;
using System.Web;
using System.IO;
using System.Diagnostics;
using System.Threading;

namespace Networking
{
    internal class TCP
    {
        //private static object oLock = null;
        private const ushort ConnectionIdleLimit = 30;  // Idle Connection time before closure

        internal static Hashtable Connections = new Hashtable(10);

        internal static void HandlePacket(byte[] payload)
        {
            bool SYN = (payload[47] & (1<<1)) != 0;  // Synchronize
            bool ACK = (payload[47] & (1<<4)) != 0;  // Acknoledge
            bool FIN = (payload[47] & (1<<0)) != 0;  // Finish
            bool PSH = (payload[47] & (1<<3)) != 0;  // Push
            bool RST = (payload[47] & (1<<2)) != 0;  // Reset

            byte[] SourceIP = Utility.ExtractRangeFromArray(payload, 26, 4);
            byte[] SourcePort = Utility.ExtractRangeFromArray(payload, 34, 2);
            byte[] LocalPort = Utility.ExtractRangeFromArray(payload, 36, 2);
            ulong connectionID = GenerateConnectionID(payload);
            Connection con = null;

            //TODO: Validate TCP checksum and reject packet if invalid!  

            uint packetSeqNumber = Utility.ExtractRangeFromArray(payload, 38, 4).ToInt(); 

            // Handle a new request
            // if this is a new connection SYN, add to the Connections collection
            if (SYN && !ACK)   // new connection
            {
                //Debug.WriteLine("SYN");

                if (Connections.Contains(connectionID) && (Connections[connectionID] as Connection).IsOpen) Connections.Remove(connectionID);  // remove old connection?

                // Prune old Connections
                
                var keys = new ulong[Connections.Count];
                Connections.Keys.CopyTo(keys, 0);

                foreach (var key in keys)
                {
                    con = Connections[key] as Connection;

                    if (Microsoft.SPOT.Hardware.Utility.GetMachineTime().Subtract((Connections[key] as Connection).LastActivity).Seconds > ConnectionIdleLimit)
                    {
                        con.isClosing = true;
                        //Adapter.StopListeningToPort(con.LocalPort);
                        //Debug.WriteLine("FIN -- Closing idle connection");
                        con.SeqNumber++;
                        con.SendAck(false, true);  // SendAck Fin/Ack
                        Connections.Remove(key);
                    }
                }

                if (!Connections.Contains(connectionID)) Connections.Add(connectionID, new Connection());

                con = Connections[connectionID] as Connection;
                
                con.RemoteIP = SourceIP;
                con.RemotePort = SourcePort.ToShort();
                con.RemoteMac = Utility.ExtractRangeFromArray(payload, 6, 6); 
                con.LocalPort = LocalPort.ToShort();
                con.SeqNumber = packetSeqNumber + 1;
                con.StartSeqNumber = packetSeqNumber;
                con.AckNumber = 2380; //Utility.ExtractRangeFromArray(payload, 42, 4).ToInt(), //TODO: this should be a random number initially?  
                con.WindowSize = 1024; //Utility.ExtractRangeFromArray(payload, 48, 2).ToShort() 

                con.ReadyForRequest = true;
                con.SendAck(true); // Syn/Ack

                con.AckNumber++;
                con.IsOpen = true;  // This needs to be last because a call to the Connection.Open() may be blocked until this property gets set!
            }
            else if (Connections.Contains(connectionID) && (ACK || FIN || PSH || RST))
            {
                con = Connections[connectionID] as Connection;

                ushort totalLength = Utility.ExtractRangeFromArray(payload, 16, 2).ToShort();
                ushort ipHeaderLength = (ushort)((payload[14] & 0x0f) * 4);
                ushort tcpHeaderLength = (ushort)((payload[26 + ipHeaderLength] >> 4) * 4);

                if (totalLength + 14 > payload.Length)
                {
                    // No Good -- Does not account for 0 padding? 
                    Debug.WriteLine("Bad packet size detected?  " + totalLength.ToString() + "/" + payload.Length.ToString());
                    return;
                }

                //Debug.WriteLine("1 - con.seqnum = " + con.SeqNumber);

                con.SeqNumber += (uint)(totalLength - (tcpHeaderLength + ipHeaderLength));
                con.WindowSize -= (ushort)(totalLength - (tcpHeaderLength + ipHeaderLength));

                //Debug.WriteLine("2 - con.seqnum = " + con.SeqNumber);


                if (PSH)
                {
                    //Debug.WriteLine("PSH");
                    con.SendAck();  // PSH indicates we want an ACK after this packet?  
                }
                else if (SYN && ACK)
                {
                    con.SeqNumber = packetSeqNumber + 1;
                    con.StartSeqNumber = packetSeqNumber;
                    con.AckNumber++; 
                    con.SendAck();
                    con.IsOpen = true;
                    return;
                }
                else if ((FIN || RST) && ACK)
                {
                    // Debug.WriteLine("FIN/RST + ACK");
                    con.isClosing = true;
                    //Adapter.StopListeningToPort(con.LocalPort);
                    con.SeqNumber++;
                    con.SendAck();
                    // This is an ACKnowledgement that the connection is now closed, so delete it.
                    Connections.Remove(connectionID);
                    return;
                }
                else if (FIN)
                {
                    con.isClosing = true;
                    //Adapter.StopListeningToPort(con.LocalPort);
                    // Debug.WriteLine("FIN");

                    con.SeqNumber++;
                    con.SendAck(false, true);
                    return;
                }
                else if (RST)
                {
                    con.isClosing = true;
                    //Adapter.StopListeningToPort(con.LocalPort);
                    // Debug.WriteLine("FIN");
                    con.SeqNumber++;
                    //con.SendAck(false, true);
                    return;
                }
                else if (ACK && con.isClosing)
                {
                    // Debug.WriteLine("ACK + Closing");
                    // This is an ACKnowledgement that the connection is now closed, so delete it.
                    //Adapter.StopListeningToPort(con.LocalPort);
                    Connections.Remove(connectionID);
                    return;
                }

                if (Adapter.VerboseDebugging) Debug.WriteLine("Check for data");

                //We got some data!?
                if ((totalLength - (tcpHeaderLength + ipHeaderLength)) > 0)
                {
                    byte[] segment = Utility.ExtractRangeFromArray(payload, (14 + ipHeaderLength + tcpHeaderLength), (totalLength - (tcpHeaderLength + ipHeaderLength)));

                    if (Adapter.VerboseDebugging) Debug.WriteLine("got some data, psn: " + packetSeqNumber.ToString() + ", ssn: " + con.StartSeqNumber.ToString() + ", header delim: " + segment.Locate(HttpRequest.HeaderDelimiter).ToString());

                    Networking.Adapter.FireTcpPacketEvent(segment, packetSeqNumber - con.StartSeqNumber, con);  // TCP events always fire

                    con.FireOnConnectionPacketReceived(new Packet(PacketType.TCP) { SequenceNumber = packetSeqNumber - con.StartSeqNumber, Content = segment, Socket = con });

                    // Filters out anything that is not a GET or POST Http VERB (I did a byte comparison to avoid utf decoding exceptions, since we don't know that we actually have text yet)
                    //if (packetSeqNumber - con.StartSeqNumber == 1)
                    if (segment.Length < 10 || !(segment[0] == 0x47 && segment[1] == 0x45 && segment[2] == 0x54) && !(segment[0] == 0x50 && segment[1] == 0x4F && segment[2] == 0x53 && segment[3] == 0x54)) return;  // if it is not a get, then we won't handle it through the HTTP Request Handler

                    //if (packetSeqNumber - con.StartSeqNumber == 1) // && segment.Locate(HttpRequest.HeaderDelimiter) > -1)
                    if (con.ReadyForRequest)
                    {
                        // get the TCP checksum from the current packet and make sure it does not match the last request for we start...
                        byte[] lrc = Utility.ExtractRangeFromArray(payload, (30 + ipHeaderLength), 2);

                        if (con.LastRequestChecksum.BytesEqual(lrc))
                        {
                            if (Adapter.VerboseDebugging) Debug.WriteLine("Retransmission of Request Ignored!");
                        }
                        else
                        {
                            con.LastRequestChecksum = lrc;
                            con.ReadyForRequest = false;
                            Networking.Adapter.FireHttpPacketEvent(segment, con);
                        }
                    }
                }
            }
            else if ((FIN || RST) && ACK)
            {
                //Debug.WriteLine("Handling RST for a connection that no longer exists!!!!!!!!!");
                
                con = new Connection();
                con.RemoteIP = SourceIP;
                con.RemotePort = SourcePort.ToShort();
                con.RemoteMac = Utility.ExtractRangeFromArray(payload, 6, 6);
                con.LocalPort = LocalPort.ToShort();
                con.SeqNumber = Utility.ExtractRangeFromArray(payload, 38, 4).ToInt(); 
                con.AckNumber = Utility.ExtractRangeFromArray(payload, 42, 4).ToInt(); 

                con.isClosing = true;
                //Adapter.StopListeningToPort(con.LocalPort);
                con.SendAck();
                return;
            }
        }

        /// <summary>
        /// Generates a composite number of the SourceIP, SourcePort, and LocalPort
        /// </summary>
        /// <param name="packet"></param>
        /// <returns></returns>
        public static ulong GenerateConnectionID(byte[] packet)
        {
            //byte[] SourceIP = Utility.ExtractRangeFromArray(packet, 26, 4);
            //byte[] SourcePort = Utility.ExtractRangeFromArray(packet, 34, 2);
            //byte[] LocalPort = Utility.ExtractRangeFromArray(packet, 36, 2);

            // Combine the IP, and ports together into an integer as an ID for the connection
            return Utility.CombineArrays(Utility.CombineArrays(Utility.ExtractRangeFromArray(packet, 26, 4), Utility.ExtractRangeFromArray(packet, 34, 2)), Utility.ExtractRangeFromArray(packet, 36, 2)).ToLong();
        }

    }



    /// <summary>
    /// This is beginning to look more and more like a Socket...  I was trying to avoid that though because I think sockets intimidate beginners...
    /// </summary>
    public class Connection
    {
        private static byte[] ackBase = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x45, 0x00, 0x00, 0x2c, 0x00, 0xda, 0x00, 0x00, 0xff, 0x06, 0x37, 0x0a, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x50, 0x09, 0xac, 0x00, 0x00, 0x43, 0x80, 0x58, 0xae, 0x2d, 0x41, 0x60, 0x12, 0x04, 0x00, 0x41, 0xf2, 0x00, 0x00, 0x02, 0x04, 0x02, 0x00, 0x00, 0x00 };
        private byte[] scratch = new byte[] { 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x45, 0x00, 0x00, 0xa8, 0x00, 0xd8, 0x00, 0x00, 0xff, 0x06, 0x36, 0x8b, 0xc0, 0xa8, 0x00, 0x00, 0xc, 0xa8, 0x00, 0x00, 0x00, 0x50, 0x09, 0xac, 0x00, 0x00, 0x43, 0x81, 0x58, 0xae, 0x2e, 0x82, 0x50, 0x10, 0x04, 0x00, 0xf4, 0xb6, 0x00, 0x00 };
        internal byte[] RemoteMac = Adapter.GatewayMac;
        public byte[] RemoteIP;
        public ushort RemotePort = 80;
        public ushort LocalPort = ++Adapter.EphmeralPort >= 65535 ? (ushort)49152 : Adapter.EphmeralPort;  // port should be in Range: 49152 to 65535; see http://en.wikipedia.org/wiki/Ephemeral_port;  
        internal uint SeqNumber = 0;
        internal uint StartSeqNumber = 0;
        internal uint AckNumber = 6527; //0;
        internal ushort WindowSize = 1024;
        internal bool ReadyForRequest = false;
        //internal ushort SegmentSize;
        internal byte[] LastRequestChecksum = new byte[2];  
        
        private ManualResetEvent connectionWaitHandle = new ManualResetEvent(false);

        public delegate void ConnectionPacketReceivedEventHandler(Packet packet);

        /// <summary>
        /// Fires when the phy goes up or down
        /// </summary>
        public event ConnectionPacketReceivedEventHandler OnConnectionPacketReceived;

        internal void FireOnConnectionPacketReceived(Packet packet)
        {
            if (OnConnectionPacketReceived != null) OnConnectionPacketReceived(packet);
        }

        internal bool isClosing = false;
        private bool isOpen = false;

        /// <summary>
        /// Close the connection.  This should not be required and this method may be removed later...
        /// </summary>
        public void Close()
        {
            OnConnectionPacketReceived = null;

            if (isClosing == false)
            {
                isClosing = true;
                this.SendAck(false, true);
            }
        }

        internal bool IsOpen
        {
            get { return isOpen; }
            set 
            { 
                isOpen = value;
                connectionWaitHandle.Set();  // This will release an Open() call waiting for the connection!
            }
        }
        
        internal TimeSpan LastActivity = Microsoft.SPOT.Hardware.Utility.GetMachineTime();

        /// <summary>
        /// Connection Identifier based on a composite of remote IP, remote port and source port
        /// </summary>
        public ulong ID
        {
            get
            {
                // Combine the IP, and ports together into an integer as an ID for the connection
                return Utility.CombineArrays(Utility.CombineArrays(RemoteIP, RemotePort.ToBytes()), LocalPort.ToBytes()).ToLong();
            }
        }

        /// <summary>
        /// Returns true if the connection is between two systems on the same subnet/behind the same router?
        /// </summary>
        public bool IsLocal
        {
            get
            {
                //TODO: using MAC address to determine if caller is local, is that legit?  
                //TODO: This ASSUMES a subnet mask of 255.255.0.0!  This should instead use the real subnet mask to determine if the remote IP is local!

                return !this.RemoteMac.BytesEqual(Adapter.GatewayMac) || Adapter.IPAddress.ToAddress().Substring(0, 7) == this.RemoteIP.ToAddress().Substring(0, 7);
            }
        }

        /// <summary>
        /// Open this connection.  
        /// </summary>
        /// <param name="timeout"></param>
        public bool Open(int timeout = 3)
        {
            if (IsOpen) return true;  // if this connection is already open, there is nothing to do...

            //Debug.WriteLine("Sending SYN");

            this.RemoteMac = Adapter.GatewayMac;  //TODO: we should probably ARP for the MAC of the RemoteIP

            //Adapter.ListenToPort(this.LocalPort);

            TCP.Connections.Add(this.ID, this);

            connectionWaitHandle.Reset();
            
            // Send a SYN to start the open connection process...
            SendAck(true, false, false);

            //Debug.WriteLine("Waiting...");

            // Block here until connection is made or timeout happens!  
            connectionWaitHandle.WaitOne(timeout * 1000, true);

            //Debug.WriteLine("Connection Open => " + this.IsOpen);

            return this.isOpen;
        }


        /// <summary>
        /// Send the entire buffer.  Use this for short messages only, otherwise you may have out of memory exceptions.  
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="size"></param>
        public void SendAsync(byte[] buffer)
        {
            //TODO: consider automatically send as chunks to allow large buffers to work with a minimal impact on memory

            SendAsync(buffer, 0, (short)buffer.Length);
        }


        /// <summary>
        /// Send a TCP Data Segment
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="size"></param>
        public void SendAsync(byte[] buffer, int offset, short size)
        {
            if (Adapter.GatewayMac == null || Adapter.IPAddress == null || size <= 0 || isClosing) return;

            if (!isOpen) Open();

            //Debug.WriteLine("Sending TCP Data Segment");

            scratch.Overwrite(0 + 2, this.RemoteMac);
            scratch.Overwrite(6 + 2, Adapter.MacAddress);
            scratch.Overwrite(26 + 2, Adapter.IPAddress);
            scratch.Overwrite(30 + 2, this.RemoteIP);
            scratch.Overwrite(34 + 2, this.LocalPort.ToBytes());
            scratch.Overwrite(36 + 2, this.RemotePort.ToBytes());
            scratch.Overwrite(38 + 2, this.AckNumber.ToBytes());  // SYN
            scratch.Overwrite(42 + 2, this.SeqNumber.ToBytes());  // ACK
            //scratch.Overwrite(54 + 2, new byte[] { 0x00, 0x00, 0x00, 0x00 });  // clear Max Segment Size option
            scratch.Overwrite(48 + 2, new byte[] { 0x04, 0x00 });  // Set window size to 1024 bytes

            
            //TODO: Handle the PSH flag here...
            // Set Flags
            //if (isLast)
            //    scratch[47] = 0x18;  // PSH and ACK
            //else
                scratch[47 + 2] = 0x10;  // ACK

            scratch.Overwrite(50 + 2, new byte[2] { 0x00, 0x00 }); // clear header checksum, so that calc excludes the checksum itself
            scratch.Overwrite(24 + 2, new byte[2] { 0x00, 0x00 }); // clear header checksum, so that calc excludes the checksum itself

            buffer = Utility.CombineArrays(scratch, 0, scratch.Length, buffer, offset, size);

            buffer.Overwrite(16 + 2, ((ushort)((buffer.Length - 2) - 14)).ToBytes());  // set the IPv4 Message size

            // Calculate the TCP header checksum
            buffer.Overwrite(50 + 2, buffer.InternetChecksum(20 + size, 34 + 2, Adapter.IPAddress, this.RemoteIP, 0x06)); // header checksum

            // Calculate the IPv4 header checksum
            buffer.Overwrite(24 + 2, buffer.InternetChecksum(20, 14 + 2)); // header checksum

            //TODO: uneven lengths need 0 padding to make them even...

            this.AckNumber += (uint)size;
            Adapter.nic.SendFrame(buffer, 2);
        }

        /// <summary>
        /// Send connection control messages
        /// </summary>
        internal void SendAck(bool Synchronize = false, bool Finish = false, bool Ack = true)
        {
            if (Adapter.GatewayMac == null || Adapter.IPAddress == null) return;

            // Debug.WriteLine("Sending TCP ACK - " + Connections.Count);

            ackBase.Overwrite(0, this.RemoteMac);
            ackBase.Overwrite(6, Adapter.MacAddress);
            ackBase.Overwrite(26, Adapter.IPAddress);
            ackBase.Overwrite(30, this.RemoteIP);
            ackBase.Overwrite(34, this.LocalPort.ToBytes());
            ackBase.Overwrite(36, this.RemotePort.ToBytes());
            ackBase.Overwrite(38, this.AckNumber.ToBytes());  // SYN
            ackBase.Overwrite(42, this.SeqNumber.ToBytes());  // ACK
            ackBase.Overwrite(48, this.WindowSize.ToBytes());  // Set window size to 1024 bytes

            if (Synchronize)
            {
                //ackBase.Overwrite(54, new byte[4] { 0x02, 0x04, 0x03, 0x00 });  // Set Max Segment Size option to 768 bytes because the entire header must fit in a single segment because the packets can arrive too quickly to process if fragmented!
                ackBase.Overwrite(54, new byte[4] { 0x02, 0x04, 0x05, 0xb4 });  // Set Max Segment Size option to 1460 bytes because the entire header must fit in a single segment because the packets can arrive too quickly to process if fragmented!
                ackBase.Overwrite(16, new byte[2] { 0x00, 0x2c });  // Update IPv4 header length         
                ackBase.Overwrite(46, new byte[1] { 0x60 });  // Update TCP length to 24                    
            }
            else
            {
                ackBase.Overwrite(54, new byte[4] { 0x00, 0x00, 0x00, 0x00 });  // clear Max Segment Size option
                ackBase.Overwrite(16, new byte[2] { 0x00, 0x28 });  // Update IPv4 header length                     
                ackBase.Overwrite(46, new byte[1] { 0x50 });  // Update TCP length to 20                    
            }

            // Set Flags
            if (Synchronize && Ack)
            {
                ackBase[47] = 0x12;  // SYN and ACK
                if (Adapter.VerboseDebugging) Debug.WriteLine("Sending TCP SYN+ACK");
            }
            else if (Finish && Ack)
            {
                ackBase[47] = 0x11;  // FIN and ACK
                if (Adapter.VerboseDebugging) Debug.WriteLine("Sending TCP FIN+ACK");
            }
            else if (Synchronize)
            {
                ackBase[47] = 0x02;  // just SYN
                if (Adapter.VerboseDebugging) Debug.WriteLine("Sending TCP SYN");
            }
            else if (Finish)
            {
                ackBase[47] = 0x01;  // just FIN  -- uncommon/not necessary?
                if (Adapter.VerboseDebugging) Debug.WriteLine("Sending TCP FIN");
            }
            else
            {
                ackBase[47] = 0x10;  // just ACK
                if (Adapter.VerboseDebugging) Debug.WriteLine("Sending TCP ACK");
            }

            // Calculate the TCP header checksum
            ackBase.Overwrite(50, new byte[2] { 0x00, 0x00 }); // clear header checksum, so that calc excludes the checksum itself
            ackBase.Overwrite(50, ackBase.InternetChecksum(Synchronize ? 24 : 20, 34, Adapter.IPAddress, this.RemoteIP, 0x06)); // header checksum

            // Calculate the IPv4 header checksum
            ackBase.Overwrite(24, new byte[2] { 0x00, 0x00 }); // clear header checksum, so that calc excludes the checksum itself
            ackBase.Overwrite(24, ackBase.InternetChecksum(20, 14)); // header checksum

            Adapter.nic.SendFrame(ackBase);

            //System.Threading.Thread.Sleep(10);
            //Microsoft.SPOT.Debug.GC(false);
            //System.Threading.Thread.Sleep(5);
        }
    }

    public class HttpRequest
    {

        public Hashtable Headers { get; internal set; }  // all headers in a key value string/string hashset
        //public string[] AcceptTypes { get; internal set; }
        
        /// <summary>
        /// This is the HTTP verb, such as GET, PUT, POST, etc.
        /// </summary>
        public string RequestType { get; internal set; }  // GET, PUT, POST
        
        /// <summary>
        /// This is the path to the requested resource, usually a file name
        /// </summary>
        public string Path { get; set; } // the virtual path requested
        
        /// <summary>
        /// The Protocol and version, such as HTTP/1.1
        /// </summary>
        public string Protocol { get; internal set; }        
        
        /// <summary>
        /// The Requested host name
        /// </summary>
        public string Host { get; internal set; }
        
        /// <summary>
        /// The content of the request.  GET requests are usually empty, but others may have something in here...
        /// </summary>
        public string Content { get; internal set; }

        internal static byte[] HeaderDelimiter = new byte[4] { 0x0d, 0x0a, 0x0d, 0x0a };
        private static byte[] CrLf = new byte[2] { 0x0d, 0x0a };

        private Connection _con;
        private HttpResponse _responseToSend = null;
        private bool omitContent = false;

        internal Stream _content;

        private ManualResetEvent responseWaitHandle = new ManualResetEvent(false);

        /// <summary>
        /// Sends an HTTP Request to the specified URL.  If no content is specified a GET is sent, otherwise a POST is sent.  
        /// </summary>
        /// <param name="url">URL address with path, such as http://odata.netflix.com/Catalog/Titles%28%27BVIuO%27%29/Synopsis/$value</param>
        /// <param name="content">The body of a POST</param>
        /// <param name="connection">Specify a connection object when you want to get the response event from ConnectionResponseReceived</param>
        public HttpRequest(string url, string content = null, Connection connection = null)
        {
            if (connection != null) _con = connection;

            url = (url.IndexOf("http://") >= 0) ? url.Substring(url.IndexOf("http://") + 7) : url;

            Host = url.Split('/')[0].Trim();
            Path = System.Web.HttpUtility.UrlEncode(url.Substring(Host.Length).Trim(), false);
            if (Path == string.Empty) Path = "/";

            Protocol = "HTTP/1.1";
            Content = content;
            RequestType = "GET";

            Headers = new Hashtable();

            if (content != null && content != string.Empty)
            {
                RequestType = "POST";
                Headers.Add("Content-Length", content.Length.ToString());
            }
            

        }

        private byte[] AssembleRequest()
        {
            string a = RequestType;
            a += " " + Path + " " + Protocol + "\r\nHost: ";
            a += Host + "\r\n";

            foreach (var aHeader in Headers.Keys)
                a += (string)aHeader + ": " + (string)Headers[aHeader] + "\r\n";

            a += "\r\n"; // Cache-Control: no-cache\r\n  //Accept-Charset: utf-8;\r\n

            if (Content != null && Content != string.Empty && RequestType == "POST") a += Content;

            // Connection: Close\r\n

            // "Connection: Keep-Alive"

            return Encoding.UTF8.GetBytes(a);
        }


        internal HttpRequest()
        {

        }

        public bool IsLocalCall
        {
            get
            {
                //TODO: using MAC address to determine if caller is local, is that legit?  
                //TODO: This ASSUMES a subnet mask of 255.255.0.0!  This should instead use the real subnet mask to determine if the remote IP is local!

                return !_con.RemoteMac.BytesEqual(Adapter.GatewayMac) || Adapter.IPAddress.ToAddress().Substring(0, 7) == _con.RemoteIP.ToAddress().Substring(0, 7);

                //return !_con.RemoteMac.BytesEqual(Adapter.GatewayMac); // this.client.LocalEndPoint.ToString().Substring(0, 7) == this.client.RemoteEndPoint.ToString().Substring(0, 7);
            }
        }

        /// <summary>
        /// A New Http Request
        /// </summary>
        /// <param name="tcpContent">The data content of the TCP packet</param>
        /// <param name="socket"></param>
        internal HttpRequest(byte[] tcpContent, Connection socket)
        {
            // Parse the packet into the various properties...
            _con = socket;
            Headers = new Hashtable();

            // VERY worrisome.  I have seen the last 4 bytes be wrong twice now.  The second time I could see the packet was transmitted correctly in Wireshark, but the last 4 bytes were wrong as produced by the ReceiveFrame() method...
            if (!Utility.ExtractRangeFromArray(tcpContent, tcpContent.Length - 4, 4).BytesEqual(HeaderDelimiter))
                Debug.WriteLine("This should never happen!  ");
            
            tcpContent.Overwrite(tcpContent.Length - 4, HeaderDelimiter);  // ugly workaround for possible bug...  should not be necessary

            var delimiterLocation = tcpContent.Locate(HeaderDelimiter);
            var firstLineLocation = tcpContent.Locate(CrLf);

            if (firstLineLocation < 12) throw new Exception("Malformed HTTP Request.");

            var firstLine = new string(Encoding.UTF8.GetChars(tcpContent, 0, firstLineLocation));

            RequestType = firstLine.Split(' ')[0].Trim().ToUpper();
            Path = HttpUtility.UrlDecode(firstLine.Split(' ')[1].Trim(), false);

            int colonLocation = -1;
            int start = firstLineLocation;
            bool malformed = false;

            // Parse all the header keys and values
            for (int i = firstLineLocation; i <= delimiterLocation; i++)
            {
                if (tcpContent[i] == 0x3A && colonLocation == -1) colonLocation = i;
                if (tcpContent[i] > 0x7E || tcpContent[i] < 0x09) malformed = true;

                if (tcpContent[i] == 0x0D || tcpContent[i] == 0x0A)
                {
                    if (colonLocation > start && !malformed)
                    {
                        // By handling the exception at each parameter, we can salvage the other headers if one contains an invalid character
                        // Although, we should be avoiding any exceptions with the malformed flag.  
                        try
                        {
                            Headers.Add(new string(Encoding.UTF8.GetChars(tcpContent, start, colonLocation - start)).Trim(), 
                                        new string(Encoding.UTF8.GetChars(tcpContent, colonLocation + 1, i - colonLocation)).Trim());
                        }
                        catch { }
                    }

                    colonLocation = -1;
                    start = i + 1;
                    malformed = false;
                }
            }



                // Parse all the header keys and values
                //foreach (var aLine in new string(Encoding.UTF8.GetChars(tcpContent, firstLineLocation, (delimiterLocation - firstLineLocation))).Split('\r', '\n'))
                //    if (aLine.IndexOf(':') > 0) Headers.Add(aLine.Substring(0, aLine.IndexOf(':')), aLine.Substring(aLine.IndexOf(':') + 1).TrimStart());
            if (RequestType != "GET")
                Content = new string(Encoding.UTF8.GetChars(tcpContent, delimiterLocation + 4, tcpContent.Length - (delimiterLocation + 4)));
            else
                Content = string.Empty;

            // Assume HTTP 1.1 if cannot parse protocol...
            Protocol = (firstLine.IndexOf("HTTP") > 5) ? firstLine.Split(' ')[2] : "HTTP/1.1";

            if (Headers.Contains("Host")) this.Host = Headers["Host"] as string;
        }

        public void SendAsync()
        {
            // if _con is null, create and open a new connection.  with a DNS lookup of host if necessary
            if (_con == null)
            {
                var remoteIp = DNS.Lookup(Host);
                _con = new Connection() { RemoteIP = remoteIp }; 
            }

            var r = AssembleRequest();

            _con.SendAsync(r, 0, (short)r.Length);  
        }


        /// <summary>
        /// A synchronous call to send an HTTP Request and Receive a Response --  this call will block (until the timeout) while trying to get the result
        /// </summary>
        /// <param name="timeout">Time out in seconds</param>
        /// <param name="RetrieveHeaderOnly">Set this to true if you don't need the content of the response (which can consume a lot of memory)</param>
        /// <returns>And HttpResponse object OR a null if it timeout happened</returns>
        public HttpResponse Send(ushort timeout = 5, bool ReturnHeaderOnly = false)
        {
            this.omitContent = ReturnHeaderOnly;

            // if _con is null, create and open a new connection.  with a DNS lookup of host if necessary
            if (_con == null)
            {
                var remoteIp = DNS.Lookup(Host);
                _con = new Connection() { RemoteIP = remoteIp };
            }

            var r = AssembleRequest();
            _responseToSend = null;

            _con.OnConnectionPacketReceived += _con_OnConnectionPacketReceived;

            responseWaitHandle.Reset();  // This will release an Open() call waiting for the connection!

            _con.SendAsync(r, 0, (short)r.Length);

            //Wait for response or timeout

            responseWaitHandle.WaitOne(timeout * 1000, true);

            _con.OnConnectionPacketReceived -= _con_OnConnectionPacketReceived;

            return _responseToSend;
        }

        void _con_OnConnectionPacketReceived(Packet packet)
        {
            _responseToSend = new HttpResponse(packet.Content, omitContent);
            responseWaitHandle.Set();
        }


        ///// <summary>
        ///// Send the Http Response as chunks of bytes.  This allows us to send a large number of bytes without using up all our memory :)
        ///// </summary>
        ///// <param name="buffer"></param>
        ///// <param name="offset"></param>
        ///// <param name="size"></param>
        //public void SendResponse(byte[] buffer, int offset, short size)
        //{
        //    _con.SendAsync(buffer, offset, size);
        //}

        /// <summary>
        /// Send a 404 not found response
        /// </summary>
        public void SendNotFound()
        {
            string body = "<html><head><title>Page Not Found</title></head><body>404 - Not Found</body></html>";

            var NotFoundResponse = new HttpResponse(body, "text/html", "404 Not Found");
            NotFoundResponse.Headers.Add("Date", DateTime.Now.ToUniversalTime() + " UTC");
            this.SendResponse(NotFoundResponse);
        }

        public static object sdLock = new object();
        private static byte[] chunk = new byte[1400];

        /// <summary>
        /// Send the Http Response with automatic chunking to keep memory usage low...
        /// </summary>
        /// <param name="response">Response object to send</param>
        /// <param name="chunkSize">If this chunk size exceeds the size of the controller buffer size, bad things might happen...  I would recommend a max of 1024...</param>
        public void SendResponse(HttpResponse response, int chunkSize = 512)
        {
            try
            {
                //Debug.WriteLine("SR - Memory: " + Microsoft.SPOT.Debug.GC(false).ToString());

                _con.SendAsync(response.HeaderSection, 0, (short)response.HeaderSection.Length);  // Send Header

                short bytesToSend = 0;

                lock (sdLock)
                {
                    response._content.Position = 0;

                    // Send message in chunks
                    do
                    {
                        bytesToSend = (short)response._content.Read(chunk, 0, chunkSize);
                        _con.SendAsync(chunk, 0, bytesToSend);
                        // Will a bunch of waiting async calls pile up here?  using a lot of RAM?  
                        //TODO: make this a Synchronous call!!!  So that the memory of pending calls does not stack up
                    }
                    while (bytesToSend > 0 && response._content.Position <= response._content.Length); //chunkSize == bytesToSend);

                    response._content.Flush();
                    response._content.Close();

                    //Debug.WriteLine("A Pre-Garbage Collecting... Memory: " + Microsoft.SPOT.Debug.GC(false).ToString());
                    //Microsoft.SPOT.Debug.GC(true);
                    //Debug.WriteLine("A Post-Garbage Collecting... Memory: " + Microsoft.SPOT.Debug.GC(false).ToString());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Pre-Garbage Collecting... Memory: " + Microsoft.SPOT.Debug.GC(false).ToString());
                Microsoft.SPOT.Debug.GC(true);
                Debug.WriteLine("Post-Garbage Collecting... Memory: " + Microsoft.SPOT.Debug.GC(false).ToString());
                Debug.WriteLine(ex.Message);
            }
            finally
            {
                _con.ReadyForRequest = true;
            }
        }
    }

    public class HttpResponse
    {
        // HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nCache-Control: no-cache; charset=utf-8\r\nContent-Length: 82\r\nConnection: close
                        // Send the header
        public string ContentType;
        public string Connection;

        /// <summary>
        /// A string containing both the status code number and the Message.  Such as "HTTP/1.1 200 OK" or "HTTP/1.1 404 Not Found".  
        /// </summary>
        public string Status;
        public string Message;
        public string Header;
        
        /// <summary>
        /// Additional HTTP Headers in "key: value" format, such as "Cache-Control: no-cache; charset=utf-8"
        /// </summary>
        public Hashtable Headers = new Hashtable();
        internal Stream _content;

        /// <summary>
        /// Creates a new Http Response object.  
        /// </summary>
        /// <param name="body">Be aware of memory limitations here.  Use small strings with this constructor.  </param>
        /// <param name="contentType"></param>
        /// <param name="status"></param>
        /// <param name="connection"></param>
        public HttpResponse(string body, string contentType = "text/html", string status = "200 OK", string connection = "close")
        {
            ContentType = contentType;
            Connection = connection;
            Status = status;

            _content = new System.IO.MemoryStream( Encoding.UTF8.GetBytes( body ) );
        }

        public HttpResponse(Stream content, string contentType = "text/html", string status = "200 OK", string connection = "close")
        {
            ContentType = contentType;
            Connection = connection;
            Status = status;

            _content = content;
        }

        /// <summary>
        /// Creates a new Response object by parsing the content into the Response object properties
        /// </summary>
        /// <param name="content"></param>
        public HttpResponse(byte[] content, bool omitContent = false)
        {
            int contentStart = content.Locate(new byte[4] { 0x0d, 0x0a, 0x0d, 0x0a });
            int headerStart = content.Locate(new byte[2] { 0x0d, 0x0a });

            Header = string.Empty;
            Message = string.Empty;

            Status = new string(Encoding.UTF8.GetChars(Utility.ExtractRangeFromArray(content, 0, headerStart)));
            if ((contentStart > (headerStart + 2)) && headerStart > 5)
            {
                int colonLocation = -1;
                int start = headerStart + 2;
                bool malformed = false;

                // Parse all the header keys and values (Note: this is intentionally parsed directly from the byte array to minimize creating and destroying strings that consume memory and need garbage collection)
                for (int i = headerStart + 2; i <= contentStart; i++)
                {
                    if (content[i] == 0x3A && colonLocation == -1) colonLocation = i;
                    if (content[i] > 0x7E || content[i] < 0x09) malformed = true;

                    if (content[i] == 0x0D || content[i] == 0x0A)
                    {
                        if (colonLocation > start && !malformed)
                        {
                            // By handling the exception at each parameter, we can salvage the other headers if one contains an invalid character
                            // Although, we should be avoiding any exceptions with the malformed flag.  
                            try
                            {
                                Headers.Add(new string(Encoding.UTF8.GetChars(content, start, colonLocation - start)).Trim(),
                                            new string(Encoding.UTF8.GetChars(content, colonLocation + 1, i - colonLocation)).Trim());
                            }
                            catch { }
                        }

                        colonLocation = -1;
                        start = i + 1;
                        malformed = false;
                    }
                }
                
                //Header = new string(Encoding.UTF8.GetChars(Utility.ExtractRangeFromArray(content, headerStart + 2, contentStart - (headerStart + 2))));
                if (!omitContent && contentStart + 4 < content.Length)
                    Message = new string(Encoding.UTF8.GetChars(Utility.ExtractRangeFromArray(content, contentStart + 4, content.Length - (contentStart + 4))));
            }


        }


        internal byte[] HeaderSection
        {
            get
            {
                string header = "HTTP/1.1 " + Status + (ContentType != null && ContentType != string.Empty ? ("\r\nContent-Type: " + ContentType) : string.Empty);

                foreach (string headerKey in Headers.Keys)
                {
                    header += "\r\n" + headerKey + ": " + (Headers[headerKey] as string);
                }

                header += ((_content != null && _content.Length > 0) ? ("\r\nContent-Length: " + _content.Length.ToString()) : string.Empty)
                          + (Connection != null && Connection != string.Empty ? ("\r\nConnection: " + Connection) : string.Empty) 
                          + "\r\n\r\n";

                return Encoding.UTF8.GetBytes(header); 
            }
        }



    }

}
