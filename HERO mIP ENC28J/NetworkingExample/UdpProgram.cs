using System;
using System.Collections;
using System.Threading;
using System.Text;
using Networking;
using Microsoft.SPOT.Hardware;
using System.Diagnostics;

namespace NetworkingExample
{
    public class UdpProgram
    {
        public static void Main()
        {
            // http://forums.netduino.com/index.php?/topic/322-experimental-drivers-for-wiznet-based-ethernet-shields/page__view__findpost__p__3170
            // 5C-86-4A-00-00-DD   This is a test MAC address from Secret Labs
            // Note: This MAC address should be Unique, but it should work fine on a local network (as long as there is only one instance running with this MAC)
            Networking.Adapter.Start(new byte[] { 0x5c, 0x86, 0x4a, 0x00, 0x00, 0xdd }, "mip", InterfaceProfile.Hero_Socket1_ENC28);

            // Listen for UDP messages sent to activated ports
            Networking.Adapter.OnUdpReceivedPacketEvent += new Adapter.UdpPacketReceivedEventHandler(Adapter_OnUdpReceivedPacketEvent);
            // Activate the NTP (date/time) port 123
            Networking.Adapter.ListenToPort(123); 

            // Create a NTP (date/time) Request Message
            var msg = new byte[48];
            msg[0] = 0x1b;

            // Let's get the UTC time from a time zerver using a UDP Message
            UDP.SendUDPMessage(msg, new byte[4] { 0x40, 0x5a, 0xb6, 0x37 }, 123, 123);  // 64.90.182.55 the address of a NIST time server
            
            // Loop to keep program alive
            while (true) Thread.Sleep(100);
        }

        static void Adapter_OnUdpReceivedPacketEvent(Packet packet)
        {
            if (packet.Socket.RemotePort == 123)
            {
                var transitTime = Utility.ExtractRangeFromArray(packet.Content, 40, 8);
                Microsoft.SPOT.Trace.Print("Current UTC Date/Time is " + transitTime.ToDateTime());
            }
        }
    }

    public static class Extensions
    {
        /// <summary>
        /// Convert an 8-byte array from NTP format to .NET DateTime.  
        /// </summary>
        /// <param name="ntpTime">NTP format 8-byte array containing date and time</param>
        /// <returns>A Standard .NET DateTime</returns>
        public static DateTime ToDateTime(this byte[] ntpTime)
        {
            Microsoft.SPOT.Debug.Assert(ntpTime.Length == 8, "The passed array is too short to be a valid ntp date and time");

            ulong intpart = 0;
            ulong fractpart = 0;

            for (int i = 0; i <= 3; i++)
                intpart = (intpart << 8) | ntpTime[i];

            for (int i = 4; i <= 7; i++)
                fractpart = (fractpart << 8) | ntpTime[i];

            ulong milliseconds = (intpart * 1000 + (fractpart * 1000) / 0x100000000L);

            var timeSince1900 = TimeSpan.FromTicks((long)milliseconds * TimeSpan.TicksPerMillisecond);
            return new DateTime(1900, 1, 1).Add(timeSince1900);

        }
    }
}
