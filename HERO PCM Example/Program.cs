using System;
using System.Threading;
using Microsoft.SPOT;
using System.Text;

namespace HERO_PCM_Example
{
    public class Program
    {
        /** make a PCM with Device ID of 0 */
        static CTRE.PneumaticControlModule _pcm = new CTRE.PneumaticControlModule(0);

        /** Use a USB gamepad plugged into the HERO */
        static CTRE.Gamepad _gamepad = new CTRE.Gamepad(new CTRE.UsbHostDevice());

        public static void Main()
        {
            /* enable compressor closed loop.  Compressor output will turn automatically
             *  if pressure switch signals low-pressure. */
            _pcm.StartCompressor();

            /* loop forever */
            while (true)
            {
                /* Check our gamepad inputs */
                bool bActivateSolenoid = _gamepad.GetButton(0);

                /* turn on channel 0 if button is held */
                _pcm.SetSolenoidOutput(0, bActivateSolenoid);
                /* turn on channel 1 if button is NOT held */
                _pcm.SetSolenoidOutput(1, !bActivateSolenoid);

                /* only enable actuators (PCM/Talons/etc.) if gamepad is present */
                if (_gamepad.GetConnectionStatus() == CTRE.UsbDeviceConnection.Connected)
                {
                    CTRE.Watchdog.Feed();
                }

                /* yield for a while */
                System.Threading.Thread.Sleep(10);
            }
        }
    }
}
