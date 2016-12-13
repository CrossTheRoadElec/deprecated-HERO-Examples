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
using System.Drawing;
using System.Windows.Forms;

namespace HERO_Debug_Monitor
{
    public partial class Form1 : Form
    {
        /// <summary>
        /// General utility class for detecting USB Devices
        /// </summary>
        private UsbSearch _usbSearch = new UsbSearch();
        /// <summary>
        /// Wrapper for all of the Microsoft Device API used in this example.
        /// </summary>
        private DeviceAPI _deviceApi = new DeviceAPI();

        public Form1()
        {
            InitializeComponent();
            /* start timer */
            timer1.Interval = 100;
            timer1.Enabled = true;
            /* fill bottom tool strip with default text */
            toolStripStatusLabel1.Text = this.Text;
            /* paint the documentation on top of the form */
            FillDescription();
            /* enable GUI */
            EnableGui();
        }
        /// <summary>
        /// When the form closes, cleanup our allocated resources and close all threads.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _deviceApi.Dispose();
            _usbSearch.Dispose();
        }

        private void FillDescription()
        {
            rtbDescription.Rtf = Properties.Resources.Documentation;
            Color col = this.BackColor;
            rtbDescription.ReadOnly = true;
            rtbDescription.BackColor = col;
        }
 
        /// <summary>
        /// Setup form so user can press buttons and use the interface
        /// </summary>
        void EnableGui()
        {
            btnConnect.Enabled = !_deviceApi.IsConnected();
            btnDisconnect.Enabled = _deviceApi.IsConnected();
        }
        /// <summary>
        /// Setup form so user can not press buttons or use the interface.
        /// </summary>
        void DisableGui()
        {
            btnConnect.Enabled = false;
            btnDisconnect.Enabled = false;
        }
        /// <summary>
        /// Update the bottom tool strip with color/text.
        /// </summary>
        /// <param name="label"></param>
        /// <param name="color"></param>
        void UpdateToolStrip(String label, System.Drawing.Color color)
        {
            toolStripStatusLabel1.Text = label;
            toolStripStatusLabel1.ForeColor = color;
        }

        //----------------- Form event handlers ----------------------//
        private void btnConnect_Click(object sender, EventArgs e)
        {
            /* turn off form so user can't double-press button */
            DisableGui();
            /* attempt to connect, and report the results */
            switch (_deviceApi.Connect())
            {
                case 0:
                    UpdateToolStrip("Connected", System.Drawing.Color.DarkGreen);
                    break;
                case -1:
                    UpdateToolStrip("Disconnected, HERO not found.", System.Drawing.Color.DarkRed);
                    break;
                case -5:
                    UpdateToolStrip("Disconnected, HERO connected to Visual Studio.", System.Drawing.Color.DarkRed);
                    break;
                default:
                    UpdateToolStrip("Disconnected", System.Drawing.Color.DarkRed);
                    break;
            }
            /* reenable gui so user can press buttons */
            EnableGui();
        }

        private void btnDisconnect_Click(object sender, EventArgs e)
        {
            /* turn off form so user can't double-press button */
            DisableGui();
            /* disconnect regardless of connect state */
            _deviceApi.Disconnect();
            UpdateToolStrip("Disconnected", System.Drawing.Color.DarkRed);
            /* reenable gui so user can press buttons */
            EnableGui();
        }
        private void timer1_Tick(object sender, EventArgs e)
        {
            /* grab the newest string data from debug buffer */
            String newOutput = _deviceApi.GetCachedOutputBuffer();
            /* paint the new stuff */
            richTextBox1.AppendText(newOutput);
            /* sanity check the presense of HERO */
            switch (_usbSearch.GetHeroCount())
            {
                case 0: /* no HEROs are plugged in... */
                    if (_deviceApi.IsConnected())
                    {
                        /* ... however we were connected, so user must have disconnected it. */
                        btnDisconnect_Click(sender, e);
                        /* update bottom tool strip with a unique error */
                        UpdateToolStrip("HERO was disconnected", System.Drawing.Color.Red);
                    }
                    break;
                case 1: /* one HERO */
                    break;
                default: /* many HEROs */
                    break;
            }
        }

        private void toolStripStatusLabel2_Click_1(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("http://www.ctr-electronics.com/control-system/hro.html#product_tabs_technical_resources");
        }
    }
}
