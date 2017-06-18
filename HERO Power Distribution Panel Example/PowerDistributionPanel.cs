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
using System;
using Microsoft.SPOT;

namespace CTRE
{
    /**
     * Class object representing a CTRE Power Distribution Panel on CAN Bus.
     * Construct an instance with the appropriate Device ID.
     */
    public class PowerDistributionPanel : CANBusDevice
    {
        private int _lastError = 0;

        private UInt64 _cache;
        private UInt32 _len;

        private Int16[] _cache_words = new Int16[6];

        private const float kCurrentScalar = 0.125f;

        /** CAN frame defines */
        private const UInt32 STATUS_1 = 0x08041400; //Channels 0-5
        private const UInt32 STATUS_2 = 0x08041440; //Channels 6-11
        private const UInt32 STATUS_3 = 0x08041480; //Channels 12-15

        /**
         * Create a PDP object that communicates on CAN Bus.
         * @param deviceNumber [0,62] Device ID of PDP.
         */
        public PowerDistributionPanel(uint deviceNumber) : base(deviceNumber)
        {
        }

        /* -------------- Basic CAN Frame Decoders -------------- */
        private int ReceiveCAN(UInt32 arbId)
        {
            int errCode = CTRE.Native.CAN.Receive(arbId | GetDeviceNumber(), ref _cache, ref _len);
            return errCode;
        }

        private int GetSixParam10(UInt32 arbId)
        {
            int errCode = ReceiveCAN(arbId);

            _cache_words[0] = (Int16)((byte)(_cache));
            _cache_words[0] <<= 2;
            _cache_words[0] |= (Int16)((_cache >> 14) & 0x03);

            _cache_words[1] = (Int16)((_cache >> 8) & 0x3F);
            _cache_words[1] <<= 4;
            _cache_words[1] |= (Int16)((_cache >> 20) & 0x0F);

            _cache_words[2] = (Int16)((_cache >> 16) & 0x0F);
            _cache_words[2] <<= 6;
            _cache_words[2] |= (Int16)((_cache >> 26) & 0x3F);

            _cache_words[3] = (Int16)((_cache >> 24) & 0x03);
            _cache_words[3] <<= 8;
            _cache_words[3] |= (Int16)((byte)(_cache >> 32));

            _cache_words[4] = (Int16)((byte)(_cache >> 40));
            _cache_words[4] <<= 2;
            _cache_words[4] |= (Int16)((_cache >> 54) & 0x03);

            _cache_words[5] = (Int16)((_cache >> 48) & 0x3F);
            _cache_words[5] <<= 4;
            _cache_words[5] |= (Int16)((_cache >> 60) & 0x0F);
          
            return errCode;
        }

        /**
         * Get current for a given channel in amperes. 
         * @param channelId [0,15] channel to retrieve current.
         * @return current in amperes.
         * @see GetLastError to retrieve error information.
         */
        public float GetChannelCurrent(int channelId)
        {
            float retval = 0;
            int errCode = StatusCodes.CAN_INVALID_PARAM;

            if (channelId >= 0 && channelId <= 5)
            {
                errCode = GetSixParam10(STATUS_1);
                retval = (_cache_words[channelId] * kCurrentScalar);
            }
            else if (channelId >= 6 && channelId <= 11)
            {
                errCode = GetSixParam10(STATUS_2);
                retval = (_cache_words[channelId - 6] * kCurrentScalar);
            }
            else if (channelId >= 12 && channelId <= 15)
            {
                errCode = GetSixParam10(STATUS_3);
                retval = (_cache_words[channelId - 12] * kCurrentScalar);
            }

            HandleError(errCode);
            return retval;
        }
        public float GetVoltage()
        {
            /* get latest status 3 frame */
            int errCode = ReceiveCAN(STATUS_3);
            /* grab the vbat byte */
            byte vbatByte = (byte)(_cache >> 48); /* byte 6 */
            /* scale it to volts */
            float retval = 0.05f * vbatByte + 4.0f;
            /* error handle and return voltage to caller */
            HandleError(errCode);
            return retval;
        }
        /**
         * @return error code from last API call.
         */
        public int GetLastError()
        {
            return _lastError;
        }

        private int HandleError(int errorCode)
        {
            /* error handler */
            if (errorCode != 0)
            {
                /* This requires being in the main CTRE library package to function (due to access protection).
                For now, print error no. to debug.
                Reporting.SetError should be made public. - Ozrien*/
                //Reporting.SetError(errorCode, Reporting.getHALErrorMessage(errorCode));
                Debug.Print("PDP Error: " + errorCode + "\r\n");
            }
            /* mirror last status */
            _lastError = errorCode;
            return _lastError;
        }
    }
}
