/**
 * Test project for using TAP detect features of the PigeonIMU.
 * This feature can be used to detect if the robot has hit anything.
 * The hit detect is based on mg per ms (Jerk) thresholds with additional settings to tweak.
 * Feature will be added in next release of Pigeon firmware and CTRE Toolsuite.
 */

using System;
using Microsoft.SPOT;
using System.Text;

namespace HERO_Example
{
    public class Program
    {
        /* the goal is to plug in a Xinput Logitech Gamepad or Xbox360 style controller */
        CTRE.Gamepad _gamepad = new CTRE.Gamepad(CTRE.UsbHostDevice.GetInstance());

        struct ButtonPress{ public bool now; public bool last; public bool WasPressed() { return now && !last; }  }
        
        ButtonPress [] _buttons = new ButtonPress[20];

        CTRE.PigeonImu _pidgy = new CTRE.PigeonImu(0);
        
        byte[] _taps = null;

        public void RunForever()
        {
            while (true)
            {
                if (_gamepad.GetConnectionStatus() == CTRE.UsbDeviceConnection.Connected)
                {
                    CTRE.Watchdog.Feed();
                }

                /* get buttons */
                for (uint i = 1; i < 20; ++i)
                {
                    _buttons[i].last = _buttons[i].now;
                    _buttons[i].now = _gamepad.GetButton(i);
                }
                
                /* yield for a bit, and track timeouts */
                System.Threading.Thread.Sleep(10);
                
                /* pigeon related tasks */
                PidgyTask();
            }
        }
        /**
         * @param x arbitrary float
         * @return string version of x truncated to "X.XX" 
         */
        String Format(float x)
        {
            x *= 100;
            x = (int)x;
            x *= 0.01f;
            return "" + x;
        }

        String TapIdxToStr(int i)
        {
            switch(i)
            {
                case 0: return "X+:";
                case 1: return "X-:";
                case 2: return "Y+:";
                case 3: return "Y-:";
                case 4: return "Z+:";
                case 5: return "Z-:";
            }
            return "";
        }
    
        /* set and get all of the tap settings */
        void PidgtSettings(ushort [] settings)
        {
#if false // explanation of settings belo....
	        unsigned short tap_threshX; //!< Set tap threshold for a specific axis in mg/ms.
	        unsigned short tap_threshY; //!< Set tap threshold for a specific axis in mg/ms.
	        unsigned short tap_threshZ; //!< Set tap threshold for a specific axis in mg/ms.

	        unsigned short tap_count; //!< Minimum consecutive taps (1-4).
	        unsigned short tap_time; //!< Set length between valid taps.Milliseconds between taps.
	        unsigned short tap_time_multi; //!< Set max time between taps to register as a multi-tap.Max milliseconds between taps.

	        unsigned short shake_reject_thresh; //!< Set shake rejection threshold. thresh  Gyro threshold in dps.
	        /** 
	         * Set shake rejection time. Sets the length of time that the gyro 
	         *  must be outside of the threshold set by @e gyro_set_shake_reject_thresh 
	         * before taps are rejected. A mandatory 60 ms is added to this parameter. 
	         * Time in milliseconds. 
	         */
	        unsigned short shake_reject_time;
	        /**
	         *  Set shake rejection timeout.
	         *  Sets the length of time after a shake rejection that the gyro must stay
	         *  inside of the threshold before taps can be detected again. A mandatory
	         *  60 ms is added to this parameter.
	         *  Time in milliseconds.
	         */
	        unsigned short shake_reject_timeout;
#endif
            /* timeout for each set to ensure they are successful */
            const int kTimeoutMs = 10;

            /* speed up certain frames for telemetry */
            _pidgy.SetStatusFrameRateMs(CTRE.PigeonImu.StatusFrameRate.BiasedStatus_6_Accel, 1, kTimeoutMs);
            _pidgy.SetStatusFrameRateMs(CTRE.PigeonImu.StatusFrameRate.CondStatus_12_Taps, 1, kTimeoutMs);

            _pidgy.SetTapThresholdX(settings[0], kTimeoutMs);
            _pidgy.SetTapThresholdY(settings[1], kTimeoutMs);
            _pidgy.SetTapThresholdZ(settings[2], kTimeoutMs);
            _pidgy.SetTapMinConsecutiveCount(settings[3], kTimeoutMs); // [1,2,4]
            _pidgy.SetTapTime(settings[4], kTimeoutMs);
            _pidgy.SetTapMultiTime(settings[5], kTimeoutMs);
            _pidgy.SetTapRejectThreshold(settings[6], kTimeoutMs);
            _pidgy.SetTapRejectTime(settings[7], kTimeoutMs);
            _pidgy.SetTapRejectTimeout(settings[8], kTimeoutMs);

            /* read them back out */
            int[] v = new int[9];
            int i = 0;

            v[i++] = _pidgy.GetTapThresholdX();
            v[i++] = _pidgy.GetTapThresholdY();
            v[i++] = _pidgy.GetTapThresholdZ();
            v[i++] = _pidgy.GetTapMinConsecutiveCount();
            v[i++] = _pidgy.GetTapTime();
            v[i++] = _pidgy.GetTapMultiTime();
            v[i++] = _pidgy.GetTapRejectThreshold();
            v[i++] = _pidgy.GetTapRejectTime();
            v[i++] = _pidgy.GetTapRejectTimeout();

            /* print the read settings */
            String line = "";
            foreach (int value in v)
            {
                line += value + ",";
            }
            Debug.Print(line);
        }
        void PidgyTask()
        {
            /* get tap event coun ts */
            if (_taps == null)
            {
                /* first pass */
                _taps = new byte[6];
                _pidgy.GetTapEventCounts(_taps);
            }
            else
            {
                /* csave old data */
                byte[] old = { 0, 0, 0, 0, 0, 0 };
                System.Array.Copy(_taps, old, 6);
                /* get new data*/
                _pidgy.GetTapEventCounts(_taps);
                /* compare new data against old data */
                for(int i=0;i<6;++i)
                {
                    if(old[i] != _taps[i])
                    {
                        int delta = (int)_taps[i] - (int)old[i];
                        Debug.Print(TapIdxToStr(i) + delta);
                    }
                }
            }

            if (_buttons[6].WasPressed())
            {
                _pidgy.SetYaw(0);
            }
            if (_buttons[7].WasPressed())
            {
                _pidgy.SetYaw(100);
            }

            if (_buttons[1].WasPressed())
            {
                PidgtSettings(new ushort[] {
                    50, 260, 260,
                    1, 400, 25, // tap_count, tap_time, tap_time_multi
                    200, 40, 10 }); // shake_reject_thresh, shake_reject_time, shake_reject_timeout

            }
            if (_buttons[2].WasPressed())
            {
                PidgtSettings(new ushort[] {
                    260, 50, 260,
                    1, 400, 25, // tap_count, tap_time, tap_time_multi
                    200, 40, 10 }); // shake_reject_thresh, shake_reject_time, shake_reject_timeout

            }
            if (_buttons[3].WasPressed())
            {
                PidgtSettings(new ushort[] {
                    260, 260, 50,
                    1, 400, 25, // tap_count, tap_time, tap_time_multi
                    200, 40, 10 }); // shake_reject_thresh, shake_reject_time, shake_reject_timeout
            }
        }
        public static void Main()
        {
            new Program().RunForever();
        }
    }
}
