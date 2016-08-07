using System;
using Microsoft.SPOT;
using Networking;
using System.Threading;
using Microsoft.SPOT.Hardware;
using System.Text;

namespace NetworkingExample
{
    class HttpProgram
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


            // This is a call to the OData REST web server hosted by netflix.  Just Ctrl-Click the link to see what it does.  
            var r = new HttpRequest("http://odata.netflix.com/Catalog/Titles('BVIuO')/Synopsis/$value");
            r.Headers.Add("Accept", "*/*");  // Add custom properties to the Request Header
            var response = r.Send();

            if (response != null) Debug.Print("Response: " + response.Message);
        }

    }

 
}
