// See the Microchip PDF for specs on the ENC28J60 here:
// http://ww1.microchip.com/downloads/en/devicedoc/39662a.pdf
// mIP is free software licensed under the Apache License 2.0

using System;
using System.Threading;
using Microsoft.SPOT.Hardware;
using System.Diagnostics;


namespace Networking
{
	internal class ENC28J60Driver
	{
		MultiSPI spi;
		InterruptPort irq;
		private bool resetPending = false;
        private bool RxResetPending = false;
        
        /// <summary>
        /// The Machine Time that the Link came up... MaxValue means the link is currently DOWN
        /// </summary>
        private TimeSpan linkUpTime = TimeSpan.MaxValue;
        private TimeSpan lastReceiveReset = TimeSpan.MinValue;
        private TimeSpan lastRestart = TimeSpan.MinValue;
        private TimeSpan lastPacketReceived = Utility.GetMachineTime();
        private Timer watchdog = null;
        private bool initialized = false;
        private bool linkInit = false;

		byte[] _mac = new byte[] { 0x5c, 0x86, 0x4a, 0x00, 0x00, 0xdd };

		readonly object BankAccess = new object();
		readonly object SendAccess = new object();
		readonly object InterruptAccess = new object();
        
        private const int checkDelay = 5000;
        private byte lastFilter = 0x00;

        Timer regCheckTimer = null;

		byte[] spibuf = new byte[3];

		/// <summary>
		/// Event handler for link status changes
		/// </summary>
		/// <param name="sender">The ENC28J60 chip instance from which the event originates</param>
		/// <param name="time">Time when the event occurred</param>
		/// <param name="isUp">Indicates link status</param>
		public delegate void LinkChangedEventHandler(ENC28J60Driver sender, DateTime time, bool isUp);

		/// <summary>
		/// Fires when the phy goes up or down
		/// </summary>
		public event LinkChangedEventHandler OnLinkChangedEvent;

		/// <summary>
		/// Event handler for incominig ethernet frames
		/// </summary>
		/// <param name="sender">The ENC28J60 chip instance from which the event originates</param>
		/// <param name="time">Time when the event occurred</param>
		public delegate void FrameArrivedEventHandler(ENC28J60Driver sender, byte[] frame, DateTime timeReceived);

		/// <summary>
		/// Fires when one or more incoming frames have arrived from network
		/// </summary>
		public event FrameArrivedEventHandler OnFrameArrived;

		private byte Low(ushort a) { return (byte)(a & 0xFF); }
		private byte High(ushort a) { return (byte)(a >> 8); }

		private const ushort RXSTART = 0x0000;    // Start of 8k
		private const ushort RXSTOP = 0x19FF; //0x18F8;     // The rest of the buffer
		private const ushort TXSTART = 0x1AFF;  //0x18FF;    // A bit more than the max frame size of 1518
		private const ushort TXSTOP = 0x1FFF;     // End of 8k

		private ushort NextPacketPointer = RXSTART;
		//private byte ENCRevID = 0x00;

		/// <summary>
		/// Creates a new ENC28J60 driver object instance, the chip will be held
		/// in reset until the Start method is called
		/// </summary>
		/// <param name="irqPin">Host pin to use for interrupt request</param>
		/// <param name="csPin">Host pin to use for SPI chip select</param>
		/// <param name="spiModule">Host SPI module to use</param>
		public ENC28J60Driver(Cpu.Pin irqPin, Cpu.Pin csPin, SPI.SPI_module spiModule)
		{
			irq = new InterruptPort(irqPin, true, Port.ResistorMode.PullDown, Port.InterruptMode.InterruptEdgeLevelLow);
			irq.OnInterrupt += new NativeEventHandler(irq_OnInterrupt);

            

            // http://www.mikroe.com/forum/viewtopic.php?f=91&p=192252
			var cfg = new SPI.Configuration(csPin, false, 0, 0, false, true, 8000, spiModule);
			spi = new MultiSPI(cfg);
		}

		//
		
		
		/// <summary>
		/// Starts up the driver and establishes a link
		/// </summary>
		/// <param name="mac">Six byte array containing MAC address</param>
		public void Start(byte[] mac)
		{
			_mac = mac;

			Start();
		}

		/// <summary>
		/// Starts up the driver and establishes a link
		/// </summary>
		public void Start()
		{
			byte i;

			var loopMax = 100;  // about 10 seconds with a 100 ms sleep

			// Wait for CLKRDY to become set.
			// Bit 3 in ESTAT is an unimplemented bit.  If it reads out as '1' that
			// means the part is in RESET or there is something wrong with the SPI 
			// connection.  This loop makes sure that we can communicate with the 
			// ENC28J60 before proceeding.
			// 2.2 -- After a Power-on Reset, or the ENC28J60 is removed from Power-Down mode, the
			//        CLKRDY bit must be polled before transmitting packets
			do
			{
				i = ReadCommonReg(ESTAT);
				loopMax--;
				Thread.Sleep(100);
			} while(((i & 0x08) != 0 || (~i & ESTAT_CLKRDY) != 0) && loopMax > 0);

            if (Adapter.VerboseDebugging) Debug.WriteLine("ESTAT: " + ReadCommonReg(ESTAT).ToString());

			if (loopMax <= 0) throw new Exception("Unable to Communicate to the Ethernet Controller.  Check the InterfaceProfile in your Adapter.Start()");

            regCheckTimer = new Timer(new TimerCallback(CheckRegisters), null, checkDelay, checkDelay);
            watchdog = new Timer(new TimerCallback(WatchDogCheck), null, 10000, 3000);

			Restart();
            //Init();
		}

        internal void Restart()
        {
            if (lastRestart > Utility.GetMachineTime().Subtract(new TimeSpan(0, 0, 7))) return;   // Allow Restarts at no more frequent than every 7 seconds
            
            watchdog.Change(10000, 10000);  // wave off the watchdog for the next 10 seconds...

            lastRestart = Utility.GetMachineTime();

            // RESET the entire ENC28J60, clearing all registers
            SendSystemReset();

            Thread.Sleep(100);
            
            Init();
        }

		internal void Init()
		{
            if (Adapter.VerboseDebugging) Debug.WriteLine("Init called");
            initialized = false;

			SetupReceiveBuffer();
            SetupTransmitBuffer();

			//TODO: port this line
			//MACPut(0x00);
			

			SetupMacFilters();

			// 7.2.3
			//BfsReg(ECON2, ECON2_AUTOINC);

			// Enter Bank 2 and configure the MAC
			//CurrentBank = MACON1;

			// Enable the receive portion of the MAC
			WriteReg(MACON1, MACON1_TXPAUS | MACON1_RXPAUS | MACON1_MARXEN);
			WriteReg(MACON2, 0x00);
			// Pad packets to 60 bytes, add CRC, and check Type/Length field.
			//  #if defined(FULL_DUPLEX)
			//      WriteReg((BYTE)MACON3, MACON3_PADCFG0 | MACON3_TXCRCEN | MACON3_FRMLNEN | MACON3_FULDPX);
			//      WriteReg((BYTE)MABBIPG, 0x15);	
			// #else
			WriteReg(MACON3, MACON3_PADCFG0 | MACON3_TXCRCEN | MACON3_FRMLNEN);
			WriteReg(MABBIPG, 0x12);
			//#endif

			// Allow infinite deferals if the medium is continuously busy 
			// (do not time out a transmission if the half duplex medium is 
			// completely saturated with other people's data)
            // And reject packets that do not have a Pure Preamble
			WriteReg(MACON4, MACON4_DEFER | MACON4_PUREPRE);

			// Late collisions occur beyond 63+8 bytes (8 bytes for preamble/start of frame delimiter)
			// 55 is all that is needed for IEEE 802.3, but ENC28J60 B5 errata for improper link pulse 
			// collisions will occur less often with a larger number.
			WriteReg(MACLCON2, 63);

			// Set non-back-to-back inter-packet gap to 9.6us.  The back-to-back 
			// inter-packet gap (MABBIPG) is set by MACSetDuplex() which is called 
			// later.
			WriteReg(MAIPGL, 0x12);
			WriteReg(MAIPGH, 0x0C);

			// Set the maximum packet size which the controller will accept
			WriteReg(MAMXFLL, Low(MaxFrameSize));	 // 1518 is the IEEE 802.3 specified limit
			WriteReg(MAMXFLH, High(MaxFrameSize)); // 1518 is the IEEE 802.3 specified limit

			// Enter Bank 3 and initialize physical MAC address registers
			//CurrentBank = MAADR1;
			WriteReg(MAADR1, _mac[0]);
			WriteReg(MAADR2, _mac[1]);
			WriteReg(MAADR3, _mac[2]);
			WriteReg(MAADR4, _mac[3]);
			WriteReg(MAADR5, _mac[4]);
			WriteReg(MAADR6, _mac[5]);

			// Disable the CLKOUT output to reduce EMI generation
			WriteReg(ECOCON, 0x00);	// Output off (0V)
			//WriteReg((BYTE)ECOCON, 0x01);	// 25.000MHz
			//WriteReg((BYTE)ECOCON, 0x03);	// 8.3333MHz (*4 with PLL is 33.3333MHz)

			// Get the Rev ID so that we can implement the correct errata workarounds
			//ENCRevID = ReadEthReg(EREVID);

			// Disable half duplex loopback in PHY.  Bank bits changed to Bank 2 as a 
			// side effect.
			WritePhyReg(PHCON2, PHCON2_HDLDIS);

			// Configure LEDA to display LINK status, LEDB to display TX/RX activity
			//SetLEDConfig(0x3472);

			// Enable Interrupt on Link Change.
			// 12.1.5 -- To receive it, the host controller must set the PHIE.PLNKIE and PGEIE bits
			WritePhyReg(PHIE, 0x0012);

			// Set the MAC and PHY into the proper duplex state
			//  #if defined(FULL_DUPLEX)
			//      WritePHYReg(PHCON1, PHCON1_PDPXMD);
			//  #elif defined(HALF_DUPLEX)
			WritePhyReg(PHCON1, 0x0000);
			//  #else
			// Use the external LEDB polarity to determine weather full or half duplex 
			// communication mode should be set.  
			//    {
			//        REG Register;
			//        PHYREG PhyReg;

			//        // Read the PHY duplex mode
			//        PhyReg = ReadPHYReg(PHCON1);
			//        DuplexState = PhyReg.PHCON1bits.PDPXMD;

			//        // Set the MAC to the proper duplex mode
			//        BankSel(MACON3);
			//        Register = ReadMACReg((BYTE)MACON3);
			//        Register.MACON3bits.FULDPX = PhyReg.PHCON1bits.PDPXMD;
			//        WriteReg((BYTE)MACON3, Register.Val);

			//        // Set the back-to-back inter-packet gap time to IEEE specified 
			//        // requirements.  The meaning of the MABBIPG value changes with the duplex
			//        // state, so it must be updated in this function.
			//        // In full duplex, 0x15 represents 9.6us; 0x12 is 9.6us in half duplex
			//        WriteReg((BYTE)MABBIPG, PhyReg.PHCON1bits.PDPXMD ? 0x15 : 0x12);	
			//    }
			//#endif

			//CurrentBank = ERDPTL;		// Return to default Bank 0

			resetPending = false;
            initialized = true;

			// reset NETMF irq
			irq.ClearInterrupt();

			// After this, the interrupt output will be set when a new frame arrives! 
			// 7.2.1 -- If an interrupt is desired whenever a packet is received, set EIE.PKTIE and EIE.INTIE.
            WriteReg(EIE, EIE_PKTIE | EIE_INTIE | EIE_LINKIE | EIE_RXERIE | EIE_TXERIE);

            // 7.2.3
            BfsReg(ECON2, ECON2_AUTOINC);  

			// Enable packet reception
			BfsReg(ECON1, ECON1_RXEN);
            //WriteReg(ECON1, ECON1_RXEN);

            if (Adapter.VerboseDebugging) Debug.WriteLine("Packet Reception enabled.");
		}

