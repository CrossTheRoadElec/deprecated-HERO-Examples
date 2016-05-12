/**
 * Example demonstrating the position closed-loop servo.
 * Tested with Logitech F350 USB Gamepad inserted into HERO.
 * 
 * Use the mini-USB cable to deploy/debug.
 *
 * Be sure to select the correct feedback sensor using SetFeedbackDevice() below.
 *
 * After deploying/debugging this to your HERO, first use the left Y-stick 
 * to throttle the Talon manually.  This will confirm your hardware setup.
 * Be sure to confirm that when the Talon is driving forward (green) the 
 * position sensor is moving in a positive direction.  If this is not the cause
 * flip the boolena input to the SetSensorDirection() call below.
 *
 * Once you've ensured your feedback device is in-phase with the motor,
 * use the button shortcuts to servo to target positions.  
 *
 * Tweak the PID gains accordingly.
 */
using System;
using System.Threading;
using Microsoft.SPOT;
using System.Text;

namespace Hero_Position_Servo_Example
{
    /** Simple stub to start our project */
    public class Program
    {
        static RobotApplication _robotApp = new RobotApplication();
        public static void Main()
        {
            while(true)
            {
                _robotApp.Run();
            }
        }
    }
    /**
     * The custom robot application.
     */
    public class RobotApplication
    {
        /** scalor to max throttle in manual mode. negative to make forward joystick positive */
        const float kJoystickScaler = -0.3f;

        /** hold bottom left shoulder button to enable motors */
        const uint kEnableButton = 7;

        /** make a talon with deviceId 0 */
        CTRE.TalonSrx _talon = new CTRE.TalonSrx(0);

        /** Use a USB gamepad plugged into the HERO */
        CTRE.Gamepad _gamepad = new CTRE.Gamepad(CTRE.UsbHostDevice.GetInstance());

        /** hold the current button values from gamepad*/
        bool[] _btns = new bool[10];

        /** hold the last button values from gamepad, this makes detecting on-press events trivial */
        bool[] _btnsLast = new bool[10];

        /** some objects used for printing to the console */
        StringBuilder _sb = new StringBuilder();
        int _timeToPrint = 0;

        float _targetPosition = 0;

        uint [] _debLeftY = { 0, 0 }; // _debLeftY[0] is how many times leftY is zero, _debLeftY[1] is how many times leftY is not zeero.

