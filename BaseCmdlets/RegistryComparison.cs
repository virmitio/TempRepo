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

        private readonly Hive hiveA, hiveB;
        public string HiveA { get { return hiveA.file; } }
        public string HiveB { get { return hiveB.file; } }

        RegistryComparison(string HiveFileA, string HiveFileB)
        {
            hiveA = new Hive(HiveFileA);
            hiveB = new Hive(HiveFileB);
        }
        
        public class Data
        {
            public RegistryValueKind TypeA { get; private set; }
            public object ValueA { get; private set; }
            public RegistryValueKind TypeB { get; private set; }
            public object ValueB { get; private set; }


            public Data(){}

            public Data(object aVal, RegistryValueKind aType, object bVal, RegistryValueKind bType)
            {
                TypeA = aType;
                TypeB = bType;
                ValueA = aVal;
                ValueB = bVal;
            }

            public void SetA(object aVal, RegistryValueKind aType)
            {
                TypeA = aType;
                ValueA = aVal;
            }

            public void SetB(object bVal, RegistryValueKind bType)
            {
                TypeA = bType;
                ValueA = bVal;
            }
        }

        private Dictionary<string, Data> _output = new Dictionary<string, Data>();
        public Dictionary<string, Data> Output { get{return new Dictionary<string, Data>(_output);} }

        public bool DoCompare()
        {
            return hiveA.Init() && hiveB.Init() && InnerCompare(hiveA.root, hiveB.root, @"\", ref _output);
        }

        private static bool InnerCompare(RegistryKey A, RegistryKey B, string root,ref Dictionary<string, Data> output)
        {
            bool current = true;
            try
            {
                if (A != null)
                {
                    // Process A
                    foreach (var Name in A.GetValueNames())
                    {
                        string EntryName = root + A.Name + "::" + Name;
                        var dat = new Data();
                        dat.SetA(A.GetValue(Name), A.GetValueKind(Name));
                        output.Add(EntryName, dat);
                    }
                    foreach (var keyName in A.GetSubKeyNames())
                    {
                        RegistryKey subA = A.OpenSubKey(keyName);
                        RegistryKey subB = B == null ? null : B.OpenSubKey(keyName);
                        current &= InnerCompare(subA, subB, root + keyName + @"\", ref output);
                    }
                }
                if (B != null)
                {
                    foreach (var Name in B.GetValueNames())
                    {
                        string EntryName = root + B.Name + "::" + Name;
                        Data dat = output.ContainsKey(EntryName) ? output[EntryName] : new Data();
                        dat.SetB(B.GetValue(Name), B.GetValueKind(Name));
                        output[EntryName] = dat;
                    }
                    foreach (var keyName in B.GetSubKeyNames())
                    {
                        // when we get here, we have already cleared everything present in the A side
                        RegistryKey subB = B.OpenSubKey(keyName);
                        current &= InnerCompare(null, subB, root + keyName + @"\", ref output);
                    }

                }
                return current;
            }
            catch (Exception e)
            {
                return false;
            }
        }

    }
}