        private void SetupReceiveBuffer()
		{
			// Start up in Bank 0 and configure the receive buffer boundary pointers 
			// and the buffer write protect pointer (receive buffer read pointer)
			//bool WasDiscarded = true;
			//NextPacketLocation = RXSTART;

			// 6.1 -- Receive Buffer Initialization
			WriteReg(ERXSTL, Low(RXSTART));
			WriteReg(ERXSTH, High(RXSTART));
			WriteReg(ERXRDPTL, Low(RXSTART));	// Write low byte first
			WriteReg(ERXRDPTH, High(RXSTART));	// Write high byte last
			WriteReg(ERXNDL, Low(RXSTOP));
			WriteReg(ERXNDH, High(RXSTOP));

			NextPacketPointer = RXSTART;
		}

        private void SetupTransmitBuffer()
        {
            // Set end of transmit region
            WriteReg(ETXNDL, Low(TXSTOP));
            WriteReg(ETXNDH, High(TXSTOP));
            WriteReg(ETXSTL, Low(TXSTART));
            WriteReg(ETXSTH, High(TXSTART));

            // Write a permanant per packet control byte of 0x00
            WriteReg(EWRPTL, Low(TXSTART));
            WriteReg(EWRPTH, High(TXSTART));
        }

		private void SetupMacFilters()
		{
			// Enter Bank 1 and configure Receive Filters 
			// (No need to reconfigure - Unicast OR Broadcast with CRC checking is 
			// acceptable)
			// Write ERXFCON_CRCEN only to ERXFCON to enter promiscuous mode

			// Promiscious mode example:
			//BankSel(ERXFCON);
			//WriteReg((BYTE)ERXFCON, ERXFCON_CRCEN);

			// UCEN = Unicast MAC Addresses are enabled
			// BCEN = Broadcast MAC Address is enabled
			// MCEN = Multicast MAC Addresses are enabled
//            WriteReg(ERXFCON, ERXFCON_UCEN | ERXFCON_BCEN | ERXFCON_MCEN);
			//WriteReg(ERXFCON, ERXFCON_UCEN | ERXFCON_MCEN);

			// Valkyrie-MT: Based on empirical evidence, I believe that the MCEN flag uses the Least significant byte instead of the Most significant byte
			// Making it very much broken.  So, I am using the pattern match instead...

			// Valkyrie-MT: This allows for MDNS and LLMNR IPv4 multicast through pattern match
			WriteReg(ERXFCON, ERXFCON_UCEN | ERXFCON_BCEN | ERXFCON_PMEN | ERXFCON_CRCEN); // unicast, broadcast and pattern match (for multicast), and CRC checking
			WriteReg(EPMOH, 0x00);  // Offset
			WriteReg(EPMOL, 0x00);

			WriteReg(EPMM0, 0x07);  // Bitmask for which bytes are used in the Pattern Match
			WriteReg(EPMM1, 0x00);
			WriteReg(EPMM2, 0x00);
			WriteReg(EPMM3, 0xc0);
			WriteReg(EPMM4, 0x01);
			WriteReg(EPMM5, 0x00);
			WriteReg(EPMM6, 0x00);
			WriteReg(EPMM7, 0x00);

			WriteReg(EPMCSH, 0xa0);  // Checksum for the pattern match
			WriteReg(EPMCSL, 0x1f);  // I used Windows Calc to sum 4 byte groups in hex, then used the "Not" function for 1's Complement


			// Valkyrie-MT: This allows for All IPv4 multicast through pattern match
			//WriteReg(ERXFCON, ERXFCON_UCEN | ERXFCON_BCEN | ERXFCON_PMEN); // unicast, broadcast and pattern match (for multicast)
			//WriteReg(EPMOH, 0x00);  // Offset
			//WriteReg(EPMOL, 0x00);

			//WriteReg(EPMM0, 0x07);
			//WriteReg(EPMM1, 0x00);
			//WriteReg(EPMM2, 0x00);
			//WriteReg(EPMM3, 0x00);
			//WriteReg(EPMM4, 0x00);
			//WriteReg(EPMM5, 0x00);
			//WriteReg(EPMM6, 0x00);
			//WriteReg(EPMM7, 0x00);

			//WriteReg(EPMCSH, 0xa0);
			//WriteReg(EPMCSL, 0xff);
		}

        private void WatchDogCheck(object o)
        {
            if (Adapter.VerboseDebugging) Debug.WriteLine(DateTime.Now.ToString() + " -- Watchdog Check!  Mem = " + Microsoft.SPOT.Debug.GC(false));

            if (linkUpTime == TimeSpan.MaxValue)
            {
                // Link is supposedly down, let's verify
                UpdateLinkState(true);

                if (linkUpTime == TimeSpan.MaxValue) return;  // Link is really down, nothing to do...?
            }

            irq_OnInterrupt(0, 0, DateTime.Now);

            //if (Utility.GetMachineTime().Subtract(lastPacketReceived).Ticks > TimeSpan.TicksPerSecond * 5)
            //{

            //        ReceiveReset();
            //        //WritePhyReg(PHIE, 0x0012);  // Enable Interrupts

            //    if (resetPending) Restart();
            //}

            ////watchdog.Change(10000, 10000);
        }

        private void CheckRegisters(object o)
        {
            if (RxResetPending || resetPending) return;

            var eir = ReadCommonReg(EIR);

            if ((eir & EIR_RXERIF) != 0)
            {
                BfcReg(EIR, EIR_RXERIF);  // Clear the RX error flag
                if (Adapter.VerboseDebugging) Debug.WriteLine("CheckRegisters Clearing RX Error - EIR: " + eir.ToString() + " => " + ReadCommonReg(EIR).ToString()); 
            }
            
            var eie = ReadCommonReg(EIE);

            if (ReadCommonReg(EIE) != (EIE_INTIE | EIE_PKTIE | EIE_LINKIE | EIE_TXERIE | EIE_RXERIE))
            {
                WriteReg(EIE, EIE_INTIE | EIE_PKTIE | EIE_LINKIE | EIE_TXERIE | EIE_RXERIE);
                if (Adapter.VerboseDebugging) Debug.WriteLine("CheckRegisters Correction - EIE: " + eie.ToString() + " => " + ReadCommonReg(EIE).ToString()); 
            }

            var econ1 = ReadCommonReg(ECON1);

            if (!RxResetPending && ((ReadCommonReg(ECON1) & ECON1_RXEN) == 0))
            {
                WriteReg(ECON1, ECON1_RXEN);
                if (Adapter.VerboseDebugging) Debug.WriteLine("CheckRegisters Correction - ECON1: " + econ1.ToString() + " => " + ReadCommonReg(ECON1).ToString());
            }
        }


		ushort _bank = 0;

		/// <summary>
		/// Read bank from chip and write bank back to chip here
		/// </summary>
		private ushort CurrentBank 
		{
			get
			{
				return _bank;
			}

			set
			{
				lock(BankAccess)
			    {
					value = (ushort)((value >> 8) & 0x03);
					if (value == _bank) return;     
			   
					BfcReg(ECON1, ECON1_BSEL1 | ECON1_BSEL0);
					BfsReg(ECON1, (byte)value);

					_bank = value;

					//TODO: 
					//Debug.Assert(value == GetActualBank from ECON1 ,"Verify that you have selected the correct InterfaceProfile in the Networking.Adapter.Start method");
				}
			}
		}

