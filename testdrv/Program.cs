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
            //VMProvisioningAgent.PluginLoader.ScanForPlugins();

            var li = new string[255];
            for (int i = 0; i < 255; i++)
                li[i] = string.Format(@"C:\VM\Disk\A-{0}.vhd", (i+1));
            var agent = new VMProvisioningAgent.ProvisionVM {VHD = li, Name = args[0]};
            agent.Go();
        }
    }
}