        public void Run()
        {
            /* first choose the sensor */
            _talon.SetFeedbackDevice(CTRE.TalonSrx.FeedbackDevice.CtreMagEncoder_Relative);
            _talon.SetSensorDirection(false);
            //_talon.ConfigEncoderCodesPerRev(XXX), // if using CTRE.TalonSrx.FeedbackDevice.QuadEncoder
            //_talon.ConfigPotentiometerTurns(XXX), // if using CTRE.TalonSrx.FeedbackDevice.AnalogEncoder or CTRE.TalonSrx.FeedbackDevice.AnalogPot

            /* set closed loop gains in slot0 */
            _talon.SetP(0, 0.2f); /* tweak this first, a little bit of overshoot is okay */
            _talon.SetI(0, 0f); 
            _talon.SetD(0, 0f);
            _talon.SetF(0, 0f); /* For position servo kF is rarely used. Leave zero */

            /* use slot0 for closed-looping */
            _talon.SelectProfileSlot(0);

            /* set the peak and nominal outputs, 12V means full */
            _talon.ConfigNominalOutputVoltage(+0.0f, -0.0f);
            _talon.ConfigPeakOutputVoltage(+3.0f, -3.0f);

            /* how much error is allowed?  This defaults to 0. */
            _talon.SetAllowableClosedLoopErr(0,0);

            /* zero the sensor and throttle */
            ZeroSensorAndThrottle();

            /* loop forever */
            while (true)
            {
                Loop10Ms();

                //if (_gamepad.GetConnectionStatus() == CTRE.UsbDeviceConnection.Connected) // check if gamepad is plugged in OR....
                if(_gamepad.GetButton(kEnableButton)) // check if bottom left shoulder buttom is held down.
                {
                    /* then enable motor outputs*/
                    CTRE.Watchdog.Feed();
                }

                /* print signals to Output window */
                Instrument();

                /* 10ms loop */
                Thread.Sleep(10);
            }
        }
        /**
         * Zero the sensor and zero the throttle.
         */
        void ZeroSensorAndThrottle()
        {
            _talon.SetPosition(0); /* start our position at zero, this example uses relative positions */
            _targetPosition = 0;
            /* zero throttle */
            _talon.SetControlMode(CTRE.TalonSrx.ControlMode.kPercentVbus);
            _talon.Set(0);
            Thread.Sleep(100); /* wait a bit to make sure the Setposition() above takes effect before sampling */
        }
        void EnableClosedLoop()
        {
            /* user has let go of the stick, lets closed-loop whereever we happen to be */
            _talon.SetVoltageRampRate(0); /* V per sec */
            _talon.SetControlMode(CTRE.TalonSrx.ControlMode.kPosition);
            _talon.Set(_targetPosition);
        }
        void Loop10Ms()
        {
            /* get all the buttons */
            FillBtns(ref _btns);

            /* get the left y stick, invert so forward is positive */
            float leftY = kJoystickScaler * _gamepad.GetAxis(1);
            Deadband(ref leftY);

            /* debounce the transition from nonzero => zero axis */
            float filteredY = leftY;

            if (filteredY != 0)
            {
                /* put in a ramp to prevent the user from flipping their mechanism */
                _talon.SetVoltageRampRate(12.0f); /* V per sec */
                /* directly control the output */
                _talon.SetControlMode(CTRE.TalonSrx.ControlMode.kPercentVbus);
                _talon.Set(filteredY);
            }
            else if (_talon.GetControlMode() == CTRE.TalonSrx.ControlMode.kPercentVbus)
            {
                _targetPosition = _talon.GetPosition();

                /* user has let go of the stick, lets closed-loop whereever we happen to be */
                EnableClosedLoop();
            }

            /* if a button is pressed while stick is let go, servo position */
            if (filteredY == 0)
            {
                if (_btns[1])
                {
                    _targetPosition = _talon.GetPosition() ; /* twenty rotations forward */
                    EnableClosedLoop();
                }
                else if(_btns[4])
                {
                    _targetPosition = +10.0f; /* twenty rotations forward */
                    EnableClosedLoop();
                }
                else if (_btns[2])
                {
                    _targetPosition = -10.0f; /* twenty rotations reverese */
                    EnableClosedLoop();
                }
            }

            /* copy btns => btnsLast */
            System.Array.Copy(_btns, _btnsLast, _btns.Length);
        }
        /**
         * @return a filter value for the y-axis.  Don't return zero unless we've been in deadband for a number of loops.
         *                                          This is only done because this example will throttle the motor with 
         */
        float FilterLeftY(float y, uint numLoop)
        {
            /* get the left y stick */
            float leftY = -1 * _gamepad.GetAxis(1);
            Deadband(ref leftY);
            if (leftY == 0)
            {
                _debLeftY[1] = 0;
                ++_debLeftY[0];
            }
            else
            {
                _debLeftY[0] = 0;
                ++_debLeftY[1];
            }

            if (_debLeftY[0] > numLoop)
                return 0;
            return y;
        }
        /**
         * If value is within 10% of center, clear it.
         */
        void Deadband(ref float value)
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
        /** throw all the gamepad buttons into an array */
        void FillBtns(ref bool[] btns)
        {
            for (uint i = 1; i < btns.Length; ++i)
                btns[i] = _gamepad.GetButton(i);
        }
        /** occasionally builds a line and prints to output window */
        void Instrument()
        {
            if (--_timeToPrint <= 0)
            {
                _timeToPrint = 20;
                _sb.Clear();
                _sb.Append( "pos=");
                _sb.Append(_talon.GetPosition());
                _sb.Append(" vel=");
                _sb.Append(_talon.GetSpeed());
                _sb.Append(" err=");
                _sb.Append(_talon.GetClosedLoopError());
                _sb.Append(" out%=");
                _sb.Append(_talon.GetOutputVoltage()*100.0f/12.0f);
                Debug.Print(_sb.ToString());
            }
        }
    }
}