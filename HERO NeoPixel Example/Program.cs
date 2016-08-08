/// <summary>
/// Example project controlling NeoPixels through any SPI (S) port.
/// </summary>
using System;
using System.Threading;

namespace NeoPixel_Example
{
    public class Program
    {
        const uint kNumOfNeoPixels = 30;

        /* lets make some NeoPixels, and default the color */
        HeroPixel _heroPixels = new HeroPixel(HeroPixel.OFF, kNumOfNeoPixels);

        uint[] _colorSequence =
        {
            HeroPixel.WHITE,
            HeroPixel.PINK,
            HeroPixel.CYAN,
            HeroPixel.GREEN,
            HeroPixel.MAGENTA,
            HeroPixel.ORANGE,
            HeroPixel.PURPLE,
            HeroPixel.RED,
            HeroPixel.YELLOW,
            HeroPixel.BLUE,
        };
        /// <summary> Color index in color sequence.</summary>
        uint _colIdx = 0;

        /// <summary> Pixel index for when using the color sequence.  </summary>
        uint _pixelIdx = 0;

        /// <summary> Tracks button events.  </summary>
        bool _lastBtn = false;

        /// <summary> TRUE iff color is controlled by user interface, FALSE if streaming through color sequence. </summary>
        bool _colorWheelMode = false;

        private float Deadband(float f)
        {
            if (f > +0.98)
                return 1f;
            if (f < -0.98)
                return -1f;
            if (f > +0.06)
                return f;
            if (f < -0.06)
                return f;
            return 0;
        }
        public void RunForever()
        {
            /* get an X,Y, and btn value.  These could come from potentiometers for example, or USB gamepad */
            Microsoft.SPOT.Hardware.AnalogInput aiBtn = new Microsoft.SPOT.Hardware.AnalogInput(CTRE.HERO.IO.Port1.Analog_Pin3);
            Microsoft.SPOT.Hardware.AnalogInput aiX = new Microsoft.SPOT.Hardware.AnalogInput(CTRE.HERO.IO.Port1.Analog_Pin4);
            Microsoft.SPOT.Hardware.AnalogInput aiY = new Microsoft.SPOT.Hardware.AnalogInput(CTRE.HERO.IO.Port1.Analog_Pin5);

            /* loop forever */
            while (true)
            {
                /* loop sleep */
                Thread.Sleep(5);

                /* get x,y, and button */
                bool btn = aiBtn.Read() < 0.5f;
                float x = (float)aiX.Read() * 2f - 1f; // [-1,1]
                float y = (float)aiY.Read() * 2f - 1f; // [-1,1]
                x = Deadband(x);
                y = Deadband(y);

                /* figure out next color based on current mode */
                if (_colorWheelMode)
                {
                    /* spin around the top circular slice of an HSV surface,
                    https://en.wikipedia.org/wiki/HSL_and_HSV */

                    /* calc sat and hue, use 100% for value */
                    float hueDeg = 0;
                    if (y != 0 || x != 0)
                    {
                        hueDeg = (float)System.Math.Atan2(y, x) * 180f / (float)System.Math.PI;
                        /* keep the angle positive */
                        if (hueDeg < 0) { hueDeg += 360f; }
                    }

                    float sat = (float)System.Math.Sqrt(x * x + y * y);
                    float value = 1f;

                    /* convert to rgb */
                    uint color;
                    uint r, g, b;
                    HsvToRgb.Convert(hueDeg, sat, value, out r, out g, out b);
                    color = r;
                    color <<= 8;
                    color |= g;
                    color <<= 8;
                    color |= b;

                    /* set all LEDs to one color, controlled by analog signals */
                    _heroPixels.setColor(color, 0, _heroPixels.NumberPixels);
                }
                else
                {
                    /* just ramp through the predetermined color sequence */
                    uint color = _colorSequence[_colIdx];
                    _heroPixels.setColor(color, _pixelIdx, 1);
                }

                /* update LEDs using SPI*/
                _heroPixels.writeOutput();

                /* iterate to next pixel */
                if (++_pixelIdx >= _heroPixels.NumberPixels)
                {
                    /* back to first pixel */
                    _pixelIdx = 0;

                    /* step to next color when using the predetermined sequence */
                    if (++_colIdx >= _colorSequence.Length) { _colIdx = 0; }
                }

                /* detect on-press event on button to change mode. */
                if (btn && !_lastBtn) { _colorWheelMode = !_colorWheelMode; }
                _lastBtn = btn;
            }
        }
        /// <summary>
        /// Entry point into program.
        /// </summary>
        public static void Main()
        {
            new Program().RunForever();
        }
    }
}