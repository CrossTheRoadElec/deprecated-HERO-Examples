using System;
using System.Text;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using CTRE.HERO;

namespace CTRE
{
    namespace HERO
    {
        namespace Module
        {
            public class ESP12F : ModuleBase
            {
                public readonly char kModulePortType = 'U';

                private PortDefinition port;
                public System.IO.Ports.SerialPort uart;
                private int status;

                int baud = 115200;
                string gateway = "192.168.4.1";
                String SSID;

                public enum SecurityType { OPEN, WPA_PSK = 2, WPA2_PSK, WPA_WPA2_PSK }
                public enum wifiMode { STATION = 1, SOFTAP, SOFTAP_STATION }


                //UART Return Value Processor
                const int READY = 0;
                const int HEADER = 10;
                const int LINKID = 20;
                const int SIZE = 30;
                const int DATA = 40;

                const int STAGE1 = 1;
                const int STAGE2 = 2;
                const int STAGE3 = 3;
                const int STAGE4 = 4;

                int processState;
                int headerState;
                int linkIDState;

                int processedLinkID;
                int processedSize;

                int dataCount;
                byte[] dataCache = new byte[0];
                static byte[] _rx = new byte[1024];

                WiFiSerialLexer lex = new WiFiSerialLexer();

                InputPort resetPin;
                InputPort GPIO;
                bool modulePresent = true;

                public ESP12F(PortDefinition port)
                {
                    if (Contains(port.types, kModulePortType))
                    {
                        status = StatusCodes.OK;
                        this.port = port;

                        processState = READY;
                        headerState = STAGE1;
                        linkIDState = STAGE1;
                        processedSize = 0;
                        dataCount = 0;
                        InitUart((IPortUart)(this.port));
                        InitPresenceCheck(this.port);
                    }
                    else
                    {
                        status = StatusCodes.PORT_MODULE_TYPE_MISMATCH;
                        //Reporting.SetError(status);
                    }
                }

                private void InitUart(IPortUart port)
                {
                    uart = new System.IO.Ports.SerialPort(port.UART, baud, System.IO.Ports.Parity.None, 8, System.IO.Ports.StopBits.One);
                    uart.Open();

                }

                public void InitPresenceCheck(PortDefinition port)
                {
                    switch (port.id)
                    {
                        case 1:
                            {
                                resetPin = new InputPort(IO.Port1.Pin6, false, Port.ResistorMode.PullDown);
                                GPIO = new InputPort(IO.Port1.Pin3, false, Port.ResistorMode.PullDown);
                                modulePresent = resetPin.Read();
                                break;
                            }
                        case 4:
                            {
                                resetPin = new InputPort(IO.Port4.Pin6, false, Port.ResistorMode.PullDown);
                                GPIO = new InputPort(IO.Port4.Pin3, false, Port.ResistorMode.PullDown);
                                modulePresent = resetPin.Read();
                                break;
                            }
                        case 6:
                            {
                                resetPin = new InputPort(IO.Port6.Pin6, false, Port.ResistorMode.PullDown);
                                GPIO = new InputPort(IO.Port6.Pin3, false, Port.ResistorMode.PullDown);
                                modulePresent = resetPin.Read();
                                break;
                            }
                        default: break;
                    }
                }
                public bool CheckPresence()
                {
                    bool temp1 = resetPin.Read();
                    bool temp2 = GPIO.Read();
                    Debug.Print("Reset Pin: " + temp1 + "   | GPIO0: " + temp2 + "\r\n");
                    modulePresent = temp1 && temp2;
                    return modulePresent;
                }

                public ESP12F(System.IO.Ports.SerialPort _uart)
                {
                    processState = READY;
                    headerState = STAGE1;
                    linkIDState = STAGE1;
                    processedSize = 0;
                    dataCount = 0;

                    uart = _uart;
                }

                public int test(int timeoutMs = 100)
                {
                    byte[] toSend = MakeByteArrayFromString("AT" + "\r\n");
                    uart.Write(toSend, 0, toSend.Length);
                    System.Threading.Thread.Sleep(5);
                    //int byteCount = toSend.Length + 7;
                    int byteCount = 1;
                    byte[] rx = new byte[byteCount];
                    long start = DateTime.Now.Ticks;
                    long now;
                    while (lex.isDone == false)
                    {
                        System.Threading.Thread.Sleep(1);
                        if (uart.BytesToRead > 0)
                        {
                            start = DateTime.Now.Ticks;
                            int readCnt = uart.Read(rx, 0, byteCount);
                            lex.process(rx);
                        }
                        else
                        {
                            CheckPresence();
                            now = DateTime.Now.Ticks;
                            if (now - start > (timeoutMs) * TimeSpan.TicksPerMillisecond || !modulePresent)
                            {
                                lex.isDone = true;
                            }
                        }
                    }
                    lex.clearDoneFlag();

                    if (lex.lines.Contains("OK"))
                    {
                        lex.clearLines();
                        return 0;
                    }
                    return -1;
                }

