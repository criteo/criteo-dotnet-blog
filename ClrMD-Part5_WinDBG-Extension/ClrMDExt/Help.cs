using System;
using System.Runtime.InteropServices;
using RGiesecke.DllExport;

namespace ClrMDExt
{
    public partial class DebuggerExtensions
    {
        [DllExport("Help")]
        public static void Help(IntPtr client, [MarshalAs(UnmanagedType.LPStr)] string args)
        {
            OnHelp(client, args);
        }

        [DllExport("help")]
        public static void help(IntPtr client, [MarshalAs(UnmanagedType.LPStr)] string args)
        {
            OnHelp(client, args);
        }

        const string _help =
        "-------------------------------------------------------------------------------\r\n"+
        "CRITEO is a debugger extension DLL designed to dig into CLR data structures.\r\n" +
        "Functions are listed by category and shortcut names are listed in parenthesis.\r\n" +
        "Type \"!help <functionname>\" for detailed info on that function.\r\n"+
        "\r\n"+
        "Thread Pool                       Timers\r\n"+
        "-----------------------------     -----------------------------\r\n"+
        "TpQueue(tpq)                      TimerInfo (ti)\r\n"+
        "TpRunning(tpr)\r\n"+
        "\r\n"+
        "Tasks                             Strings\r\n"+
        "-----------------------------     -----------------------------\r\n"+
        "TkState (tks)                     StringDuplicates (sd)\r\n";

        const string _tksHelp =
        "-------------------------------------------------------------------------------\r\n" +
        "!TkState [hexa address]\r\n" +
        "         [decimal state value]\r\n" +
        "\r\n" +
        "!TkState translates a Task m_stateFlags field value into text.\r\n" +
        "It supports direct decimal value or hexacimal address correspondig to a task instance.\r\n" +
        "\r\n" +
        "0:000> !tkstate 000001db16cf98f0\r\n" +
        "Task state = Running\r\n" +
        "0:000> !tkstate 204800\r\n" +
        "Task state = Running\r\n";

        private static void OnHelp(IntPtr client, string args)
        {
            // Must be the first thing in our extension.
            if (!InitApi(client))
                return;

            string command = args;
            if (args != null)
                command = args.ToLower();

            switch (command)
            {
                case "tks":
                case "tkstate":
                    Console.WriteLine(_tksHelp);
                    break;

                default:
                    Console.WriteLine(_help);
                    break;
            }
        }
    }
}
