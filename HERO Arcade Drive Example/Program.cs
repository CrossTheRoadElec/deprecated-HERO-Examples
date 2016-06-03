using System;
using System.Threading;
using Microsoft.SPOT;
using System.Text;

namespace Hero_Arcade_Drive_Example
{
    public class Program
    {
        /* create a talon */
        static CTRE.TalonSrx rightSlave = new CTRE.TalonSrx(4);
        static CTRE.TalonSrx right = new CTRE.TalonSrx(3);
        static CTRE.TalonSrx leftSlave = new CTRE.TalonSrx(2);
        static CTRE.TalonSrx left = new CTRE.TalonSrx(1);

        static StringBuilder stringBuilder = new StringBuilder();

        static CTRE.Gamepad _gamepad = null;

        public static void Main()
        {
            /* loop forever */
            while (true)
            {
                /* drive robot using gamepad */
                Drive();
                /* print whatever is in our string builder */
                Debug.Print(stringBuilder.ToString());
                stringBuilder.Clear();
                /* feed watchdog to keep Talon's enabled */
                CTRE.Watchdog.Feed();
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
        static void Drive()
        {
            if (null == _gamepad)
                _gamepad = new CTRE.Gamepad(CTRE.UsbHostDevice.GetInstance());

            float x = _gamepad.GetAxis(0);
            float y = -1 * _gamepad.GetAxis(1);
            float twist = _gamepad.GetAxis(2);

            Deadband(ref x);
            Deadband(ref y);
            Deadband(ref twist);

            float leftThrot = y + twist;
            float rightThrot = y - twist;

            left.Set(leftThrot);
            leftSlave.Set(leftThrot);
            right.Set(-rightThrot);
            rightSlave.Set(-rightThrot);

            stringBuilder.Append("\t");
            stringBuilder.Append(x);
            stringBuilder.Append("\t");
            stringBuilder.Append(y);
            stringBuilder.Append("\t");
            stringBuilder.Append(twist);

        }
    }
}