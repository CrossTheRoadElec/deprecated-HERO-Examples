using System;
using Microsoft.SPOT.Hardware;
using CTRE;
using CTRE.HERO.IO;
using System.Threading;

//This Program uses the PWMMotorController Class from the HERO SDK
//      and the PWM pin mapping in the latest HERO.IO file.
//The PWM Motor Controller Does not currently obey the Watchdog status.
//      This will be implemented in a future update.


namespace HERO_PWM_Motor_Controller_Example
{
    public class Program
    {
        public static void Main()
        {
            Gamepad joystick = new Gamepad(new CTRE.UsbHostDevice());

            PWMMotorController talon = new PWMMotorController(Port3.PWM_Pin9);
            
            while(true)
            {
                float input = joystick.GetAxis(1); //Y-Axis of left joystick
                bool enable = joystick.GetButton(5); //left Bumper button

                //Run motor based on joystick when enable button is pressed
                talon.Set(enable ? input : 0);

                Thread.Sleep(10);
            }
        }
    }
}
