using System;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using CTRE;

/*
 * This example allows you to test basic limit
 * switch functionality.  You can change the 
 * limit mode to use soft limits or disable
 * the limit switches entirely.
 */

namespace HERO_Limit_Switch_Example
{
    public class Program
    {
        public static void Main()
        {
            TalonSrx test = new TalonSrx(0);
            Gamepad stick = new Gamepad(UsbHostDevice.GetInstance());

            /* loop forever */
            while (true)
            {
                if (stick.GetConnectionStatus() == UsbDeviceConnection.Connected)
                {
                    CTRE.Watchdog.Feed();
                }

                //This call is redundant but you can un-comment to guarantee limit switches will work or change the mode.
                //test.ConfigLimitMode(TalonSrx.LimitMode.kLimitMode_SwitchInputsOnly);

                Debug.Print("Rev: " + test.IsRevLimitSwitchClosed() + "  | Fwd: " + test.IsFwdLimitSwitchClosed());

                test.Set(stick.GetAxis(1));

                /* wait a bit */
                System.Threading.Thread.Sleep(10);
            }
        }
    }
}
