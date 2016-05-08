using System.Threading;
using System;
using Microsoft.SPOT;

namespace HERO_Motion_Magic_Example
{
    public class Program
    {
        /** talon to control */
        private CTRE.TalonSrx _talon = new CTRE.TalonSrx(0);
        /** desired mode to put talon in */
        private CTRE.TalonSrx.ControlMode _mode = CTRE.TalonSrx.ControlMode.kMotionMagic;
        /** attached gamepad to HERO, tested with Logitech F710 */
        private CTRE.Gamepad _gamepad = new CTRE.Gamepad(new CTRE.UsbHostDevice());
        /** constant slot to use */
        const uint kSlotIdx = 0;
        /** constant how long to wait for receipt when we set a param */
        const uint kTimeoutMs = 1;

        /**
         * Setup all of the configuration parameters.
         */
        public int SetupConfig()
        {
            /* binary OR all the return values so we can make a quick decision if our init was successful */
            int status = 0; 

            _talon.SetFeedbackDevice(CTRE.TalonSrx.FeedbackDevice.CtreMagEncoder_Relative);
            _talon.SetSensorDirection(false); /* make sure positive motor output means sensor moves in position direction */

            status |= _talon.ConfigNeutralMode(CTRE.TalonSrx.NeutralMode.Brake);
            status |= _talon.SetF(kSlotIdx, 0.1153f, kTimeoutMs); // 1300RPM (8874 native sensor units per 100ms) at full motor output
            status |= _talon.SetP(kSlotIdx, 2.00f, kTimeoutMs);
            status |= _talon.SetI(kSlotIdx, 0f, kTimeoutMs);
            status |= _talon.SetD(kSlotIdx, 20f, kTimeoutMs);
            status |= _talon.SetIzone(kSlotIdx, 0, kTimeoutMs);
            status |= _talon.SelectProfileSlot(kSlotIdx); /* select this slot */

            status |= _talon.ConfigNominalOutputVoltage(0f, 0f, kTimeoutMs);
            status |= _talon.ConfigPeakOutputVoltage(+12f, -12f, kTimeoutMs);
            status |= _talon.SetMotionMagicCruiseVelocity(1000f, kTimeoutMs); // RPM
            status |= _talon.SetMotionMagicAcceleration(2000f, kTimeoutMs); // RPM per sec

            /* home the relative sensor */
            status |= _talon.SetPosition(0, kTimeoutMs);

            return status;
        }
        public void RunForever()
        {
            int initStatus = SetupConfig(); /* configuration */
            while (initStatus != 0)
            {
                Instrument.PrintConfigError();
                initStatus = SetupConfig(); /* (re)config*/
            }

            while (true)
            {
                /* get joystick params */
                float leftY = -1f * _gamepad.GetAxis(1);
                bool btnTopLeftShoulder = _gamepad.GetButton(5);
                bool btnBtmLeftShoulder = _gamepad.GetButton(7);
                Deadband(ref leftY);

                /* keep robot enabled */
                if (_gamepad.GetConnectionStatus() == CTRE.UsbDeviceConnection.Connected)
                    CTRE.Watchdog.Feed();

                /* set the control mode based on button pressed */
                if (btnTopLeftShoulder)
                    _mode = CTRE.TalonSrx.ControlMode.kPercentVbus;
                if (btnBtmLeftShoulder)
                    _mode = CTRE.TalonSrx.ControlMode.kMotionMagic;

                /* calc the Talon output based on mode */
                if (_mode == CTRE.TalonSrx.ControlMode.kPercentVbus)
                {
                    float output = leftY; // [-1, +1] percent duty cycle
                    _talon.SetControlMode(_mode);
                    _talon.Set(output);
                }
                else if (_mode == CTRE.TalonSrx.ControlMode.kMotionMagic)
                {
                    float servoToRotation = leftY * 10;// [-10, +10] rotations
                    _talon.SetControlMode(_mode);
                    _talon.Set(servoToRotation);
                }
                /* instrumentation */
                Instrument.Process(_talon);

                /* wait a bit */
                System.Threading.Thread.Sleep(5);
            }
        }
        public static void Deadband(ref float val)
        {
            if (val > 0.10f) { /* do nothing */ }
            else if (val < -0.10f) { /* do nothing */ }
            else { val = 0; } /* clear val since its within deadband */
        }
        /** singleton instance  and entry point into program */
        public static void Main()
        {
            Program program = new Program();
            program.RunForever();
        }
    }
}
