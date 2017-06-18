/*
 *  Software License Agreement
 *
 * Copyright (C) Cross The Road Electronics.  All rights
 * reserved.
 * 
 * Cross The Road Electronics (CTRE) licenses to you the right to 
 * use, publish, and distribute copies of CRF (Cross The Road) binary firmware files (*.crf) 
 * and software example source ONLY when in use with Cross The Road Electronics hardware products.
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

/**
 * Basic Example demonstrating the CTRE Power Distribution Panel.
 * Test PDP has a default device ID of '0'.
 * 
 * Use the mini-USB cable to deploy/debug.
 */

using System;
using Microsoft.SPOT;

namespace HERO_Power_Distribution_Panel_Example {
    public class Program {
        public static void Main()
        {
            /* create a PDP object, pass the device ID '0' (must match Device ID in HERO LifeBoat */
            CTRE.PowerDistributionPanel pdp = new CTRE.PowerDistributionPanel(0);

            while (true) {

                float channel0_Amps = pdp.GetChannelCurrent(0);
                float channel1_Amps = pdp.GetChannelCurrent(1);
                float vbattery = pdp.GetVoltage();

                Debug.Print("ch0:" + channel0_Amps + " A" +
                            "ch1:" + channel1_Amps + " A" +
                            "Bat:" + vbattery + " V" +
                            "");

                System.Threading.Thread.Sleep(100);
            }
        }
    }
}
