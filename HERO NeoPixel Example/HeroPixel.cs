using Microsoft.SPOT.Hardware;

/// <summary>
/// Updates a series of NeoPixels.
/// Call setColor (or setStripColor) to update individual pixels colors (or all at once).
/// Call writeOutput to flush latest LED values.  
/// </summary>
public class HeroPixel
{
    // const values for inflated bits
    private const byte T0 = 0xC0; // 1100 0000 (9MHz SPI transmission translates to a 0 bit)
    private const byte T1 = 0xFe; // 1111 1110 (9MHz SPI transmission translates to a 1 bit)
    private const byte START_OF_RST = 0x00; // 0000 0000 (used to add rest between transmissions)

    // const values for common colors
    public const uint WHITE = 0xFFFFFF;
    public const uint OFF = 0x000000;
    public const uint RED = 0xFF0000;
    public const uint GREEN = 0x00FF00;
    public const uint BLUE = 0x0000FF;
    public const uint CYAN = 0x00FFFF;
    public const uint MAGENTA = 0xFF00FF;
    public const uint YELLOW = 0xFFFF00;
    public const uint PURPLE = 0x800080;
    public const uint ORANGE = 0xFF3a00;
    public const uint PINK = 0xFF6065;

    /// <summary>
    /// Each color requires 8 bits, which translates to 8 SPI-byte-writes.
    /// </summary>
    private const uint kSpiBytesPerColor = 8; 
    private const uint kColorsPerPixel = 3;

    // used for SPI communication
    SPI SPIDevice;
    SPI.Configuration Configuration;

    // instance variables
    uint[] _pixels;         // RGB values of each pixel
    byte[] _spiOut;
    uint _numPixels;         // strip's length

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="color"> RGB value to initially set all LEDs to.  Pass OFF or 0 to start with LEDs off. </param>
    /// <param name="numPixels"> Number of pixels in strip.</param>
    /// <param name="spiModule"> Optional paramter for specifying which SPI module.  Omit this paramter to default to HERO S ports. </param>
    public HeroPixel(uint color, uint numPixels = 1, SPI.SPI_module spiModule = SPI.SPI_module.SPI4) 
    {
        _numPixels = (numPixels > 0) ? numPixels : 1;

        _pixels = new uint[_numPixels];
        _spiOut = new byte[kColorsPerPixel * kSpiBytesPerColor * _numPixels + 1]; // create array for output with +1 extra for START_OF_RST at the end

        setStripColor(color);

        // initialize SPI
        Configuration = new SPI.Configuration(Cpu.Pin.GPIO_NONE, false, 0, 0, false, false, 9000, spiModule);
        SPIDevice = new SPI(Configuration);
    }
    /// <summary>
    /// sets color values for pixels strip of size length beginning at start
    /// </summary>
    /// <param name="color"> RGB value to set in selected pixels</param>
    /// <param name="start"> Index of the first pixel to update. </param>
    /// <param name="length"> Number of pixels to change. </param>
    public void setColor(uint color, uint start, uint length)
    {
        for (uint i = start; i < (start + length); i++)
        {
            /* only update the SPI bytes if pixel has changed */
            if (_pixels[i] != color)
            {
                _pixels[i] = color;

                /* update spi map */
                uint counter = kColorsPerPixel * kSpiBytesPerColor * i; // keeps track of position in array
                byte grn = (byte)(color >> 8);
                byte red = (byte)(color >> 16);
                byte blu = (byte)(color >> 0);
                // inflate every bit in green byte and add to output array, starting with most significant
                for (byte j = 0x80; j > 0; j >>= 1)
                {
                    if ((grn & j) != 0) { _spiOut[counter++] = T1; }
                    else { _spiOut[counter++] = T0; }
                }

                // inflate every bit in red byte and add to output array, starting with most significant
                for (byte j = 0x80; j > 0; j >>= 1)
                {
                    if ((red & j) != 0) { _spiOut[counter++] = T1; }
                    else { _spiOut[counter++] = T0; }
                }

                // inflate every bit in blue byte and add to output array, starting with most significant
                for (byte j = 0x80; j > 0; j >>= 1)
                {
                    if ((blu & j) != 0) { _spiOut[counter++] = T1; }
                    else { _spiOut[counter++] = T0; }
                }
            }
        }
    }
    /// <summary>
    /// sets color of strip 
    /// </summary>
    /// <param name="color"></param>
    public void setStripColor(uint color)
    {
        setColor(color, 0, _numPixels);
    }
    /// <summary>
    ///  Outputs the data over 9 MHz SPI transmission
    /// </summary>
    public void writeOutput()
    {
        SPIDevice.Write(_spiOut);
    }
    /// <summary>
    /// Number of pixels this class was constructed with.
    /// </summary>
    public uint NumberPixels
    {
        get
        {
            return _numPixels;
        }
    }
}
