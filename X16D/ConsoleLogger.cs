using BitMagic.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace X16D
{
    internal class ConsoleLogger : IEmulatorLogger
    {
        public void Log(string message)
        {
            Console.Write(message);
        }

        public void LogError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        public void LogLine(string message)
        {
            Console.WriteLine(message);
        }
    }
}
