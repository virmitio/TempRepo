using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace testdrv
{
    class Program
    {
        static void Main(string[] args)
        {
            VMProvisioningAgent.PluginLoader.ScanForPlugins();
            var T = VMProvisioningAgent.PluginLoader.FindType("WinImpl");
            Console.Out.WriteLine(T == null ? "Boo!" : "Yay!");
        }
    }
}