		// hanzibal: added interrupts and event routing
		// TODO: Here's a potential bug. Since async events does not seem
		// to be supported by framework, we should fifo them to be called
		// from outside this ISR - this in order not to mess up the register bank
		// of the chip
		void irq_OnInterrupt(uint data1, uint data2, System.DateTime time)
		{
            //Debug.WriteLine("Interrupt Called");

            if (!initialized) return;

			// this lock avoids interleaved register bank switching
            lock (InterruptAccess)
            {
                //Debug.WriteLine("Int Processed after waiting: " + DateTime.Now.Subtract(time).Seconds + " seconds");
                //Debug.WriteLine("Free memory = " + Microsoft.SPOT.Debug.GC(false));

                //if (Adapter.VerboseDebugging) Debug.WriteLine("Int Process");

                var eir = ReadCommonReg(EIR);
                //if (Adapter.VerboseDebugging) Debug.WriteLine("EIR: " + eir.ToString());
                

                if (eir == 0) CheckRegisters(null);  

                if ((eir & EIR_TXERIF) != 0)
                {
                    Debug.WriteLine("TX Error Detected");
                    BfcReg(EIR, EIR_TXERIF);
                    // resetPending = true;
                }
                else if ((eir & EIR_RXERIF) != 0)
                {
                    Debug.WriteLine("RX Error Detected");
                    BfcReg(EIR, EIR_RXERIF);


                   // resetPending = true;                    
                }

                //if (Utility.GetMachineTime().Subtract(lastPacketReceived).Ticks > TimeSpan.TicksPerSecond * 5)
                //{
                  //  RxResetPending = true;
                    
                    //ReceiveReset();
                    //WritePhyReg(PHIE, 0x0012);  // Enable Interrupts

                //}

                if (!resetPending && !RxResetPending)   // This will allow any queued up called to exit...
                {
                    //if (Adapter.VerboseDebugging) Debug.WriteLine("EIR: " + eir.ToString());


                    UpdateLinkState();

                    // now we can fire events
                    //if (linkChange && OnLinkChangedEvent != null) OnLinkChangedEvent.Invoke(this, time, IsLinkUp);

                    //byte packetCount = ReadEthReg(EPKTCNT);
                    //bool rxError = (ReadCommonReg(EIR) & EIR_RXERIF) != 0;
                    //Debug.WriteLine("Starting Loop -- " + packetCount);

                    // We have 1 or more frames in the buffer.  I am checking PKTIF then using short-circuit to check EPKTCNT only when PKTIF == 0.  
                    while (OnFrameArrived != null && !resetPending && (((ReadCommonReg(EIR) & EIR_PKTIF) != 0) || ReadEthReg(EPKTCNT) > 0)) //&& !rxError
                    {
      //                  Debug.WriteLine("Packets left in buffer = " + packetCount + " @ " + time.ToString());
                        OnFrameArrived.Invoke(this, ReceiveFrame(), time);
                        //packetCount = ReadEthReg(EPKTCNT);
                      //  rxError = (ReadCommonReg(EIR) & EIR_RXERIF) != 0;
                    }



        //            Debug.WriteLine("Done with packets");

                    //if (rxError)
//                    if ((ReadCommonReg(EIR) & EIR_RXERIF) != 0)
  //                  {
                        //if (rxError) 
    //                        Debug.WriteLine("RECEIVE ERROR Detected!");
                        //else
                        //    Debug.WriteLine("BUFFER OVERFLOW Detected!");

                        //TCP.Connections.Clear();
                        //Debug.WriteLine("Executing a Complete RESET at " + DateTime.Now);
                        //resetPending = true;
      //              }

                    //if (Adapter.VerboseDebugging) Debug.WriteLine("exit");
                }

                // reset NETMF irq
                irq.ClearInterrupt();
            }


            if (resetPending)
			{
                resetPending = false;
				Thread.Sleep(50);  // This allows the potentially backed up calls that were waiting for a lock to unroll...?  
				Restart();
			}
            else if (Utility.GetMachineTime() < lastReceiveReset.Add(new TimeSpan(0, 0, 12)) && Utility.GetMachineTime().Subtract(linkUpTime).Ticks > TimeSpan.TicksPerSecond * 12 && Utility.GetMachineTime().Subtract(lastPacketReceived).Ticks > TimeSpan.TicksPerSecond * 12)
            {
                // Link has been up for 12 seconds, but no packets.  Reset.  

                resetPending = false;
                Thread.Sleep(50);  // This allows the potentially backed up calls that were waiting for a lock to unroll...?  
                Restart();
            }
            else if (!RxResetPending && Utility.GetMachineTime().Subtract(lastPacketReceived).Ticks > TimeSpan.TicksPerSecond * 5)
            {
                // Link has been up for 5 seconds, but no packets.  Reset Receive Only. 

                ReceiveReset();
                //Restart();
                Thread.Sleep(10);
            }
		}

        internal void UpdateLinkState(bool force = false)
        {
            // 12.1.5 -- Performing an MII read on the PHIR register will clear the LINKIF, PGIF and PLNKIF bits automatically
            //var physicalInterruptRegister = ReadPhyReg(PHIR);

            //bool linkChange = (physicalInterruptRegister & PHIR_PLNKIF) != 0;
            var eir = ReadCommonReg(EIR);

            if (linkInit == false || (eir & EIR_LINKIF) != 0 || force)  // Link state Changed?
            {
                bool linkUp = (ReadPhyReg(PHSTAT2) & PHSTAT2_LSTAT) != 0;
                ReadPhyReg(PHIR);  // Clear the Link Interrupt Flag (LINKIF)

                if (linkUp && linkUpTime == TimeSpan.MaxValue)
                {
                    // Link was down and is now up!
                    linkUpTime = Utility.GetMachineTime();
                    if (OnLinkChangedEvent != null) OnLinkChangedEvent.Invoke(this, DateTime.Now, true);
                }
                else if (!linkUp && linkUpTime != TimeSpan.MaxValue)
                {
                    // Link was up and is now down!
                    linkUpTime = TimeSpan.MaxValue;
                    if (OnLinkChangedEvent != null) OnLinkChangedEvent.Invoke(this, DateTime.Now, false);
                }

                linkInit = true;
            }

        }

		internal void SendFrame(byte[] frame, int startIndex = 0)
		{
            lock (SendAccess)
			{
				// Set pointer to Start of Transmit Region
				WriteReg(EWRPTL, Low(TXSTART));
				WriteReg(EWRPTH, High(TXSTART));         

				// Populate buffer with our frame data
				WriteBuffer(frame, startIndex);

				// Set the pointer to the end of the frame, otherwise it sends lots of random buffer bytes as trailer
				WriteReg(ETXNDL, Low((ushort)(TXSTART + frame.Length)));
				WriteReg(ETXNDH, High((ushort)(TXSTART + frame.Length)));

				// Transmit the frame!
				BfsReg(ECON1, ECON1_TXRTS);
                //WriteReg(ECON1, 0x0C);

				int timeout = 100;

				while ((ReadCommonReg(ECON1) & ECON1_TXRTS) != 0 && timeout-- > 0) Thread.Sleep(5);

				//while ((ReadCommonReg(EIR) & (EIR_TXERIF | EIR_TXIF)) == 0) Thread.Sleep(1);

                if ((ReadCommonReg(EIR) & EIR_TXERIF) != 0) BfcReg(ECON1, ECON1_TXRTS);  //WriteReg(ECON1, 0x04); 

			}

			// Originally called MACFlush

			//// Reset transmit logic if a TX Error has previously occured
			//// This is a silicon errata workaround
			//if ((ReadEthReg(EIR) & EIR_TXERIF) != 0)
			//{
			//    BfsReg(ECON1, ECON1_TXRST);
			//    BfcReg(ECON1, ECON1_TXRST);
			//}
			
			//BfcReg(EIR, EIR_TXERIF | EIR_TXIF);

			//// Start the transmission
			//// After transmission completes (MACIsTxReady() returns TRUE), the packet 
			//// can be modified and transmitted again by calling MACFlush() again.
			//// Until MACPutHeader() is called, the data in the TX buffer will not be 
			//// corrupted.
			//BfsReg(ECON1, ECON1_TXRTS);

			//// Revision B5 and B7 silicon errata workaround
			//if (ENCRevID == 0x05u || ENCRevID == 0x06u)
			//{
			//    while ((ReadEthReg(EIR) & (EIR_TXERIF | EIR_TXIF)) == 0) Thread.Sleep(1);

			//    if ((ReadEthReg(EIR) & EIR_TXERIF) != 0)
			//    {
			//        ushort ReadPtrSave;
			//        ushort TXEnd;
			//        byte[] TXStatus;
			//        Byte i;

			//        // Cancel the previous transmission if it has become stuck set
			//        BfcReg(ECON1, ECON1_TXRTS);

			//        // Save the current read pointer (controlled by application)
			//        ReadPtrSave = ((ushort)ReadEthReg((byte)ERDPTL)) & (((ushort)ReadEthReg((byte)ERDPTH)) << 8);

			//        // Get the location of the transmit status vector
			//        TXEnd.v[0] = ReadETHReg(ETXNDL).Val;
			//        TXEnd.v[1] = ReadETHReg(ETXNDH).Val;
			//        TXEnd.Val++;

			//        // Read the transmit status vector
			//        WriteReg(ERDPTL, TXEnd.v[0]);
			//        WriteReg(ERDPTH, TXEnd.v[1]);
			//        MACGetArray((BYTE*)&TXStatus, sizeof(TXStatus));

			//        // Implement retransmission if a late collision occured (this can 
			//        // happen on B5 when certain link pulses arrive at the same time 
			//        // as the transmission)
			//        for (i = 0; i < 16u; i++)
			//        {
			//            if (ReadETHReg(EIR).EIRbits.TXERIF && TXStatus.bits.LateCollision)
			//            {
			//                // Reset the TX logic
			//                BFSReg(ECON1, ECON1_TXRST);
			//                BFCReg(ECON1, ECON1_TXRST);
			//                BFCReg(EIR, EIR_TXERIF | EIR_TXIF);

			//                // Transmit the packet again
			//                BFSReg(ECON1, ECON1_TXRTS);
			//                while (!(ReadETHReg(EIR).Val & (EIR_TXERIF | EIR_TXIF))) ;

			//                // Cancel the previous transmission if it has become stuck set
			//                BFCReg(ECON1, ECON1_TXRTS);

			//                // Read transmit status vector
			//                WriteReg(ERDPTL, TXEnd.v[0]);
			//                WriteReg(ERDPTH, TXEnd.v[1]);
			//                MACGetArray((BYTE*)&TXStatus, sizeof(TXStatus));
			//            }
			//            else
			//            {
			//                break;
			//            }
			//        }

			//        // Restore the current read pointer
			//        WriteReg(ERDPTL, ReadPtrSave.v[0]);
			//        WriteReg(ERDPTH, ReadPtrSave.v[1]);
			//    }
			//}
		}        
		
