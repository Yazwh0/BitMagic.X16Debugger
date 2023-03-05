using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BitMagic.X16Debugger;


internal static class PathFunctions
{
    // This will break on linux...
    public static string FixPath(string path)
    {
        return path.ToLower();
    }
}
