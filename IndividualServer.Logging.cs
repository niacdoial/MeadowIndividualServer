using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace RainMeadow
{

    public class InvalidProgrammerException : InvalidOperationException
    {
        public InvalidProgrammerException(string message) : base(message + " you goof") { }
    }

    public partial class RainMeadow
    {
        private static string TrimCaller(string callerFile) { return (callerFile = callerFile.Substring(Math.Max(callerFile.LastIndexOf(Path.DirectorySeparatorChar), callerFile.LastIndexOf(Path.AltDirectorySeparatorChar)) + 1)).Substring(0, callerFile.LastIndexOf('.')); }
        private static string LogDOT() { return DateTime.Now.ToUniversalTime().TimeOfDay.ToString().Substring(0, 8); }
        public static void Debug(object data, [CallerFilePath] string callerFile = "", [CallerMemberName] string callerName = "")
        {
            Console.WriteLine($"{LogDOT()}|{TrimCaller(callerFile)}.{callerName}:{data}");
        }
        public static void DebugMe([CallerFilePath] string callerFile = "", [CallerMemberName] string callerName = "")
        {
            Console.WriteLine($"{LogDOT()}|{TrimCaller(callerFile)}.{callerName}");
        }
        public static void Error(object data, [CallerFilePath] string callerFile = "", [CallerMemberName] string callerName = "")
        {
            Console.WriteLine($"{LogDOT()}|{TrimCaller(callerFile)}.{callerName}:{data}");
            //Stacktrace();
        }

        public static void Stacktrace()
        {
            var stacktrace = Environment.StackTrace;
            stacktrace = stacktrace.Substring(stacktrace.IndexOf('\n') + 1);
            stacktrace = stacktrace.Substring(stacktrace.IndexOf('\n'));
            Console.WriteLine(stacktrace);
        }
    }
}