		private byte[] ReceiveFrame()
		{
			byte[] frame = null;

            lock (SendAccess)
            {

                byte packetCount = ReadEthReg(EPKTCNT);

                ThrottleFrameReception(packetCount);

                if (Adapter.VerboseDebugging) Debug.WriteLine("Packet Count is: " + packetCount.ToString());

                ushort readPointer = (ushort)(ReadEthReg(ERXRDPTL) | ReadEthReg(ERXRDPTH) << 8);
                ushort writePointer = (ushort)(ReadEthReg(ERXWRPTL) | ReadEthReg(ERXWRPTH) << 8);

                if (Adapter.VerboseDebugging) Debug.WriteLine("ERXRDPT: " + readPointer.ToString());
                if (Adapter.VerboseDebugging) Debug.WriteLine("ERXWRPT: " + writePointer.ToString());

                if (packetCount >= 255 || readPointer > RXSTOP || writePointer > RXSTOP)
                {
                    // Something is very wrong here...
                    Debug.WriteLine("Something is very wrong here...");

                    ReceiveReset();

                    return null;
                }

                // Set read pointer to start reading
                WriteReg(ERDPTL, Low(NextPacketPointer));
                WriteReg(ERDPTH, High(NextPacketPointer));
                //            WriteReg(ERDPTH, High(NextPacketPointer)); 
                //            WriteReg(ERDPTL, Low(NextPacketPointer));

                if (Adapter.VerboseDebugging) Debug.WriteLine("Setting Read Pointer in buffer to: " + NextPacketPointer);

                ushort LastPacketPointer = NextPacketPointer;

                // At the beginning of a new packet is the Next Packet Pointer and Status Vector.  
                // So, we need to grab that now.  
                // See Table 7-3 for complete breakdown of meaning of vector
                frame = ReadBuffer(6);
                var ReceivedByteCount = (ushort)(frame[2] | frame[3] << 8);
                var StatusVector = (ushort)(frame[4] | frame[5] << 8);
                NextPacketPointer = (ushort)(frame[0] | frame[1] << 8);

                if (Adapter.VerboseDebugging) Debug.WriteLine("Setting Next Packet Pointer to: " + NextPacketPointer);

                bool ReceivedOk = (frame[4] & (1 << 7)) != 0;
                bool ZeroBitMissing = (frame[5] & (1 << 7)) != 0;

                if (NextPacketPointer > RXSTOP || NextPacketPointer == 0 || !ReceivedOk || ZeroBitMissing)
                {
                    if (Adapter.VerboseDebugging) Debug.WriteLine("Buffer Read Fix Experiment");

                    // Set read pointer to start reading
                    WriteReg(ERDPTL, Low(LastPacketPointer));
                    WriteReg(ERDPTH, High(LastPacketPointer));

                    Thread.Sleep(10);
                    frame = ReadBuffer(6);
                    ReceivedByteCount = (ushort)(frame[2] | frame[3] << 8);
                    StatusVector = (ushort)(frame[4] | frame[5] << 8);
                    NextPacketPointer = (ushort)(frame[0] | frame[1] << 8);

                    if (Adapter.VerboseDebugging) Debug.WriteLine("Setting Next Packet Pointer to: " + NextPacketPointer);
                }

                if (Adapter.VerboseDebugging) Debug.WriteLine("Received Byte Count: " + ReceivedByteCount);
                if (Adapter.VerboseDebugging) Debug.WriteLine("Status Vector: " + StatusVector);
                //var bit = (b & (1 << bitNumber - 1)) != 0;

                // 23:16 => frame[4]
                // 31:24 => frame[5]
                bool CRCError = (frame[4] & (1 << 4)) != 0;
                bool LengthCheckError = (frame[4] & (1 << 5)) != 0;
                bool LengthOutOfRange = (frame[4] & (1 << 6)) != 0;
                ReceivedOk = (frame[4] & (1 << 7)) != 0;
                bool IsMulticast = (frame[5] & (1 << 0)) != 0;
                bool IsBroadcast = (frame[5] & (1 << 1)) != 0;
                bool UnknownOpcode = (frame[5] & (1 << 5)) != 0;
                ZeroBitMissing = (frame[5] & (1 << 7)) != 0;
                bool IsValidBroadcast1 = IsBroadcast && frame[0] == 0xFF && frame[1] == 0xFF && frame[2] == 0xFF && frame[3] == 0xFF && frame[4] == 0xFF && frame[5] == 0xFF;

                if (Adapter.VerboseDebugging)
                {
                    Debug.WriteLine("**** Packet Received Status Vector ****");
                    Debug.WriteLine("CRC Error: " + CRCError);
                    Debug.WriteLine("Length Check Error: " + LengthCheckError);
                    Debug.WriteLine("Length Out of Range (large than 1500 bytes?): " + LengthOutOfRange);
                    Debug.WriteLine("Received OK: " + ReceivedOk);  // This is false for IPv6 Packets ?
                    Debug.WriteLine("Is Multicast? " + IsMulticast);
                    Debug.WriteLine("Is Broadcast? " + IsBroadcast);
                    Debug.WriteLine("UnknownOpcode: " + UnknownOpcode);  // IPv6 Packets will have an unknown opcode? seems so...
                }

                //if ()
                //{
                //    Debug.WriteLine("Aborting this packet!!!!");
                //    //BfsReg(ECON2, ECON2_PKTDEC);
                //    return null;
                //}


                ////TODO: Detect buffer overrun better than this...
                //if (ReceivedByteCount - 4 > MaxFrameSize)
                //{
                //    Debug.WriteLine("Executing a RECEIVE RESET! ");

                //    // Reset the Receive System
                //    BfsReg(ECON1, ECON1_RXRST);
                //    Thread.Sleep(1);
                //    BfcReg(ECON1, ECON1_RXRST);

                //    NextPacketPointer = RXSTART;

                //    // 6.1 -- Receive Buffer Initialization
                //    WriteReg(ERXSTL, Low(RXSTART));
                //    WriteReg(ERXSTH, High(RXSTART));
                //    WriteReg(ERXRDPTL, Low(RXSTART));	// Write low byte first
                //    WriteReg(ERXRDPTH, High(RXSTART));	// Write high byte last
                //    WriteReg(ERXNDL, Low(RXSTOP));
                //    WriteReg(ERXNDH, High(RXSTOP));

                //    frame = null;

                //    BfsReg(EIE, EIE_PKTIE | EIE_INTIE | EIE_LINKIE);
                //    // Re-Enable Reception of packets
                //    BfsReg(ECON1, ECON1_RXEN);

                //    return frame;
                //}


                //TODO: Detect buffer overrun better than this...
                if (CRCError || !ReceivedOk || UnknownOpcode || ZeroBitMissing || ReceivedByteCount - 4 > MaxFrameSize || ReceivedByteCount < 38)
                {
                    if (Adapter.VerboseDebugging)
                    {
                        Debug.WriteLine("Error detected\nStatus Vector = " + StatusVector);
                        Debug.WriteLine("Rec'd Byte Count = " + ReceivedByteCount);
                        Debug.WriteLine("Packets: " + ReadEthReg(EPKTCNT));
                    }

                    //WriteReg(ERDPTL, Low(NextPacketPointer));  // not needed if autoinc is set
                    //WriteReg(ERDPTH, High(NextPacketPointer));

                    if (ReceivedByteCount > MaxFrameSize + 4 || ZeroBitMissing || ReceivedByteCount < 38)
                    {
                        //ReceiveReset();

                        int trycount = 256;

                        Debug.WriteLine("## Dropping " + ReadEthReg(EPKTCNT).ToString() + " packets ## - Experimental");

                        while (ReadEthReg(EPKTCNT) > 0 && trycount-- > 0)
                            BfsReg(ECON2, ECON2_PKTDEC);  // Decrement packet count to 0 (skipping any other packets in the buffer)

                        NextPacketPointer = (ushort)(ReadEthReg(ERXWRPTL) | ReadEthReg(ERXWRPTH) << 8);

                        if (Adapter.VerboseDebugging) Debug.WriteLine("Setting Next Packet Pointer to: " + NextPacketPointer);

                        if ((NextPacketPointer - 1) < RXSTART || (NextPacketPointer - 1) > RXSTOP)
                        {
                            if (Adapter.VerboseDebugging) Debug.WriteLine("Read Pointer set to STOP");

                            // Always write Low, then High  7.2.4
                            WriteReg(ERXRDPTL, Low(RXSTOP));
                            WriteReg(ERXRDPTH, High(RXSTOP));

                            //WriteReg(ERXRDPTL, Low(RXSTOP));
                            //WriteReg(ERXRDPTH, High(RXSTOP));
                        }
                        else
                        {
                            // Always write Low, then High  7.2.4
                            if (Adapter.VerboseDebugging) Debug.WriteLine("Read Pointer set to NextPacketPointer");
                            WriteReg(ERXRDPTL, Low((ushort)(NextPacketPointer - 1)));
                            WriteReg(ERXRDPTH, High((ushort)(NextPacketPointer - 1)));
                            //WriteReg(ERXRDPTL, Low((ushort)(NextPacketPointer - 1)));
                            // WriteReg(ERXRDPTH, High((ushort)(NextPacketPointer - 1)));
                        }

                        WriteReg(ERDPTL, Low(NextPacketPointer));
                        WriteReg(ERDPTH, High(NextPacketPointer));

                        frame = null;
                        return frame;
                    }

                    if ((NextPacketPointer - 1) < RXSTART || (NextPacketPointer - 1) > RXSTOP)
                    {
                        if (Adapter.VerboseDebugging) Debug.WriteLine("Read Pointer set to STOP");
                        // Always write Low, then High  7.2.4
                        WriteReg(ERXRDPTL, Low(RXSTOP));
                        WriteReg(ERXRDPTH, High(RXSTOP));
                    }
                    else
                    {
                        // Always write Low, then High  7.2.4
                        //Debug.WriteLine("Read Pointer set to NextPacketPointer");
                        WriteReg(ERXRDPTL, Low((ushort)(NextPacketPointer - 1)));
                        WriteReg(ERXRDPTH, High((ushort)(NextPacketPointer - 1)));
                    }

                    BfsReg(ECON2, ECON2_PKTDEC);

                    // Set read pointer to start reading
                    WriteReg(ERDPTL, Low(NextPacketPointer));
                    WriteReg(ERDPTH, High(NextPacketPointer));

                    frame = null;
                    return frame;
                }

                // The rest is the packet data itself (except last 4 bytes are CRC, checked when CRCEN is enabled, so we don't need to keep this)
                frame = ReadBuffer(ReceivedByteCount - 4);

                bool SourceAndDestinationAreSame = (frame[0] == frame[6] && frame[1] == frame[7] && frame[2] == frame[8] && frame[3] == frame[9] && frame[4] == frame[10] && frame[5] == frame[11]);
                bool IsValidBroadcast = IsBroadcast && frame[0] == 0xFF && frame[1] == 0xFF && frame[2] == 0xFF && frame[3] == 0xFF && frame[4] == 0xFF && frame[5] == 0xFF;
                bool IsValidMulticast = IsMulticast && frame[0] == 0x01 && frame[1] == 0x00 && frame[2] == 0x5e;
                bool IsValidUnicast = !IsBroadcast && Networking.Adapter.MacAddress[0] == frame[0]
                                                   && Networking.Adapter.MacAddress[1] == frame[1]
                                                   && Networking.Adapter.MacAddress[2] == frame[2]
                                                   && Networking.Adapter.MacAddress[3] == frame[3]
                                                   && Networking.Adapter.MacAddress[4] == frame[4]
                                                   && Networking.Adapter.MacAddress[5] == frame[5];

                if (SourceAndDestinationAreSame)
                {
                    if (Adapter.VerboseDebugging) Debug.WriteLine("Source and Desination MAC: " + new byte[6] { frame[6], frame[7], frame[8], frame[9], frame[10], frame[11] }.ToHexString());
                    //RxResetPending = true;
                    //ReceiveReset();
                    //RxResetPending = false;
                    //frame = null;
                    //return frame;
                }

                //if (LastPacketPointer + ReceivedByteCount + 4) != NextPacketPointer


                //watchdog.Change(5000, 10000);

                if (Adapter.VerboseDebugging) Debug.WriteLine("Source MAC: " + new byte[6] { frame[6], frame[7], frame[8], frame[9], frame[10], frame[11] }.ToHexString() + " -- Destination MAC: " + new byte[6] { frame[0], frame[1], frame[2], frame[3], frame[4], frame[5] }.ToHexString());

                // 7.2.4 -- In addition to advancing the receive buffer read pointer,
                //          after each packet is fully processed, the host controller
                //          must write a ‘1’ to the ECON2.PKTDEC bit.
                if ((NextPacketPointer - 1) < RXSTART || (NextPacketPointer - 1) > RXSTOP)
                {
                    if (Adapter.VerboseDebugging) Debug.WriteLine("Read Pointer set to STOP");

                    WriteReg(ERXRDPTL, Low(RXSTOP));
                    WriteReg(ERXRDPTH, High(RXSTOP));
                    WriteReg(ERXRDPTL, Low(RXSTOP));
                    WriteReg(ERXRDPTH, High(RXSTOP));
                }
                else
                {
                    //Debug.WriteLine("Read Pointer set to NextPacketPointer");
                    WriteReg(ERXRDPTL, Low((ushort)(NextPacketPointer - 1)));
                    WriteReg(ERXRDPTH, High((ushort)(NextPacketPointer - 1)));
                    WriteReg(ERXRDPTL, Low((ushort)(NextPacketPointer - 1)));
                    WriteReg(ERXRDPTH, High((ushort)(NextPacketPointer - 1)));
                }

                BfsReg(ECON2, ECON2_PKTDEC);

                //          Debug.WriteLine("Returning Frame of Length: " + frame.Length);

                if ((IsValidBroadcast || IsValidMulticast || IsValidUnicast) && !SourceAndDestinationAreSame)
                {
                    lastPacketReceived = Utility.GetMachineTime();
                    regCheckTimer.Change(checkDelay, checkDelay);
                    return frame;
                }
                else
                {
                    return null;
                }
            }
		}