                public string getVersion(int timeoutMs = 100)
                {
                    byte[] toSend = MakeByteArrayFromString("AT+GMR\r\n");
                    uart.Write(toSend, 0, toSend.Length);
                    int byteCount = 1;
                    byte[] rx = new byte[byteCount];
                    long start = DateTime.Now.Ticks;
                    long now;
                    while (lex.isDone == false)
                    {
                        System.Threading.Thread.Sleep(1);
                        if (uart.BytesToRead > 0)
                        {
                            start = DateTime.Now.Ticks;
                            int readCnt = uart.Read(rx, 0, byteCount);
                            lex.process(rx);
                        }
                        else
                        {
                            CheckPresence();
                            now = DateTime.Now.Ticks;
                            if (now - start > (timeoutMs) * TimeSpan.TicksPerMillisecond || !modulePresent)
                            {
                                lex.isDone = true;
                            }
                        }
                    }
                    lex.clearDoneFlag();

                    string temp = "";

                    foreach (String x in lex.lines)
                    {
                        if(x.Length >= 11 && x.Substring(0,11) == "SDK version")
                        {
                            temp = x.ToString();
                        }
                    }
                    lex.clearLines();

                    return temp;
                }

                public int setWifiMode(wifiMode mode, int timeoutMs = 50)
                {
                    byte[] toSend = MakeByteArrayFromString("AT+CWMODE_CUR=" + mode + "\r\n");
                    uart.Write(toSend, 0, toSend.Length);

                    int byteCount = 1;
                    byte[] rx = new byte[byteCount];
                    long start = DateTime.Now.Ticks;
                    long now;
                    while (lex.isDone == false)
                    {
                        System.Threading.Thread.Sleep(1);
                        if (uart.BytesToRead > 0)
                        {
                            start = DateTime.Now.Ticks;
                            int readCnt = uart.Read(rx, 0, byteCount);
                            lex.process(rx);
                        }
                        else
                        {
                            CheckPresence();
                            now = DateTime.Now.Ticks;
                            if (now - start > (timeoutMs) * TimeSpan.TicksPerMillisecond || !modulePresent)
                            {
                                lex.isDone = true;
                            }
                        }
                    }
                    lex.clearDoneFlag();

                    if (lex.lines.Contains("OK"))
                    {
                        lex.clearLines();
                        return 0;
                    }
                    return -1;
                }

                public void setAP(String _SSID, String password, int channel, SecurityType encryption)
                {
                    SSID = _SSID;
                    byte[] toSend = MakeByteArrayFromString("AT+CWSAP_CUR=\"" + _SSID + "\",\"" + password + "\"," + channel + "," + encryption + "\r\n");
                    uart.Write(toSend, 0, toSend.Length);
                }

                public void sendUDP(int id, byte[] data)
                {
                    byte[] cipsend = MakeByteArrayFromString("AT+CIPSEND=" + id + "," + data.Length + "\r\n");
                    uart.Write(cipsend, 0, cipsend.Length);
                    System.Threading.Thread.Sleep(5);
                    uart.Write(data, 0, data.Length);
                }

                public void startUDP(int id, string remoteIP, int remotePort, int localPort)
                {
                    byte[] cipmux = MakeByteArrayFromString("AT+CIPMUX=1" + "\r\n");
                    uart.Write(cipmux, 0, cipmux.Length);
                    byte[] cipstart = MakeByteArrayFromString("AT+CIPSTART=" + id + ",\"UDP\",\"" + remoteIP + "\"," + remotePort + "," + localPort + ",0" + "\r\n");
                    uart.Write(cipstart, 0, cipstart.Length);
                }

                public void stopUDP(int id)
                {
                    byte[] cipclose = MakeByteArrayFromString("AT+CIPCLOSE=" + id + "\r\n");
                    uart.Write(cipclose, 0, cipclose.Length);
                }

                //Used to connect to a TCP server.
                public void startTCP(int id, string remoteIP, int remotePort)
                {
                    byte[] cipmux = MakeByteArrayFromString("AT+CIPMUX=1" + "\r\n");
                    uart.Write(cipmux, 0, cipmux.Length);
                    byte[] cipstart = MakeByteArrayFromString("AT+CIPSTART=" + id + ",\"TCP\",\"" + remoteIP + "\"," + remotePort + "\r\n");
                    uart.Write(cipstart, 0, cipstart.Length);
                }

                public void sendTCP(int id, byte[] data)
                {
                    sendUDP(id, data);
                }

                public void closeTCP(int id)
                {
                    stopUDP(id);
                }

