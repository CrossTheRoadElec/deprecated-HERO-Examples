using System;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;

/*
 * Copyright 2011-2012 Stefan Thoolen (http://www.netmftoolbox.com/)
 * 
 * Modified by Valkyrie-MT - Oct 2012
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

    /// <summary>
    /// SPI Helper to make it easier to use multiple SPI-devices on one SPI-bus
    /// </summary>
    public class MultiSPI
    {
        /// <summary>Reference to the SPI Device. All MultiSPI devices use the same SPI class from the NETMF, so this reference is static</summary>
        private static SPI _SPIDevice;

        private static object SpiLock = new object();

        /// <summary>SPI Configuration. Different for each device, so not a static reference</summary>
        private SPI.Configuration _Configuration;

        /// <summary>There is a software ChipSelect feature because of a bug. True when enabled</summary>
        /// <remarks>see http://netduino.codeplex.com/workitem/3 for more details about the bug.</remarks>
        private bool _Use_SoftwareCS;
        /// <summary>Reference to the latch-pin when using software chip-select</summary>
        private OutputPort _SoftwareCS;
        /// <summary>Active state when using software chip-select</summary>
        private bool _SoftwareCS_ActiveState;

        /// <summary>
        /// Initializes a new SPI device
        /// </summary>
        /// <param name="config">The SPI-module configuration</param>
        public MultiSPI(SPI.Configuration config, bool useSoftwareChipSelect = false)
        {
            // The timing of the Netduino pin 4, Netduino Plus pin 4 and Netduino Mini pin 13 have a small bug, probably in the IC or NETMF itself.
            //
            // They all refer to the same pin ID on the AT91SAM7X512: (int)12
            // - SecretLabs.NETMF.Hardware.Netduino.Pins.GPIO_PIN_D4
            // - SecretLabs.NETMF.Hardware.NetduinoPlus.Pins.GPIO_PIN_D4
            // - SecretLabs.NETMF.Hardware.NetduinoMini.Pins.GPIO_PIN_13
            //
            // To work around this problem we use a software chip select. A bit slower, but it works.
            // We will include this work-around until the actual bug is fixed.
            bool SoftwareChipSelect = false;
            //if ((int)config.ChipSelect_Port == 12 && (
            //    Tools.HardwareProvider == "Netduino" || Tools.HardwareProvider == "NetduinoMini" || Tools.HardwareProvider == "NetduinoPlus"
            //))
            //{
            //    Debug.Print("MultiSPI: Software ChipSelect enabled to prevent timing issues");
            //    Debug.Print("MultiSPI: See http://netduino.codeplex.com/workitem/3 for more");
            //    SoftwareChipSelect = true;
            //}

            // Sets the configuration in a local value
            this._Configuration = config;

            // When we use a software chipset we need to record some more details
            if (SoftwareChipSelect)
            {
                this._SoftwareCS = new OutputPort(config.ChipSelect_Port, !config.ChipSelect_ActiveState);
                this._SoftwareCS_ActiveState = config.ChipSelect_ActiveState;
                this._Use_SoftwareCS = true;
                // Copies the Configuration, but without Chip Select pin
                this._Configuration = new SPI.Configuration(
                    Cpu.Pin.GPIO_NONE,
                    _Configuration.BusyPin_ActiveState,
                    _Configuration.ChipSelect_SetupTime,
                    _Configuration.ChipSelect_HoldTime,
                    _Configuration.Clock_IdleState,
                    _Configuration.Clock_Edge,
                    _Configuration.Clock_RateKHz,
                    _Configuration.SPI_mod,
                    _Configuration.BusyPin,
                    _Configuration.BusyPin_ActiveState
                );
            }

            // If no SPI Device exists yet, we create it's first instance
            if (_SPIDevice == null)
            {
                // Creates the SPI Device
                _SPIDevice = new SPI(this._Configuration);
            }
        }

        /// <summary>
        /// The 8-bit bytes to write to the SPI-buffer
        /// </summary>
        /// <param name="WriteBuffer">An array of 8-bit bytes</param>
        public void Write(byte[] WriteBuffer)
        {
            lock (SpiLock)
            {
                if (this._Use_SoftwareCS) this._SoftwareCS.Write(this._SoftwareCS_ActiveState);
                _SPIDevice.Config = this._Configuration;
                _SPIDevice.Write(WriteBuffer);
                if (this._Use_SoftwareCS) this._SoftwareCS.Write(!this._SoftwareCS_ActiveState);
            }
        }

        /// <summary>
        /// The 16-bit bytes to write to the SPI-buffer
        /// </summary>
        /// <param name="WriteBuffer">An array of 16-bit bytes</param>
        public void Write(ushort[] WriteBuffer)
        {
            lock (SpiLock)
            {
                if (this._Use_SoftwareCS) this._SoftwareCS.Write(this._SoftwareCS_ActiveState);
                _SPIDevice.Config = this._Configuration;
                _SPIDevice.Write(WriteBuffer);
                if (this._Use_SoftwareCS) this._SoftwareCS.Write(!this._SoftwareCS_ActiveState);
            }
        }

        /// <summary>
        /// Reads 8-bit bytes
        /// </summary>
        /// <param name="ReadBuffer">An array with 8-bit bytes to read</param>
        public void Read(byte[] ReadBuffer)
        {
            lock (SpiLock)
            {
                if (this._Use_SoftwareCS) this._SoftwareCS.Write(this._SoftwareCS_ActiveState);
                _SPIDevice.Config = this._Configuration;
                _SPIDevice.WriteRead(ReadBuffer, ReadBuffer); // First parameter is actually a WriteBuffer
                if (this._Use_SoftwareCS) this._SoftwareCS.Write(!this._SoftwareCS_ActiveState);
            }
        }

        /// <summary>
        /// Reads 16-bit bytes
        /// </summary>
        /// <param name="ReadBuffer">An array with 16-bit bytes to read</param>
        public void Read(ushort[] ReadBuffer)
        {
            lock (SpiLock)
            {
                if (this._Use_SoftwareCS) this._SoftwareCS.Write(this._SoftwareCS_ActiveState);
                _SPIDevice.Config = this._Configuration;
                _SPIDevice.WriteRead(ReadBuffer, ReadBuffer); // First parameter is actually a WriteBuffer
                if (this._Use_SoftwareCS) this._SoftwareCS.Write(!this._SoftwareCS_ActiveState);
            }
        }

        /// <summary>
        /// Writes an array of 8-bit bytes to the interface, and reads an array of 8-bit bytes from the interface.
        /// </summary>
        /// <param name="WriteBuffer">An array with 8-bit bytes to write</param>
        /// <param name="ReadBuffer">An array with 8-bit bytes to read</param>
        public void WriteRead(byte[] WriteBuffer, byte[] ReadBuffer)
        {
            lock (SpiLock)
            {
                if (this._Use_SoftwareCS) this._SoftwareCS.Write(this._SoftwareCS_ActiveState);
                _SPIDevice.Config = this._Configuration;
                _SPIDevice.WriteRead(WriteBuffer, ReadBuffer);
                if (this._Use_SoftwareCS) this._SoftwareCS.Write(!this._SoftwareCS_ActiveState);
            }
        }

        /// <summary>
        /// Writes an array of 16-bit bytes to the interface, and reads an array of 16-bit bytes from the interface.
        /// </summary>
        /// <param name="WriteBuffer">An array with 16-bit bytes to write</param>
        /// <param name="ReadBuffer">An array with 16-bit bytes to read</param>
        public void WriteRead(ushort[] WriteBuffer, ushort[] ReadBuffer)
        {
            lock (SpiLock)
            {
                if (this._Use_SoftwareCS) this._SoftwareCS.Write(this._SoftwareCS_ActiveState);
                _SPIDevice.Config = this._Configuration;
                _SPIDevice.WriteRead(WriteBuffer, ReadBuffer);
                if (this._Use_SoftwareCS) this._SoftwareCS.Write(!this._SoftwareCS_ActiveState);
            }
        }

        /// <summary>
        /// Writes an array of 8-bit bytes to the interface, and reads an array of 8-bit bytes from the interface into a specified location in the read buffer.
        /// </summary>
        /// <param name="WriteBuffer">An array with 8-bit bytes to write</param>
        /// <param name="ReadBuffer">An array with 8-bit bytes to read</param>
        /// <param name="StartReadOffset">The offset in time, measured in transacted elements from writeBuffer, when to start reading back data into readBuffer</param>
        public void WriteRead(byte[] WriteBuffer, byte[] ReadBuffer, int StartReadOffset)
        {
            lock (SpiLock)
            {
                if (this._Use_SoftwareCS) this._SoftwareCS.Write(this._SoftwareCS_ActiveState);
                _SPIDevice.Config = this._Configuration;
                _SPIDevice.WriteRead(WriteBuffer, ReadBuffer, StartReadOffset);
                if (this._Use_SoftwareCS) this._SoftwareCS.Write(!this._SoftwareCS_ActiveState);
            }
        }

        /// <summary>
        /// Writes an array of 16-bit bytes to the interface, and reads an array of 16-bit bytes from the interface into a specified location in the read buffer.
        /// </summary>
        /// <param name="WriteBuffer">An array with 16-bit bytes to write</param>
        /// <param name="ReadBuffer">An array with 16-bit bytes to read</param>
        /// <param name="StartReadOffset">The offset in time, measured in transacted elements from writeBuffer, when to start reading back data into readBuffer</param>
        public void WriteRead(ushort[] WriteBuffer, ushort[] ReadBuffer, int StartReadOffset)
        {
            lock (SpiLock)
            {
                if (this._Use_SoftwareCS) this._SoftwareCS.Write(this._SoftwareCS_ActiveState);
                _SPIDevice.Config = this._Configuration;
                _SPIDevice.WriteRead(WriteBuffer, ReadBuffer, StartReadOffset);
                if (this._Use_SoftwareCS) this._SoftwareCS.Write(!this._SoftwareCS_ActiveState);
            }
        }

        /// <summary>
        /// Writes an array of 16-bit bytes to the interface, and reads an array of 16-bit bytes from the interface into a specified location in the read buffer. 
        /// </summary>
        /// <param name="WriteBuffer">An array with 8-bit bytes to write</param>
        /// <param name="ReadBuffer">An array with 8-bit bytes to read</param>
        /// <param name="WriteOffset">The offset in writeBuffer to start write data from</param>
        /// <param name="WriteCount">The number of elements in writeBuffer to write</param>
        /// <param name="ReadOffset">The offset in readBuffer to start read data from</param>
        /// <param name="ReadCount">The number of elements in readBuffer to fill</param>
        /// <param name="StartReadOffset">The offset in time, measured in transacted elements from writeBuffer, when to start reading back data into readBuffer</param>
        public void WriteRead(byte[] WriteBuffer, int WriteOffset, int WriteCount, byte[] ReadBuffer, int ReadOffset, int ReadCount, int StartReadOffset)
        {
            lock (SpiLock)
            {
                if (this._Use_SoftwareCS) this._SoftwareCS.Write(this._SoftwareCS_ActiveState);
                _SPIDevice.Config = this._Configuration;
                _SPIDevice.WriteRead(WriteBuffer, WriteOffset, WriteCount, ReadBuffer, ReadOffset, ReadCount, StartReadOffset);
                if (this._Use_SoftwareCS) this._SoftwareCS.Write(!this._SoftwareCS_ActiveState);
            }
        }

        /// <summary>
        /// Writes an array of 16-bit bytes to the interface, and reads an array of 16-bit bytes from the interface into a specified location in the read buffer. 
        /// </summary>
        /// <param name="WriteBuffer">An array with 16-bit bytes to write</param>
        /// <param name="ReadBuffer">An array with 16-bit bytes to read</param>
        /// <param name="WriteOffset">The offset in writeBuffer to start write data from</param>
        /// <param name="WriteCount">The number of elements in writeBuffer to write</param>
        /// <param name="ReadOffset">The offset in readBuffer to start read data from</param>
        /// <param name="ReadCount">The number of elements in readBuffer to fill</param>
        /// <param name="StartReadOffset">The offset in time, measured in transacted elements from writeBuffer, when to start reading back data into readBuffer</param>
        public void WriteRead(ushort[] WriteBuffer, int WriteOffset, int WriteCount, ushort[] ReadBuffer, int ReadOffset, int ReadCount, int StartReadOffset)
        {
            lock (SpiLock)
            {
                if (this._Use_SoftwareCS) this._SoftwareCS.Write(this._SoftwareCS_ActiveState);
                _SPIDevice.Config = this._Configuration;
                _SPIDevice.WriteRead(WriteBuffer, WriteOffset, WriteCount, ReadBuffer, ReadOffset, ReadCount, StartReadOffset);
                if (this._Use_SoftwareCS) this._SoftwareCS.Write(!this._SoftwareCS_ActiveState);
            }
        }
    }