        private void ThrottleFrameReception(byte packetCount)
        {
            byte newFilter;

            if (packetCount < 10)
            {
                newFilter = ERXFCON_UCEN | ERXFCON_BCEN | ERXFCON_PMEN | ERXFCON_CRCEN;   // unicast, broadcast and pattern match (for multicast), and CRC checking 
            }
            else if (packetCount < 20)
                newFilter = ERXFCON_UCEN | ERXFCON_PMEN | ERXFCON_CRCEN;   // we are getting backed up, let's ignore broadcast packets
            else
                newFilter = ERXFCON_UCEN | ERXFCON_CRCEN;   // big backlog... ignore all packets that are not directly addressed!

            if (newFilter != lastFilter)
            {
                if (Adapter.VerboseDebugging) Debug.WriteLine("Throttle set to filter: " + (packetCount < 10 ? "None" : (packetCount < 20 ? "All Broadcasts" : "All but directly addressed packets")));
                
                // we need to apply a filter change...
                WriteReg(ERXFCON, newFilter);
                lastFilter = newFilter;
            }
        }

        private void ReceiveReset()
        {
            // Pointers are messed up, doing a receive only reset
            // Reset the Receive System

            if (Utility.GetMachineTime() < lastReceiveReset.Add(new TimeSpan(0, 0, 5)) || Utility.GetMachineTime().Subtract(linkUpTime).Ticks < TimeSpan.TicksPerSecond * 5)
            {
                //resetPending = true;
                return;
            }

            if (Adapter.VerboseDebugging) Debug.WriteLine(DateTime.Now.ToString() + " Executing a RECEIVE RESET! ");

            RxResetPending = true;

            lastReceiveReset = Utility.GetMachineTime();

            BfsReg(ECON1, ECON1_RXRST);
            Thread.Sleep(1);
            
            //// Disable packet reception
            //BfcReg(ECON1, ECON1_RXEN);  // Disable Receive, while we setup again...

            int timeout = 100;

            // Make sure any last packet which was in-progress when RXEN was cleared 
            // is completed
            while ((ReadCommonReg(ESTAT) & ESTAT_RXBUSY) != 0 && timeout-- > 0) Thread.Sleep(2);

            timeout = 100;

            // If a packet is being transmitted, wait for it to finish
            while ((ReadCommonReg(ECON1) & ECON1_TXRTS) != 0 && timeout-- > 0) Thread.Sleep(2);

            Debug.WriteLine("Dropping " + ReadEthReg(EPKTCNT).ToString() + " packet(s)");




            //BfcReg(ECON1, ECON1_RXEN);  // Disable Receive, while we setup again...
            


            //Debug.WriteLine("Packet Count is2: " + ReadEthReg(EPKTCNT).ToString());

            BfcReg(EIR, EIR_RXERIF);  // Clear the RX error flag
            //BfsReg(EIE, EIE_RXERIE);  // Enable an interrupt on RX Error 


            SetupReceiveBuffer();

            //SetupOtherMacRegisters();

            int trycount = 256;

            Debug.WriteLine("Packet Count is3: " + ReadEthReg(EPKTCNT).ToString());

            Debug.WriteLine("1*** ERXRDPT: " + ((ushort)(ReadEthReg(ERXRDPTL) | ReadEthReg(ERXRDPTH) << 8)).ToString());
            Debug.WriteLine("1*** ERXWRPT: " + ((ushort)(ReadEthReg(ERXWRPTL) | ReadEthReg(ERXWRPTH) << 8)).ToString());

            while (ReadEthReg(EPKTCNT) > 0 && trycount-- > 0)
                BfsReg(ECON2, ECON2_PKTDEC);  // Decrement packet count to 0 (skipping any other packets in the buffer)

            Thread.Sleep(2);

            Debug.WriteLine("2*** ERXRDPT: " + ((ushort)(ReadEthReg(ERXRDPTL) | ReadEthReg(ERXRDPTH) << 8)).ToString());
            Debug.WriteLine("2*** ERXWRPT: " + ((ushort)(ReadEthReg(ERXWRPTL) | ReadEthReg(ERXWRPTH) << 8)).ToString());

            Debug.WriteLine("Packet Count is4: " + ReadEthReg(EPKTCNT).ToString());

            //WriteReg(ERXWRPTL, Low(RXSTART));  // These are READ-ONLY Pointers!  Setting them to something should not work...
            //WriteReg(ERXWRPTH, High(RXSTART));

            //WriteReg(EPKTCNT, 0);  // Reset Packet Count?   -- does not work.  This register is probably read-only
            NextPacketPointer = 0; // = (ushort)(ReadEthReg(ERXWRPTL) | ReadEthReg(ERXWRPTH) << 8);

            //if ((NextPacketPointer - 1) < RXSTART || (NextPacketPointer - 1) > RXSTOP)
            //{
            //    Debug.WriteLine("Read Pointer set to STOP2");

            //    WriteReg(ERXRDPTL, Low(RXSTOP));
            //    WriteReg(ERXRDPTH, High(RXSTOP));
            //}
            //else
            //{
            //    Debug.WriteLine("Read Pointer set to NextPacketPointer2");
            //    WriteReg(ERXRDPTL, Low((ushort)(NextPacketPointer - 1)));
            //    WriteReg(ERXRDPTH, High((ushort)(NextPacketPointer - 1)));
            //}

            Debug.WriteLine("Packet Count is5: " + ReadEthReg(EPKTCNT).ToString());

            Debug.WriteLine("3*** ERXRDPT: " + ((ushort)(ReadEthReg(ERXRDPTL) | ReadEthReg(ERXRDPTH) << 8)).ToString());
            Debug.WriteLine("3*** ERXWRPT: " + ((ushort)(ReadEthReg(ERXWRPTL) | ReadEthReg(ERXWRPTH) << 8)).ToString());

            //WriteReg(MACON1, MACON1_TXPAUS | MACON1_RXPAUS | MACON1_MARXEN);

            BfcReg(ECON1, ECON1_RXRST);  // Clear Recieve Reset

            //WriteReg(ECON1, 0x04);
            Thread.Sleep(2);
            WritePhyReg(PHIE, 0x0012);  // Enable Interrupts

            WriteReg(EIE, EIE_PKTIE | EIE_INTIE | EIE_LINKIE | EIE_RXERIE);  // Enabled Interrupts for RX, Link, and RX Error

            BfsReg(ECON1, ECON1_RXEN);  // Enable Reception of packets

            RxResetPending = false;

            Debug.WriteLine("4*** ERXRDPT: " + ((ushort)(ReadEthReg(ERXRDPTL) | ReadEthReg(ERXRDPTH) << 8)).ToString());
            Debug.WriteLine("4*** ERXWRPT: " + ((ushort)(ReadEthReg(ERXWRPTL) | ReadEthReg(ERXWRPTH) << 8)).ToString());
            Debug.WriteLine("4*** Setting Next Packet Pointer to: " + NextPacketPointer);
        }


