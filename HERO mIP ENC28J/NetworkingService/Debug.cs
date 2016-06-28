using System;
using Microsoft.SPOT;

namespace System.Diagnostics
{
    internal class Debug
    {
        // Note: For this to work, you must add "TINYCLR_TRACE" to the Conditional Compilation Symbols in the Project Properties

        [Conditional("TINYCLR_TRACE")]
        internal static void WriteLine(string text)
        {
            Microsoft.SPOT.Trace.Print(text);
        }
    }
}

