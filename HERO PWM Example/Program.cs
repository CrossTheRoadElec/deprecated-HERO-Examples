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

/**
 * This example has two additional references under the Solution Explorer...
 * "Microsoft.SPOT.Hardware" and "Microsoft.SPOT.Hardware.PWM" 
 */
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;

namespace HERO_PWM_Example
{
    public class Program
    {
        public static void Main()
        {
            //Gamepad for input
            CTRE.Gamepad _gamepad = new CTRE.Gamepad(CTRE.UsbHostDevice.GetInstance());

            //simple PWM for fine control of pulse width, period, timing...
            uint period = 50000; //period between pulses
            uint duration = 1500; //duration of pulse
            PWM pwm_9 = new PWM(CTRE.HERO.IO.Port3.PWM_Pin9, period, duration, PWM.ScaleFactor.Microseconds, false);
            pwm_9.Start(); //starts the signal

            // ...and just a PWM SpeedController for motor controller (Victor SP, Talon SR, Victor 888, etc.)...
            CTRE.PWMSpeedController pwmSpeedController = new CTRE.PWMSpeedController(CTRE.HERO.IO.Port3.PWM_Pin4);

            while (true)
            {
                /* only enable motor control (PWM/CAN) if gamepad is connected.  Logitech gamepads may be disabled using the X/D switch */
                if (_gamepad.GetConnectionStatus() == CTRE.UsbDeviceConnection.Connected)
                {
                    CTRE.Watchdog.Feed();
                }

                /* let axis control the pwm speed controller */
                pwmSpeedController.Set(0.10f); /* 10% */

                /* let button1 control the explicit PWM pin duration*/
                if (_gamepad.GetButton(1) == true)
                {
                    pwm_9.Duration = 2000; /* 2.0ms */
                }
                else
                {
                    pwm_9.Duration = 1000; /* 1.0ms */
                }

                /* yield for a bit, this controls this task's frequency */
                System.Threading.Thread.Sleep(10);
            }
        }
    }
}