		byte[] ReadBuffer(int len)
		{
            lock (BankAccess)
            {
                var result = new byte[len];

                //spibuf[0] = RBM;
                spi.WriteRead(new byte[1] { RBM } , 0, 1, result, 0, len, 1);

                return result;
            }
		}

		void WriteBuffer(byte[] data, int startIndex = 0)
		{
            byte[] status = new byte[0];
            //byte[] op = { WBM, 0x00 };

            if (startIndex == 2)
            {
                // This allows us to avoid the Memory hogging Utility.CombineArrays call if we pass in an array with 2 extra bytes at the start!
                lock (BankAccess)
                {
                    data[startIndex - 2] = WBM;
                    data[startIndex - 1] = 0x00;
                    spi.WriteRead(data, status);
                }
            }
            else if (startIndex == 0)
            {
                lock (BankAccess)
                {
                    spi.WriteRead(Utility.CombineArrays(new byte[2] { WBM, 0x00 }, data), status);
                }
            }
            else
            {
                lock (BankAccess)
                {
                    spi.WriteRead(Utility.CombineArrays(new byte[2] { WBM, 0x00 }, 0, 2, data, startIndex, data.Length-startIndex), status);
                }
            }
		}

		void SendSystemReset()
		{
            lock (BankAccess)
            {
                //spibuf[0] = 0xFF;  // Opcode and Argument  SC
                spi.WriteRead(new byte[1] { 0xFF }, 0, 1, null, 0, 0, 1);
            }
			Thread.Sleep(5);

		}//end SendSystemReset

		/// <summary>
		/// Reads all Ethernet Registers (begin with E)
		/// </summary>
		/// <param name="address"></param>
		/// <returns></returns>
		byte ReadEthReg(ushort address)
		{
			lock (BankAccess)
			{
				CurrentBank = address;

				spibuf[0] = (byte)(RCR | (address & 0x1F));
				spi.WriteRead(spibuf, 0, 2, spibuf, 0, 2, 0);
				return spibuf[1];
			}
		}

		/// <summary>
		/// Reads any common register EIE, EIR, ESTAT, ECON1, ECON2
		/// </summary>
		/// <param name="address"></param>
		/// <returns></returns>
		byte ReadCommonReg(byte address)
		{
            lock (BankAccess)
            {
                spibuf[0] = (byte)(RCR | (address & 0x1F));
                spi.WriteRead(spibuf, 0, 2, spibuf, 0, 2, 0);
                return spibuf[1];
            }
		}

		/// <summary>
		/// Reads all Mac and Mii registers (Begin with M)
		/// </summary>
		/// <param name="address"></param>
		/// <returns></returns>
		byte ReadMacReg(ushort address)
		{
			lock (BankAccess)
			{
				CurrentBank = address;

				spibuf[0] = (byte)(RCR | (address & 0x1F));
				spibuf[1] = 0x00;
				spi.WriteRead(spibuf, 0, 2, spibuf, 0, 2, 1);
				return spibuf[1];
			}
		}


		private ushort ReadPhyReg(byte register)
		{
			// Set the right address and start the register read operation
			WriteReg(MIREGADR, register);
			WriteReg(MICMD, MICMD_MIIRD);

			// Loop to wait until the PHY register has been read through the MII
			// This requires 10.24us
			//CurrentBank = MISTAT >> 8;  // bank is set by writereg
			int timeout = 100;

			while ((ReadMacReg(MISTAT) & MISTAT_BUSY) != 0 && timeout-- > 0) Thread.Sleep(2);

			// Stop reading
			//CurrentBank = MIREGADR;
			WriteReg(MICMD, 0x00);

			// Obtain results and return
			ushort result = (new byte[2] { ReadMacReg(MIRDH), ReadMacReg(MIRDL) }).ToShort();

			//CurrentBank = ERDPTL;	// Return to Bank 0
			return result;
		}//end ReadPHYReg
		
		void WriteReg(ushort Address, byte data)
		{
			lock (BankAccess)
			{
				CurrentBank = Address;

				//spibuf[0] = (byte)(WCR | (Address & 0x1F));  // Opcode and Argument
				//spibuf[1] = data;
				spi.WriteRead(new byte[2] { (byte)(WCR | (Address & 0x1F)), data }, 0, 2, null, 0, 0, 2);
			}
		}//end WriteReg


		void BfcReg(byte address, byte data)
		{
            lock (BankAccess)
            {
                //spibuf[0] = (byte)(BFC | address);  // Opcode and Argument
                //spibuf[1] = data;
                spi.WriteRead(new byte[2] { (byte)(BFC | address), data }, 0, 2, null, 0, 0, 2);
            }
		}//end BFCReg

		void BfsReg(byte address, byte data)
		{
            lock (BankAccess)
            {
                //spibuf[0] = (byte)(BFS | address);  // Opcode and Argument
                //spibuf[1] = data;
                spi.WriteRead(new byte[2] { (byte)(BFS | address), data }, 0, 2, null, 0, 0, 2);
            }
		}//end BFSReg

		void WritePhyReg(byte register, ushort data)
		{
			// Write the register address
			//CurrentBank = MIREGADR;
			WriteReg(MIREGADR, register);
			WriteReg(MICMD, 0);
			// Write the data
			// Order is important: write low byte first, high byte last
			WriteReg(MIWRL, (byte)(data >> 0));
			WriteReg(MIWRH, (byte)(data >> 8));

			// Wait until the PHY register has been written
			//CurrentBank = MISTAT;
			int timeout = 100;
			while ((ReadMacReg(MISTAT) & MISTAT_BUSY) != 0 && timeout-- > 0) Thread.Sleep(1);

			//CurrentBank = ERDPTL;	// Return to Bank 0
		}

