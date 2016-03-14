/**
 * Example demonstrating the motion profile control mode of Talon SRX.
 * Press and release button5 (top left shoulder button on Logitech Gamepad) to stream a Motion Profile to Talon SRX and execute it.
 * Press and release button7 (bottom left shoulder button on Logitech Gamepad) to put Talon into Voltage Compensation mode, where left y axis stick 
 * will control the output voltage (-14V to 14V).
 */
using System;
using System.Threading;
using Microsoft.SPOT;
using System.Text;

namespace Hero_Motion_Profile_Example
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
        CTRE.TalonSrx _talon = new CTRE.TalonSrx(6);
        CTRE.Gamepad _gamepad = new CTRE.Gamepad(CTRE.UsbHostDevice.GetInstance());
        bool[] _btns = new bool[10];
        bool[] _btnsLast = new bool[10];
        StringBuilder _sb = new StringBuilder();
        int _timeToPrint = 0;
        int _timeToColumns= 0;

        CTRE.TalonSrx.MotionProfileStatus _motionProfileStatus = new CTRE.TalonSrx.MotionProfileStatus();

        public void Run()
        {
            _talon.SetControlMode(CTRE.TalonSrx.ControlMode.kVoltage);

            _talon.SetFeedbackDevice(CTRE.TalonSrx.FeedbackDevice.CtreMagEncoder_Relative);
            _talon.SetSensorDirection(false);

            _talon.SetVoltageRampRate(0.0f);

            _talon.SetP(0, 0.80f);
            _talon.SetI(0, 0f);
            _talon.SetD(0, 0f);
            _talon.SetF(0, 0.09724488664269079041176191004297f);
            _talon.SelectProfileSlot(0);
            _talon.ConfigNominalOutputVoltage(0f, 0f);
            _talon.ConfigPeakOutputVoltage(+12.0f, -12.0f);
            _talon.ChangeMotionControlFramePeriod(5);

            /* loop forever */
            while (true)
            {
                _talon.GetMotionProfileStatus(out _motionProfileStatus);

                Drive();

                CTRE.Watchdog.Feed();

                Instrument();

                Thread.Sleep(5);
            }
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
        void FillBtns(ref bool[] btns)
        {
            for (uint i = 1; i < btns.Length; ++i)
                btns[i] = _gamepad.GetButton(i);
        }
        void Drive()
        {
            FillBtns(ref _btns);
            float y = -1 * _gamepad.GetAxis(1);

            Deadband(ref y);

            _talon.ProcessMotionProfileBuffer();

            /* button handler, if btn5 pressed launch MP, if btn7 pressed, enter voltage mode */
            if (_btns[5] && !_btnsLast[5])
            {
                /* disable MP to clear IsLast */
                _talon.SetControlMode(CTRE.TalonSrx.ControlMode.kMotionProfile);
                _talon.Set(0);
                CTRE.Watchdog.Feed();
                Thread.Sleep(10);
                /* buffer new pts in HERO */
                CTRE.TalonSrx.TrajectoryPoint point = new CTRE.TalonSrx.TrajectoryPoint();
                _talon.ClearMotionProfileHasUnderrun();
                _talon.ClearMotionProfileTrajectories();
                for (uint i = 0; i < MotionProfile.kNumPoints; ++i)
                {
                    point.position = (float)MotionProfile.Points[i][0];
                    point.velocity = (float)MotionProfile.Points[i][1];
                    point.timeDurMs = MotionProfile.kDurationMs;
                    point.isLastPoint = (i + 1 == MotionProfile.kNumPoints) ? true : false;
                    point.zeroPos = (i == 0) ? true : false;
                    point.velocityOnly = false;
                    point.profileSlotSelect = 0;
                    _talon.PushMotionProfileTrajectory(point);
                }
                /* send the first few pts to Talon */
                for (int i = 0; i < 5; ++i)
                {
                    CTRE.Watchdog.Feed();
                    Thread.Sleep(10);
                    _talon.ProcessMotionProfileBuffer();
                }
                /*start MP */
                _talon.Set(1);
            }
            else if (_btns[7] && !_btnsLast[7])
            {
                _talon.SetControlMode(CTRE.TalonSrx.ControlMode.kVoltage);
            }

            /* if in voltage mode, update output voltage */
            if (_talon.GetControlMode() == CTRE.TalonSrx.ControlMode.kVoltage)
            {
                _talon.Set(14.0f * y);
            }

            /* copy btns => btnsLast */
            System.Array.Copy(_btns, _btnsLast, _btns.Length);
        }
        void Instrument()
        {
            if (--_timeToColumns <= 0)
            {
                _timeToColumns = 400;
                _sb.Clear();
                _sb.Append("topCnt \t");
                _sb.Append("btmCnt \t");
                _sb.Append("setval \t");
                _sb.Append("HasUndr\t");
                _sb.Append("IsUnder\t");
                _sb.Append(" IsVal \t");
                _sb.Append(" IsLast\t");
                _sb.Append("VelOnly\t");
                _sb.Append(" TargetPos[AndVelocity] \t");
                _sb.Append("Pos[AndVelocity]");
                Debug.Print(_sb.ToString());
            }

            if (--_timeToPrint <= 0)
            {
                _timeToPrint = 40;

                _sb.Clear();
                _sb.Append(_motionProfileStatus.topBufferCnt);
                _sb.Append("\t\t");
                _sb.Append(_motionProfileStatus.btmBufferCnt);
                _sb.Append("\t\t");
                _sb.Append(_motionProfileStatus.outputEnable);
                _sb.Append("\t\t");
                _sb.Append(_motionProfileStatus.hasUnderrun ? "   1   \t" : "       \t");
                _sb.Append(_motionProfileStatus.isUnderrun ? "   1   \t" : "       \t");
                _sb.Append(_motionProfileStatus.activePointValid ? "   1   \t" : "       \t");
            
                _sb.Append(_motionProfileStatus.activePoint.isLastPoint  ? "   1   \t" : "       \t");
                _sb.Append(_motionProfileStatus.activePoint.velocityOnly ? "   1   \t" : "       \t");

                _sb.Append(_motionProfileStatus.activePoint.position);
                _sb.Append("[");
                _sb.Append(_motionProfileStatus.activePoint.velocity);
                _sb.Append("]\t");


                _sb.Append("\t\t\t");
                _sb.Append(_talon.GetPosition());
                _sb.Append("[");
                _sb.Append(_talon.GetSpeed());
                _sb.Append("]");

                Debug.Print(_sb.ToString());
            }
        }
    }
}