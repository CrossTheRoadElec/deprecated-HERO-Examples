// This file is part of mIP - the Managed TCP/IP Stack.
// Hosted on Codeplex: http://mip.codeplex.com
// mIP is free software licensed under the Apache License 2.0
// © Copyright 2012 ValkyrieTech, LLC

using System;
using System.Text;

namespace System.Web
{
    public static class HttpUtility
    {
        /// <summary>
        /// Decodes the URL query string into string
        /// </summary>
        /// <param name="encodedString">URL Encoded string, you know with the %20 instead of spaces and stuff like that</param>
        /// <returns>The plain string</returns>
        public static string UrlDecode(string encodedString, bool replacePlus = true)
        {
            string outStr = string.Empty;

            int i = 0;
            while (i < encodedString.Length)
            {
                switch (encodedString[i])
                {
                    case '+': outStr += (replacePlus ? ' ' : encodedString[i]); break;
                    case '%':
                        outStr += Convert.ToChar((ushort)((HexToInt(encodedString[i+1]) * 16) + HexToInt(encodedString[i+2])));
                        i += 2;
                        break;
                    default:
                        outStr += encodedString[i];
                        break;
                }
                i++;
            }
            return outStr;
        }

        private static int HexToInt(char ch)
        {
            return
                (ch >= '0' && ch <= '9') ? ch - '0' :
                (ch >= 'a' && ch <= 'f') ? ch - 'a' + 10 :
                (ch >= 'A' && ch <= 'F') ? ch - 'A' + 10 :
                -1;
        }

        /// <summary>
        /// Encodes the URL query string into string
        /// </summary>
        /// <param name="urlString">Plain URL string.  Replaces spaces with stuff like %20</param>
        /// <returns>The URL encoded string</returns>
        public static string UrlEncode(string plainString, bool encodePeriod = true)
        {
            string outStr = string.Empty;

            int i = 0;
            while (i < plainString.Length)
            {
                var charCode = (int)plainString[i];

                if (charCode == 32)
                    outStr += "+";
                else if (charCode == 46 && encodePeriod == false)
                    outStr += ".";
                else if ((charCode >= 65 && charCode <= 90) || (charCode >= 97 && charCode <= 122) || (charCode >= 48 && charCode <= 57))  // letters and numbers
                    outStr += plainString[i];
                else if (charCode == 36 || charCode == 40 || charCode == 41 || charCode == 47 || charCode == 92)
                    outStr += plainString[i];
                else
                    outStr += "%" + charCode.ToString("X");
                
                i++;
            }

            return outStr;
        }

    }
}