		void MACPowerDown()
		{
			// Disable packet reception
			BfcReg(ECON1, ECON1_RXEN);

			int timeout = 100;

			// Make sure any last packet which was in-progress when RXEN was cleared 
			// is completed
			while ((ReadCommonReg(ESTAT) & ESTAT_RXBUSY) != 0 && timeout-- > 0) Thread.Sleep(2);

			timeout = 100;

			// If a packet is being transmitted, wait for it to finish
			while ((ReadCommonReg(ECON1) & ECON1_TXRTS) != 0 && timeout-- > 0) Thread.Sleep(2);
	
			// Enter sleep mode
			BfsReg(ECON2, ECON2_PWRSV);
		}//end MACPowerDown

		void MACPowerUp()
		{	
			// Leave power down mode
			BfcReg(ECON2, ECON2_PWRSV);

			// Wait for the 300us Oscillator Startup Timer (OST) to time out.  This 
			// delay is required for the PHY module to return to an operational state.
			while(!((ReadCommonReg(ESTAT) & ESTAT_CLKRDY) == 0)) Thread.Sleep(100);
	
			// Enable packet reception
			BfsReg(ECON1, ECON1_RXEN);
		}//end MACPowerUp

		public bool IsLinkUp 
		{
			get
			{
				// LLSTAT is a latching low link status bit.  Therefore, if the link 
				// goes down and comes back up before a higher level stack program calls
				// MACIsLinked(), MACIsLinked() will still return FALSE.  The next 
				// call to MACIsLinked() will return TRUE (unless the link goes down 
				// again).

				return linkUpTime < TimeSpan.MaxValue;
			}
		
		}

		//void SetRXHashTableEntry(MAC_ADDR DestMACAddr)
		//{
		//    DWORD_VAL CRC = {0xFFFFFFFF};
		//    BYTE HTRegister;
		//    BYTE i, j;

		//    // Calculate a CRC-32 over the 6 byte MAC address 
		//    // using polynomial 0x4C11DB7
		//    for(i = 0; i < sizeof(MAC_ADDR); i++)
		//    {
		//        BYTE  crcnext;
	
		//        // shift in 8 bits
		//        for(j = 0; j < 8; j++)
		//        {
		//            crcnext = 0;
		//            if(((BYTE_VAL*)&(CRC.v[3]))->bits.b7)
		//                crcnext = 1;
		//            crcnext ^= (((BYTE_VAL*)&DestMACAddr.v[i])->bits.b0);
	
		//            CRC.Val <<= 1;
		//            if(crcnext)
		//                CRC.Val ^= 0x4C11DB7;
		//            // next bit
		//            DestMACAddr.v[i] >>= 1;
		//        }
		//    }
	
		//    // CRC-32 calculated, now extract bits 28:23
		//    // Bits 25:23 define where within the Hash Table byte the bit needs to be set
		//    // Bits 28:26 define which of the 8 Hash Table bytes that bits 25:23 apply to
		//    i = CRC.v[3] & 0x1F;
		//    HTRegister = (i >> 2) + (BYTE)EHT0;
		//    i = (i << 1) & 0x06;
		//    ((BYTE_VAL*)&i)->bits.b0 = ((BYTE_VAL*)&CRC.v[2])->bits.b7;
	
		//    // Set the proper bit in the Hash Table
		//    BankSel(EHT0);
		//    BFSReg(HTRegister, 1<<i);

		//    BankSel(ERDPTL);			// Return to Bank 0
		//}



		// ENC28J60 Opcodes (to be ORed with a 5 bit address)
		/// <summary>
		/// Write Control Register command
		/// </summary>
		const byte	WCR = (0x02 << 5);			
		
		/// <summary>
		/// Bit Field Set command
		/// </summary>
		const byte	BFS = (0x04 << 5);			
		
		/// <summary>
		/// Bit Field Clear command
		/// </summary>
		const byte	BFC = (0x05 << 5);			 
		
		/// <summary>
		/// Read Control Register command
		/// </summary>
		const byte	RCR = (0x00 << 5);		
		
		/// <summary>
		/// Read Buffer Memory command
		/// </summary>
		const byte	RBM = ((0x01 << 5) | 0x1A);	
		
		/// <summary>
		/// Write Buffer Memory command
		/// </summary>
		const byte	WBM = ((0x03 << 5) | 0x1A); 
		
		/// <summary>
		/// System Reset command does not use an address.
		/// </summary>
		const byte	SR  = ((0x07 << 5) | 0x1F);	// It requires 0x1F, however.


		/******************************************************************************
		* Register locations
		******************************************************************************/
		// Bank 0 registers --------
		const ushort ERDPTL		= 0x00;
		const ushort ERDPTH		= 0x01;
		const ushort EWRPTL		= 0x02;
		const ushort EWRPTH		= 0x03;
		const ushort ETXSTL		= 0x04;
		const ushort ETXSTH		= 0x05;
		const ushort ETXNDL		= 0x06;
		const ushort ETXNDH		= 0x07;
		const ushort ERXSTL		= 0x08;
		const ushort ERXSTH		= 0x09;
		const ushort ERXNDL		= 0x0A;
		const ushort ERXNDH		= 0x0B;
		const ushort ERXRDPTL	= 0x0C;
		const ushort ERXRDPTH	= 0x0D;
		const ushort ERXWRPTL	= 0x0E;
		const ushort ERXWRPTH	= 0x0F;
		const ushort EDMASTL		= 0x10;
		const ushort EDMASTH		= 0x11;
		const ushort EDMANDL		= 0x12;
		const ushort EDMANDH		= 0x13;
		const ushort EDMADSTL	= 0x14;
		const ushort EDMADSTH	= 0x15;
		const ushort EDMACSL		= 0x16;
		const ushort EDMACSH		= 0x17;
		//const ushort			= 0x18;
		//const ushort			= 0x19;
		//const ushort r			= 0x1A;
		
		// Common registers work no matter which bank is set
		const byte EIE			= 0x1B;
		const byte EIR			= 0x1C;
		const byte ESTAT		= 0x1D;
		const byte ECON2		= 0x1E;
		const byte ECON1		= 0x1F;

		// Bank 1 registers -----
		const ushort EHT0		= 0x100;
		const ushort EHT1 = 0x101;
		const ushort EHT2 = 0x102;
		const ushort EHT3 = 0x103;
		const ushort EHT4 = 0x104;
		const ushort EHT5 = 0x105;
		const ushort EHT6 = 0x106;
		const ushort EHT7 = 0x107;
		const ushort EPMM0 = 0x108;
		const ushort EPMM1 = 0x109;
		const ushort EPMM2 = 0x10A;
		const ushort EPMM3 = 0x10B;
		const ushort EPMM4 = 0x10C;
		const ushort EPMM5 = 0x10D;
		const ushort EPMM6 = 0x10E;
		const ushort EPMM7 = 0x10F;
		const ushort EPMCSL = 0x110;
		const ushort EPMCSH = 0x111;
		//const ushort			= 0x112;
		//const ushort			= 0x113;
		const ushort EPMOL		= 0x114;
		const ushort EPMOH		= 0x115;
		//const ushort r			= 0x116;
		//const ushort r			= 0x117;
		const ushort ERXFCON		= 0x118;
		const ushort EPKTCNT		= 0x119;
		//const ushort r			= 0x11A;
		//const ushort EIE		= 0x11B;
		//const ushort EIR		= 0x11C;
		//const ushort ESTAT		= 0x11D;
		//const ushort ECON2		= 0x11E;
		//const ushort ECON1		= 0x11F;

		// Bank 2 registers -----
		const ushort MACON1		= 0x200;
		const ushort MACON2		= 0x201;
		const ushort MACON3		= 0x202;
		const ushort MACON4		= 0x203;
		const ushort MABBIPG		= 0x204;
		//const ushort			= 0x205;
		const ushort MAIPGL		= 0x206;
		const ushort MAIPGH		= 0x207;
		const ushort MACLCON1	= 0x208;
		const ushort MACLCON2	= 0x209;
		const ushort MAMXFLL		= 0x20A;
		const ushort MAMXFLH		= 0x20B;
		//const ushort r			= 0x20C;
		//const ushort r			= 0x20D;
		//const ushort r			= 0x20E;
		//const ushort			= 0x20F;
		//const ushort r			= 0x210;
		//const ushort r			= 0x211;
		const ushort MICMD		= 0x212;
		//const ushort r			= 0x213;
		const ushort MIREGADR	= 0x214;
		//const ushort r			= 0x215;
		const ushort MIWRL		= 0x216;
		const ushort MIWRH		= 0x217;
		const ushort MIRDL		= 0x218;
		const ushort MIRDH		= 0x219;
		//const ushort r			= 0x21A;
		//const ushort EIE		= 0x21B;
		//const ushort EIR		= 0x21C;
		//const ushort ESTAT		= 0x21D;
		//const ushort ECON2		= 0x21E;
		//const ushort ECON1		= 0x21F;

		// Bank 3 registers -----
		const ushort MAADR5		= 0x300;
		const ushort MAADR6		= 0x301;
		const ushort MAADR3		= 0x302;
		const ushort MAADR4		= 0x303;
		const ushort MAADR1		= 0x304;
		const ushort MAADR2		= 0x305;
		const ushort EBSTSD		= 0x306;
		const ushort EBSTCON		= 0x307;
		const ushort EBSTCSL		= 0x308;
		const ushort EBSTCSH		= 0x309;
		const ushort MISTAT		= 0x30A;
		//const ushort			= 0x30B;
		//const ushort			= 0x30C;
		//const ushort			= 0x30D;
		//const ushort			= 0x30E;
		//const ushort			= 0x30F;
		//const ushort			= 0x310;
		//const ushort			= 0x311;
		const ushort EREVID		= 0x312;
		//const ushort			= 0x313;
		//const ushort			= 0x314;
		const ushort ECOCON		= 0x315;
		//const ushort 			= 0x316;
		const ushort EFLOCON		= 0x317;
		const ushort EPAUSL		= 0x318;
		const ushort EPAUSH		= 0x319;
		//const ushort r			= 0x31A;
		//const ushort EIE		= 0x31B;
		//const ushort EIR		= 0x31C;
		//const ushort ESTAT		= 0x31D;
		//const ushort ECON2		= 0x31E;
		//const ushort ECON1		= 0x31F;


