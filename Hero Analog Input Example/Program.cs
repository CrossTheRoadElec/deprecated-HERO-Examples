using System;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using CTRE.HERO.IO;


namespace $safeprojectname$
{
    public class Program
    {
        /* create analog inputs from PORT1(AUSX) */
        static AnalogInput analogInput0 = new AnalogInput(Port1.Analog_Pin3);
        static AnalogInput analogInput1 = new AnalogInput(Port1.Analog_Pin4);
        static AnalogInput analogInput2 = new AnalogInput(Port1.Analog_Pin5);

        public static void Main()
        {
            /* create some variables to store latest reads */
            double read0;
            double read1;
            double read2;
            /* loop forever */
            while (true)
            {
                /* grab analog value */
                read0 = analogInput0.Read();
                read1 = analogInput1.Read();
                read2 = analogInput2.Read();

                /* print the three analog inputs as three columns */
                Debug.Print("" + read0 + "\t" + read1 + "\t" + read2);

				/* wait a bit */
				System.Threading.Thread.Sleep(10);
			}
        }
    }
}
