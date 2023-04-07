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
        private IEmulatorLogger? _secondaryLogger;

        public void AddSecondaryLogger(IEmulatorLogger? secondaryLogger)
        {
            _secondaryLogger = secondaryLogger;
        }

        public void Log(string message)
        {
            Console.Write(message);

            if (_secondaryLogger != null) _secondaryLogger.Log(message);
        }

        public void LogError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ResetColor();

            if (_secondaryLogger != null) _secondaryLogger.LogError(message);
        }

        public void LogLine(string message)
        {
            Console.WriteLine(message);

            if (_secondaryLogger != null) _secondaryLogger.LogLine(message);
        }
    }
}