		/******************************************************************************
		* PH Register Locations
		******************************************************************************/
		const byte PHCON1	= 0x00;
		const byte PHSTAT1	= 0x01;
		const byte PHID1	= 0x02;
		const byte PHID2	= 0x03;
		const byte PHCON2	= 0x10;
		const byte PHSTAT2	= 0x11;
		const byte PHIE	= 0x12;
		const byte PHIR	= 0x13;
		const byte PHLCON	= 0x14;


		/******************************************************************************
		* Individual Register Bits
		******************************************************************************/
		// ETH/MAC/MII bits

		// EIE bits ----------
		const byte	EIE_INTIE		= (1<<7);
		const byte	EIE_PKTIE		= (1<<6);
		const byte	EIE_DMAIE		= (1<<5);
		const byte	EIE_LINKIE		= (1<<4);
		const byte	EIE_TXIE		= (1<<3);
		const byte	EIE_TXERIE		= (1<<1);
		const byte	EIE_RXERIE		= (1);

		// EIR bits ----------
		const byte	EIR_PKTIF		= (1<<6);
		const byte	EIR_DMAIF		= (1<<5);
		const byte	EIR_LINKIF		= (1<<4);
		const byte	EIR_TXIF		= (1<<3);
		const byte	EIR_TXERIF		= (1<<1);
		const byte	EIR_RXERIF		= (1);
	
		// ESTAT bits ---------
		const byte	ESTAT_INT		= (1<<7);
		const byte ESTAT_BUFER		= (1<<6);
		const byte	ESTAT_LATECOL	= (1<<4);
		const byte	ESTAT_RXBUSY	= (1<<2);
		const byte	ESTAT_TXABRT	= (1<<1);
		const byte	ESTAT_CLKRDY	= (1);
	
		// ECON2 bits --------
		const byte	ECON2_AUTOINC	= (1<<7);
		const byte	ECON2_PKTDEC	= (1<<6);
		const byte	ECON2_PWRSV		= (1<<5);
		const byte	ECON2_VRPS		= (1<<3);
	
		// ECON1 bits --------
		const byte	ECON1_TXRST		= (1<<7);
		const byte	ECON1_RXRST		= (1<<6);
		const byte	ECON1_DMAST		= (1<<5);
		const byte	ECON1_CSUMEN	= (1<<4);
		const byte	ECON1_TXRTS		= (1<<3);
		const byte	ECON1_RXEN		= (1<<2);
		const byte	ECON1_BSEL1		= (1<<1);
		const byte	ECON1_BSEL0		= (1);
	
		// ERXFCON bits ------
		const byte	ERXFCON_UCEN	= (1<<7);
		const byte	ERXFCON_ANDOR	= (1<<6);
		const byte	ERXFCON_CRCEN	= (1<<5);
		const byte	ERXFCON_PMEN	= (1<<4);
		const byte	ERXFCON_MPEN	= (1<<3);
		const byte	ERXFCON_HTEN	= (1<<2);
		const byte	ERXFCON_MCEN	= (1<<1);
		const byte	ERXFCON_BCEN	= (1);
	
		// MACON1 bits --------
		const byte	MACON1_TXPAUS	= (1<<3);
		const byte	MACON1_RXPAUS	= (1<<2);
		const byte	MACON1_PASSALL	= (1<<1);
		const byte	MACON1_MARXEN	= (1);
	
		// MACON3 bits --------
		const byte	MACON3_PADCFG2	= (1<<7);
		const byte	MACON3_PADCFG1	= (1<<6);
		const byte	MACON3_PADCFG0	= (1<<5);
		const byte	MACON3_TXCRCEN	= (1<<4);
		const byte	MACON3_PHDREN	= (1<<3);
		const byte	MACON3_HFRMEN	= (1<<2);
		const byte	MACON3_FRMLNEN	= (1<<1);
		const byte	MACON3_FULDPX	= (1);
	
		// MACON4 bits --------
		const byte	MACON4_DEFER	= (1<<6);
		const byte	MACON4_BPEN		= (1<<5);
		const byte	MACON4_NOBKOFF	= (1<<4);
        const byte  MACON4_PUREPRE  = (1);
	
		// MICMD bits ---------
		const byte	MICMD_MIISCAN	= (1<<1);
		const byte	MICMD_MIIRD		= (1);

		// EBSTCON bits -----
		const byte	EBSTCON_PSV2	= (1<<7);
		const byte	EBSTCON_PSV1	= (1<<6);
		const byte	EBSTCON_PSV0	= (1<<5);
		const byte	EBSTCON_PSEL	= (1<<4);
		const byte	EBSTCON_TMSEL1	= (1<<3);
		const byte	EBSTCON_TMSEL0	= (1<<2);
		const byte	EBSTCON_TME		= (1<<1);
		const byte	EBSTCON_BISTST	= (1);

		// MISTAT bits --------
		const byte	MISTAT_NVALID	= (1<<2);
		const byte	MISTAT_SCAN		= (1<<1);
		const byte	MISTAT_BUSY		= (1);
	
		// ECOCON bits -------
		const byte	ECOCON_COCON2	= (1<<2);
		const byte	ECOCON_COCON1	= (1<<1);
		const byte	ECOCON_COCON0	= (1);
	
		// EFLOCON bits -----
		const byte	EFLOCON_FULDPXS	= (1<<2);
		const byte	EFLOCON_FCEN1	= (1<<1);
		const byte	EFLOCON_FCEN0	= (1);



		// PHY bits

		// PHCON1 bits ----------
		const ushort	PHCON1_PRST		= (1<<15);
		const ushort	PHCON1_PLOOPBK	= (1<<14);
		const ushort	PHCON1_PPWRSV	= (1<<11);
		const ushort	PHCON1_PDPXMD	= (1<<8);

		// PHSTAT1 bits --------
		const ushort	PHSTAT1_PFDPX	= (1<<12);
		const ushort	PHSTAT1_PHDPX	= (1<<11);
		const ushort	PHSTAT1_LLSTAT	= (1<<2);
		const ushort	PHSTAT1_JBSTAT	= (1<<1);

		// PHID2 bits --------
		const ushort	PHID2_PID24		= (1<<15);
		const ushort	PHID2_PID23		= (1<<14);
		const ushort	PHID2_PID22		= (1<<13);
		const ushort	PHID2_PID21		= (1<<12);
		const ushort	PHID2_PID20		= (1<<11);
		const ushort	PHID2_PID19		= (1<<10);
		const ushort	PHID2_PPN5		= (1<<9);
		const ushort	PHID2_PPN4		= (1<<8);
		const ushort	PHID2_PPN3		= (1<<7);
		const ushort	PHID2_PPN2		= (1<<6);
		const ushort	PHID2_PPN1		= (1<<5);
		const ushort	PHID2_PPN0		= (1<<4);
		const ushort	PHID2_PREV3		= (1<<3);
		const ushort	PHID2_PREV2		= (1<<2);
		const ushort	PHID2_PREV1		= (1<<1);
		const ushort	PHID2_PREV0		= (1);

		// PHCON2 bits ----------
		const ushort	PHCON2_FRCLNK	= (1<<14);
		const ushort	PHCON2_TXDIS	= (1<<13);
		const ushort	PHCON2_JABBER	= (1<<10);
		const ushort	PHCON2_HDLDIS	= (1<<8);

		// PHSTAT2 bits --------
		const ushort	PHSTAT2_TXSTAT	= (1<<13);
		const ushort	PHSTAT2_RXSTAT	= (1<<12);
		const ushort	PHSTAT2_COLSTAT	= (1<<11);
		const ushort	PHSTAT2_LSTAT	= (1<<10);
		const ushort	PHSTAT2_DPXSTAT	= (1<<9);
		const ushort	PHSTAT2_PLRITY	= (1<<5);

		// PHIE bits -----------
		const ushort	PHIE_PLNKIE		= (1<<4);
		const ushort	PHIE_PGEIE		= (1<<1);

		// PHIR bits -----------
		const ushort	PHIR_PLNKIF		= (1<<4);
		const ushort	PHIR_PGIF		= (1<<2);

		// PHLCON bits -------
		const ushort	PHLCON_LACFG3	= (1<<11);
		const ushort	PHLCON_LACFG2	= (1<<10);
		const ushort	PHLCON_LACFG1	= (1<<9);
		const ushort	PHLCON_LACFG0	= (1<<8);
		const ushort	PHLCON_LBCFG3	= (1<<7);
		const ushort	PHLCON_LBCFG2	= (1<<6);
		const ushort	PHLCON_LBCFG1	= (1<<5);
		const ushort	PHLCON_LBCFG0	= (1<<4);
		const ushort	PHLCON_LFRQ1	= (1<<3);
		const ushort	PHLCON_LFRQ0	= (1<<2);
		const ushort	PHLCON_STRCH	= (1<<1);

		const ushort MaxFrameSize = 1518; // 1518 is the IEEE 802.3 specified limit


	}
}