                //TCP Server used to manage connections being made to ESP12F.
                //  The ESP1F will assign a channel ID when the connection is made (0 indexed with maximum 5 connections).
                public void openTCPServer(int localPort) //port is specified
                {
                    byte[] cipmux = MakeByteArrayFromString("AT+CIPMUX=1" + "\r\n");
                    uart.Write(cipmux, 0, cipmux.Length);
                    byte[] cipstart = MakeByteArrayFromString("AT+CIPSERVER=1," + localPort + "\r\n");
                    uart.Write(cipstart, 0, cipstart.Length);
                }

                public void openTCPServer() //overload for non-specified port (default is 333)
                {
                    byte[] cipmux = MakeByteArrayFromString("AT+CIPMUX=1" + "\r\n");
                    uart.Write(cipmux, 0, cipmux.Length);
                    byte[] cipstart = MakeByteArrayFromString("AT+CIPSERVER=1\r\n");
                    uart.Write(cipstart, 0, cipstart.Length);
                }

                public int connect(string _ssid, string password, int timeoutMs = 10000)
                {
                    byte[] toSend = MakeByteArrayFromString("AT+CWJAP_CUR=" + "\"" + _ssid + "\",\"" + password + "\"\r\n");
                    uart.Write(toSend, 0, toSend.Length);
                    //int byteCount = toSend.Length + 36;
                    int byteCount = 1;
                    byte[] rx = new byte[byteCount];
                    //System.Threading.Thread.Sleep(5000);
                    //int readCnt = uart.Read(rx, 0, byteCount);
                    long start = DateTime.Now.Ticks;
                    long now;
                    while (lex.isDone == false)
                    {
                        System.Threading.Thread.Sleep(1);
                        if(uart.BytesToRead > 0)
                        {
                            start = DateTime.Now.Ticks;
                            int readCnt = uart.Read(rx, 0, byteCount);
                            lex.process(rx);
                        }
                        else
                        {
                            now = DateTime.Now.Ticks;
                            CheckPresence();
                            if(now - start > (timeoutMs) * TimeSpan.TicksPerMillisecond || !modulePresent)
                            {
                                lex.isDone = true;
                            }
                        }
                    }
                    lex.clearDoneFlag();

                    if (lex.lines.Contains("WIFI CONNECTED") && lex.lines.Contains("OK"))
                    {
                        lex.clearLines();
                        return 0;
                    }

                    return -1;
                }

                public int PingCheck(string ip, int timeoutMs = 1000)
                {
                    byte[] toSend = MakeByteArrayFromString("AT+PING=\"" + ip + "\"\r\n");
                    uart.Write(toSend, 0, toSend.Length);
                    int byteCount = 1;
                    byte[] rx = new byte[byteCount];
                    long start = DateTime.Now.Ticks;
                    long now;
                    while (lex.isDone == false)
                    {
                        System.Threading.Thread.Sleep(1);
                        if (uart.BytesToRead > 0)
                        {
                            start = DateTime.Now.Ticks;
                            int readCnt = uart.Read(rx, 0, byteCount);
                            lex.process(rx);
                        }
                        else
                        {
                            now = DateTime.Now.Ticks;
                            CheckPresence();
                            if (now - start > (timeoutMs) * TimeSpan.TicksPerMillisecond || !modulePresent)
                            {
                                lex.isDone = true;
                            }
                        }
                    }
                    lex.clearDoneFlag();

                    if (lex.lines.Contains("OK"))
                    {
                        lex.clearLines();
                        return 0;
                    }

                    return -1;
                }


                public void disconnect()
                {
                    byte[] toSend = MakeByteArrayFromString("AT+CWQAP\r\n");
                    uart.Write(toSend, 0, toSend.Length);
                }

                public void reset()
                {
                    byte[] reset = MakeByteArrayFromString("AT+RST" + "\r\n");
                    uart.Write(reset, 0, reset.Length);
                }

                public void FactoryReset()
                {
                    byte[] reset = MakeByteArrayFromString("AT+RESTORE\r\n");
                    uart.Write(reset, 0, reset.Length);
                }

                public bool setCommRate(int baudRate)
                {
                    string cur = "AT+UART_CUR=" + baudRate + ",8,1,0,3\r\n";
                    byte[] uart_cur = MakeByteArrayFromString(cur);
                    Debug.Print(cur);
                    uart.Write(uart_cur, 0, uart_cur.Length);
                    System.Threading.Thread.Sleep(10);

                    baud = baudRate;
                    uart.Close();
                    uart.BaudRate = baud;
                    uart.DiscardInBuffer();
                    uart.Open();

                    return true;
                }

