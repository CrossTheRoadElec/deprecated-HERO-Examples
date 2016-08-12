//This Example shows how to use the current limiting feature of the TalonSRX.
//Requires TalonSRX Firmware of 10.13 or later.
//Requires HERO SDK version 4.4.0.11 or later.
//These versions are included in the CTRE Toolsuite versions 4.4.1.0 and later.


using System.Text;
using System.Threading;
using Microsoft.SPOT;

namespace HERO_Current_Limit_Example
{
    public class Program
    {
        /* create a talon */
        static CTRE.TalonSrx talon = new CTRE.TalonSrx(0);

        static StringBuilder stringBuilder = new StringBuilder();

        static CTRE.Gamepad _gamepad = new CTRE.Gamepad(CTRE.UsbHostDevice.GetInstance());

        public static void Main()
        {
            /* loop forever */
            while (true)
            {
                /* run motor using gamepad */
                Run();
                /* print whatever is in our string builder */
                Debug.Print(stringBuilder.ToString());
                stringBuilder.Clear();
                /* feed watchdog to keep Talon enabled if Gamepad is inserted. */
                if (_gamepad.GetConnectionStatus() == CTRE.UsbDeviceConnection.Connected)
                {
                    CTRE.Watchdog.Feed();
                }
                /* run this task every 20ms */
                Thread.Sleep(20);
            }
        }
        /**
         * If value is within 10% of center, clear it.
         * @param value [out] floating point value to deadband.
         */
        static void Deadband(ref float value)
        {
            if (value < -0.10)
            {
                /* outside of deadband */
            }
            else if (value > +0.10)
            {
                /* outside of deadband */
            }
            else
            {
                /* within 10% so zero it */
                value = 0;
            }
        }
        static void Run()
        {
            float x = _gamepad.GetAxis(1);

            Deadband(ref x);

            //Set the Maximum Current Limit for the Talon (in Amps)
            talon.SetCurrentLimit(10);
            //Enable the Current Limiting Feature.
            talon.EnableCurrentLimit(true);

            talon.Set(x);

            float current = talon.GetOutputCurrent();

            stringBuilder.Append("\t");
            stringBuilder.Append(current);
            stringBuilder.Append("\t");


        }
    }
}
