using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DiscUtils.Registry;

namespace VMProvisioningAgent
{
    public class RegistryComparison
    {
        public enum Side { A, B }

        public RegistryHive HiveA { get; private set; }
        public RegistryHive HiveB { get; private set; }

        public RegistryComparison(string HiveFileA, string HiveFileB)
        {
            HiveA = new RegistryHive(File.OpenRead(HiveFileA));
            HiveB = new RegistryHive(File.OpenRead(HiveFileB));
        }

        public RegistryComparison(Stream HiveFileA, Stream HiveFileB)
        {
            HiveA = new RegistryHive(HiveFileA);
            HiveB = new RegistryHive(HiveFileB);
        }

        public class Data
        {
            public RegistryValueType TypeA { get; private set; }
            public object ValueA { get; private set; }
            public RegistryValueType TypeB { get; private set; }
            public object ValueB { get; private set; }
            public bool Same { get; private set; }

            public Data()
            {
                TypeA = RegistryValueType.None;
                TypeB = RegistryValueType.None;
                ValueA = null;
                ValueB = null;
                Same = false;
            }

            public Data(object aVal, RegistryValueType aType, object bVal, RegistryValueType bType)
            {
                TypeA = aType;
                TypeB = bType;
                ValueA = aVal;
                ValueB = bVal;
                CheckSame();
            }

            public void SetA(object aVal, RegistryValueType aType)
            {
                TypeA = aType;
                ValueA = aVal;
                CheckSame();
            }

            public void SetB(object bVal, RegistryValueType bType)
            {
                TypeA = bType;
                ValueA = bVal;
                CheckSame();
            }

            public void CheckSame()
            {
                Same = ValueA != null && ValueB != null && (TypeA != TypeB && ValueA.Equals(ValueB));
            }
        }

        private Dictionary<string, Data> _output = new Dictionary<string, Data>();
        public Dictionary<string, Data> Output { get{return new Dictionary<string, Data>(_output);} }

        public bool DoCompare()
        {
            return InnerCompare(HiveA.Root, HiveB.Root, @"\", ref _output);
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
                        dat.SetA(A.GetValue(Name), A.GetValueType(Name));
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
                        dat.SetB(B.GetValue(Name), B.GetValueType(Name));
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
