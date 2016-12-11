/*
*  Software License Agreement
*
* Copyright (C) Cross The Road Electronics.  All rights
* reserved.
* 
* Cross The Road Electronics (CTRE) licenses to you the right to 
* use, publish, and distribute copies of CRF (Cross The Road) firmware files (*.crf) and Software
* API Libraries ONLY when in use with Cross The Road Electronics hardware products.
* 
* THE SOFTWARE AND DOCUMENTATION ARE PROVIDED "AS IS" WITHOUT
* WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT
* LIMITATION, ANY WARRANTY OF MERCHANTABILITY, FITNESS FOR A
* PARTICULAR PURPOSE, TITLE AND NON-INFRINGEMENT. IN NO EVENT SHALL
* CROSS THE ROAD ELECTRONICS BE LIABLE FOR ANY INCIDENTAL, SPECIAL, 
* INDIRECT OR CONSEQUENTIAL DAMAGES, LOST PROFITS OR LOST DATA, COST OF
* PROCUREMENT OF SUBSTITUTE GOODS, TECHNOLOGY OR SERVICES, ANY CLAIMS
* BY THIRD PARTIES (INCLUDING BUT NOT LIMITED TO ANY DEFENSE
* THEREOF), ANY CLAIMS FOR INDEMNITY OR CONTRIBUTION, OR OTHER
* SIMILAR COSTS, WHETHER ASSERTED ON THE BASIS OF CONTRACT, TORT
* (INCLUDING NEGLIGENCE), BREACH OF WARRANTY, OR OTHERWISE
*/
using System;
using System.Collections.Generic;
using System.Management; // need to add System.Management to your project references.
using System.Threading;

class UsbSearch
{
    private const String kHeroSearchString = "Cross Link HERO .NETMF";
    private int _heroCount = 0;

    /* ---------------------------OS objects and threading ------------------------- */
    private ManagementObjectSearcher _searcher = new ManagementObjectSearcher(@"Select * From Win32_USBHub");
    private Thread _thrd = null;
    private EventWaitHandle _stopThread = new EventWaitHandle(false, EventResetMode.ManualReset);

    public UsbSearch()
    {
        _thrd = new Thread(new ThreadStart(BackgroundTask));
        _thrd.Start();
    }
    public void Dispose()
    {
        /* signal stop thread */
        _stopThread.Set();
        _thrd.Join();
    }

    public int GetHeroCount()
    {
        return _heroCount;
    }

    private void BackgroundTask()
    {
        /* every 100ms */
        while (_stopThread.WaitOne(100) == false)
        {
            /* how many DFU devices are there */
            int heroCount = SearchCountPriv(UsbSearch.kHeroSearchString);
        
            /* squirell away the relevent data */
            Interlocked.Exchange(ref _heroCount, heroCount);
        }
    }

    /** USBDeviceInfo */
    private class USBDeviceInfo
    {
        public USBDeviceInfo(string description)
        {
            this.Description = description;
        }
        public string Description { get; private set; }
        public override string ToString() { return Description; }
    }

    private List<USBDeviceInfo> GetUSBDevices()
    {
        List<USBDeviceInfo> devices = new List<USBDeviceInfo>();
        ManagementObjectCollection collection;
        collection = _searcher.Get();
        if (collection != null)
        {
            foreach (var device in collection)
            {
                if (device != null)
                {
                    string desc = (string)device.GetPropertyValue("Description");
                    if (desc != null)
                    {
                        // https://msdn.microsoft.com/en-us/library/aa394353(v=vs.85).aspx
                        devices.Add(new USBDeviceInfo(desc));
                    }
                }
            }
            collection.Dispose();
        }
        return devices;
    }

    private int SearchCountPriv(String toSearch)
    {
        int retval = 0;
        List<USBDeviceInfo> lst = GetUSBDevices();
        foreach (USBDeviceInfo udi in lst)
        {
            if (udi.Description.IndexOf(toSearch) >= 0)
            {
                ++retval;
            }
        }
        return retval;
    }
}
