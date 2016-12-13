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
using System.Text;
using System.Collections.ObjectModel;
using Microsoft.NetMicroFramework.Tools.MFDeployTool.Engine;
using System.Threading;

class DeviceAPI
{
    private StringBuilder m_sb = new StringBuilder();

    private MFDeploy m_deploy = new MFDeploy();

    private MFDevice m_device = null;

    private int m_devRefCount = 0;

    /// <summary>
    /// Calling application must Dispose this object before end of app.
    /// </summary>
    public void Dispose()
    {
        m_deploy.Dispose();
    }

    public bool IsConnected()
    {
        return m_devRefCount > 0;
    }
    private MFPortDefinition FindHeroPortDef()
    {
        MFPortDefinition retval = null;
        ReadOnlyCollection<MFPortDefinition> list = null;
            
        list = m_deploy.EnumPorts(TransportType.USB);

        if (list.Count > 0)
        {
            retval = list[0];
        }
   
        return retval;
    }

    public int Connect()
    {
        int retval = 0; /* okay */

        if (m_device != null)
        {
            Interlocked.Increment(ref m_devRefCount);
        }
        else
        {
            MFPortDefinition port;
            try
            {
                port = FindHeroPortDef();
            }
            catch (Exception excep)
            {
                Console.Out.WriteLine(excep.Message);
                return -5; /* exception suggests HERO is connected to VS */
            }

            if (port == null)
            {
                retval = -1; /* could not find a device */
            }
            else
            {
                try
                {
                    m_device = m_deploy.Connect(port, null);

                    if (m_device != null)
                    {
                        m_device.OnDebugText += new EventHandler<DebugOutputEventArgs>(OnDbgTxt);

                        Interlocked.Increment(ref m_devRefCount);
                    }
                    else
                    {
                        retval = -2; /* tried to connect and failed */
                    }
                }
                catch (Exception)
                {
                    retval = -3; /* tried to connect and failed */
                }
            }
        }
        return retval;
    }


    public bool Disconnect()
    {
        bool fDisconnected = (m_device == null);
        if (m_device != null)
        {
            if (Interlocked.Decrement(ref m_devRefCount) <= 0)
            {

                fDisconnected = true;

                m_device.OnDebugText -= new EventHandler<DebugOutputEventArgs>(OnDbgTxt);
                m_device.Dispose();
                m_device = null;
            }
        }
        else
        {
        }

        return fDisconnected;
    }


    public void OnDbgTxt(object sender, DebugOutputEventArgs e)
    {
        lock (m_sb)
        {
            m_sb.Append(e.Text);
        }
    }

    public String GetCachedOutputBuffer()
    {
        lock (m_sb)
        {
            String retval = m_sb.ToString();
            m_sb.Clear();
            return retval;
        }
    }
}
