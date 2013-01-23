using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace VMProvisioningAgent
{
    class RegistryComparison
    {
        private class Hive
        {
            public readonly string file;
            public RegistryKey root { get; private set; }

            public Hive(string FileName)
            {
                file = FileName;
            }
            

            public bool Init()
            {
                if (file == null || file.Equals(String.Empty))
                    return false;
                if (!File.Exists(file))
                    return false;
                string tmp = RegExtra.LoadHive(RegistryHive.LocalMachine, file);
                if (tmp == null)
                    return false;
                root = Registry.LocalMachine.OpenSubKey(tmp);
                return root != null;
            }
        }

        private Hive hiveA, hiveB;
        public string HiveA { get { return hiveA.file; } }
        public string HiveB { get { return hiveB.file; } }

        RegistryComparison(string HiveFileA, string HiveFileB)
        {
            hiveA = new Hive(HiveFileA);
            hiveB = new Hive(HiveFileB);
        }


        // output storage container
        // DoCompare()


        public bool DoCompare()
        {
            return hiveA.Init() && hiveB.Init() ? InnerCompare(hiveA.root, hiveB.root, ref Output) : false;
        }

        private static bool InnerCompare(RegistryKey A, RegistryKey B, ref ?? output)
        {
            
        }

    }
}
