# What is HERO?
The HERO is a Gadgeteer main board that features the .NET Micro Framework. This powerful development platform allows users to program and debug using Visual Studio 2015 C#. This open hardware platform features 8 Gadgeteer ports that may be connected to a variety of Gadgeteer modules supporting SPI, I2C, UART/USART, SD Card, Analog, GPIO, PWM and a Talon SRX emulation port. The HERO also features dual wire CAN, USB host and USB device.

# Is HERO a robot controller?
Yes, HERO typically is used as price-competitive robot controller.  The wireless gamepad support, and native support of CTRE CAN actuators also expedient development for robot applications.

# Is HERO a development board?
Yes, the development board is NETMF (C#) based, and supports a variety of IO options for display, sensors, physical interface, etc...

# Common uses for HERO?
- Developing robot platforms (land-based vehicles, robot arm, process lines) without a major price investment.
- Inexpensive replacement for control systems on existing robots should original control system need to be repurposed or replaced.
- Open source API for learning about CAN bus and how to integrate CTRE CAN devices into custom applications.

# HERO-Examples
Visual Studio NETMF examples supporting the HERO Development Board
These examples will be periodically merged into CTRE's HERO-SDK-Installer, in which case they will appear in Visual Studio's New Project Dialog Box.

# BUILD TOOLS
	-Install Visual Studio 2015.
	-Install and run the latest HERO-SDK-Installer.
	-Run the NetmfVS14.vsix file located in the install path of the SDK.
 
 Full instructions for software install can be found in Section 6 of the HERO User's Guide.
 
 HERO User's Guide and SDK-Installer can be found at...
 http://www.ctr-electronics.com/hro.html#product_tabs_technical_resources
 
# HERO-mIP-ENC28J
This is a HERO port of the TCP/IP networking examples utilizing mIP, an open-source managed TCP/IP stack written in C#.
This was tested using an ENC28J breakout on Port 1.
Original code: http://mip.codeplex.com/