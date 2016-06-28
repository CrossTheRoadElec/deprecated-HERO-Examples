using System;
using System.Collections;
using System.Threading;
using System.Text;
using Networking;
using Microsoft.SPOT.Hardware;
using System.Diagnostics;
using Microsoft.SPOT;

namespace NetworkingExample
{
    public class DNSLookupProgram
    {
        public static void Main()
        {
            #region Static IP example
            //Networking.Adapter.IPAddress = "192.168.1.95".ToBytes();
            //Networking.Adapter.Gateway = "192.168.1.254".ToBytes();
            //Networking.Adapter.DomainNameServer = Networking.Adapter.Gateway;
            //Networking.Adapter.DomainNameServer2 = "8.8.8.8".ToBytes();  // Google DNS Server
            //Networking.Adapter.DhcpDisabled = true;
            #endregion

            // http://forums.netduino.com/index.php?/topic/322-experimental-drivers-for-wiznet-based-ethernet-shields/page__view__findpost__p__3170
            // 5C-86-4A-00-00-DD   This is a test MAC address from Secret Labs
            // Note: This MAC address should be Unique, but it should work fine on a local network (as long as there is only one instance running with this MAC)
            Networking.Adapter.Start(new byte[] { 0x5c, 0x86, 0x4a, 0x00, 0x00, 0xdd }, "mip", InterfaceProfile.Hero_Socket1_ENC28);
            
            var addressBytes = Networking.DNS.Lookup("odata.netflix.com");
            
            Microsoft.SPOT.Trace.Print("DNS Lookup: odata.netflix.com -> " + addressBytes.ToAddress());
        }
    }
}
