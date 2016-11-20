/**
 * Example display animation with a fuel gauge.
 */
/*
*  Software License Agreement
*
* Copyright (C) Cross The Road Electronics.  All rights
* reserved.
* 
* Cross The Road Electronics (CTRE) licenses to you the right to 
* use, publish, and distribute copies of CRF (Cross The Road) firmware files (*.crf) and Software
* API Libraries ONLY when in use with Cross The Road Electronics hardware products.
* 
* THE SOFTWARE AND DOCUMENTATION ARE PROVIDED "AS IS" WITHOUT
* WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT
* LIMITATION, ANY WARRANTY OF MERCHANTABILITY, FITNESS FOR A
* PARTICULAR PURPOSE, TITLE AND NON-INFRINGEMENT. IN NO EVENT SHALL
* CROSS THE ROAD ELECTRONICS BE LIABLE FOR ANY INCIDENTAL, SPECIAL, 
* INDIRECT OR CONSEQUENTIAL DAMAGES, LOST PROFITS OR LOST DATA, COST OF
* PROCUREMENT OF SUBSTITUTE GOODS, TECHNOLOGY OR SERVICES, ANY CLAIMS
* BY THIRD PARTIES (INCLUDING BUT NOT LIMITED TO ANY DEFENSE
* THEREOF), ANY CLAIMS FOR INDEMNITY OR CONTRIBUTION, OR OTHER
* SIMILAR COSTS, WHETHER ASSERTED ON THE BASIS OF CONTRACT, TORT
* (INCLUDING NEGLIGENCE), BREACH OF WARRANTY, OR OTHERWISE
*/
using System.Threading;
using Microsoft.SPOT;
using CTRE.HERO.Module;

namespace DisplayModule_FuelGauge_Example
{
    public class Program
    {
        DisplayModule _displayModule = new DisplayModule(CTRE.HERO.IO.Port8, DisplayModule.OrientationType.Landscape);

        DisplayModule.ResourceImageSprite _main, _cursor;

        public void RunForever()
        {
            _main = _displayModule.AddResourceImageSprite(
                                                           DisplayModule_FuelGauge_Example.Properties.Resources.ResourceManager,
                                                           DisplayModule_FuelGauge_Example.Properties.Resources.BinaryResources.main,
                                                           Bitmap.BitmapImageType.Bmp,
                                                           0, 0);

            _cursor = _displayModule.AddResourceImageSprite(
                                                           DisplayModule_FuelGauge_Example.Properties.Resources.ResourceManager,
                                                           DisplayModule_FuelGauge_Example.Properties.Resources.BinaryResources.cur,
                                                           Bitmap.BitmapImageType.Bmp,
                                                           45, 40);
            double cursorPos = 40;
            double cursorVel = 1;

            System.Random random = new System.Random();

            while (true)
            {
                cursorPos += cursorVel;

                //// Randomize cursor
                //if (cursorVel > 0)
                //    cursorVel = +1.0 * random.NextDouble() + 0.1; // ensure positive
                //else if (cursorVel < 0)
                //    cursorVel = -1.0 * random.NextDouble() - 0.1; // ensure negative


                if (cursorPos > 55)
                {
                    cursorPos = 55;
                    cursorVel = -1;
                }
                if (cursorPos < 5)
                {
                    cursorPos = 5;
                    cursorVel = +1;
                }
                _cursor.SetPosition(45, (int)cursorPos);


                Thread.Sleep(100);
            }
        }
        public static void Main()
        {
            new Program().RunForever();
        }
    }
}