                private byte[] MakeByteArrayFromString(String msg)
                {
                    byte[] retval = new byte[msg.Length];
                    for (int i = 0; i < msg.Length; ++i)
                        retval[i] = (byte)msg[i];
                    return retval;
                }
                public byte[] getDataCache()
                {
                    return dataCache;
                }

                public int transferDataCache(byte[] outsideCache)
                {
                    if (outsideCache.Length >= dataCache.Length)
                    {
                        for (int i = 0; i < dataCache.Length; i++)
                        {
                            outsideCache[i] = dataCache[i];
                        }

                        return dataCache.Length;
                    }
                    else { return -1; }
                }

				public bool processInput()
				{
                    if (uart.BytesToRead > 0)
                    {
                        int readCnt = uart.Read(_rx, 0, uart.BytesToRead);
                        for (int i = 0; i < readCnt; ++i)
                        {
                            if (processByte(_rx[i]))
                            {
                                return true;
                            }
                        }
                    }
                    return false;
                }
				
                public bool processByte(byte data)
                {
                    switch (processState)
                    {
                        case READY:
                            {
                                if (data == '+') { processState = HEADER; }
                                break;
                            }
                        case HEADER:
                            {
                                switch (headerState)
                                {
                                    case STAGE1:
                                        {
                                            if (data == 'I') { headerState = STAGE2; }
                                            else { headerState = STAGE1; processState = READY; }
                                            break;
                                        }
                                    case STAGE2:
                                        {
                                            if (data == 'P') { headerState = STAGE3; }
                                            else { headerState = STAGE1; processState = READY; }
                                            break;
                                        }
                                    case STAGE3:
                                        {
                                            if (data == 'D') { headerState = STAGE4; }
                                            else { headerState = STAGE1; processState = READY; }
                                            break;
                                        }
                                    case STAGE4:
                                        {
                                            if (data == ',') { headerState = STAGE1; processState = LINKID; }
                                            else { headerState = STAGE1; processState = READY; }
                                            break;
                                        }
                                }

                                break;
                            }
                        case LINKID:
                            {
                                switch (linkIDState)
                                {
                                    case STAGE1:
                                        {
                                            processedLinkID = data - '0'; //being directly from the chip it's ascii encoded
                                            linkIDState = STAGE2;
                                            break;
                                        }
                                    case STAGE2:
                                        {
                                            if (data == ',') { linkIDState = STAGE1; processState = SIZE; }
                                            else { linkIDState = STAGE1; processState = READY; }
                                            break;
                                        }
                                }
                                break;
                            }
                        case SIZE:
                            {
                                if (data == ':') { processState = DATA; }
                                else
                                {
                                    int digit = data - '0';
                                    processedSize = (processedSize * 10) + digit;
                                }
                                break;
                            }
                        case DATA:
                            {
                                //Debug.Print("DataCount: " + dataCount + "  Data: " + data);
                                if (dataCount == 0) { dataCache = null; dataCache = new byte[processedSize]; }

                                if (dataCount < processedSize)
                                {
                                    dataCache[dataCount] = data;
                                    dataCount++;
                                }

                                if (dataCount == processedSize)
                                {
                                    processState = READY;
                                    dataCount = 0;
                                    processedSize = 0;

                                    return true;
                                }

                                break;
                            }
                        default: { break; }
                    }

                    return false;
                }

                private bool Contains(char[] array, char item)
                {
                    bool found = false;

                    foreach (char element in array)
                    {
                        if (element == item)
                            found = true;
                    }

                    return found;
                }

                private class WiFiSerialLexer
                {
                    public System.Collections.ArrayList lines;
                    StringBuilder temp;
                    public bool isDone = false;

                    public WiFiSerialLexer()
                    {
                        lines = new System.Collections.ArrayList();
                        temp = new StringBuilder();
                    }

                    public void process(string input)
                    {
                        for (int i=0; i< input.Length; i++)
                        {
                            process(input[i]);
                        }
                    }

                    public void process(byte[] input)
                    {
                        for (int i = 0; i < input.Length; i++)
                        {
                            process((char)input[i]);
                        }
                    }

                    public void process(char input)
                    {
                        if (input == '\n')
                        {
                            if (temp.ToString() == "OK" || temp.ToString() == "FAIL" || temp.ToString() == "ERROR" || temp.ToString() == "ready")
                            {
                                isDone = true;
                            }

                            if (temp.ToString() != "\n" && temp.ToString() != "\r" && temp.ToString() != null)
                            {
                                lines.Add(temp.ToString());
                                Debug.Print(temp.ToString());
                            }

                            temp.Clear();
                        }
                        else
                        {
                            if(input != '\r')
                            {
                                temp.Append(input);
                            }
                        }
                    }

                    public void clearDoneFlag()
                    {
                        isDone = false;
                    }
                    public void clearLines()
                    {
                        lines.Clear();
                    }
                }
            }
        }
    }
}
