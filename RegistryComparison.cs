using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        public Dictionary<string, Data> Output { get { return new Dictionary<string, Data>(_output ?? new Dictionary<string, Data>()); } }

        public bool DoCompare()
        {
            var t = InnerCompare(HiveA.Root, HiveB.Root, @"\");
            t.Wait();
            return t.Result;
        }

        private Task<bool> InnerCompare(RegistryKey A, RegistryKey B, string root)
        {
            return Task.Factory.StartNew(() =>
                {

                    List<Task<bool>> tasks = new List<Task<bool>>();
                    try
                    {
                        if (A != null)
                        {
                            // Process A
                            string[] aVals;
                            lock (HiveA)
                                aVals = A.GetValueNames();
                            foreach (var Name in aVals)
                            {
                                string EntryName;
                                lock (HiveA)
                                    EntryName = root + A.Name + "::" + Name;
                                var dat = new Data();
                                lock (HiveA)
                                    dat.SetA(A.GetValue(Name), A.GetValueType(Name));
                                _output.Add(EntryName, dat);
                            }
                            string[] ASubKeys;
                            lock (HiveA)
                                ASubKeys = A.GetSubKeyNames();
                            string[] BSubKeys = new string[0];
                            if (B != null)
                                lock (HiveB)
                                    BSubKeys = B.GetSubKeyNames();
                            tasks.AddRange(ASubKeys.AsParallel().Select(keyName =>
                                {
                                    RegistryKey aSub, bSub;
                                    lock (HiveA)
                                        aSub = A.OpenSubKey(keyName);
                                    lock (HiveB)
                                        bSub = B == null
                                                   ? null
                                                   : BSubKeys.Contains(keyName, StringComparer.CurrentCultureIgnoreCase)
                                                         ? B.OpenSubKey(keyName)
                                                         : null;
                                    return InnerCompare(aSub, bSub, root + keyName + @"\");
                                }));
                        }
                        if (B != null)
                        {
                            // Process B
                            string[] bVals;
                            lock (HiveB)
                                bVals = B.GetValueNames();

                            foreach (var Name in bVals)
                            {
                                string EntryName;
                                lock (HiveB)
                                    EntryName = root + B.Name + "::" + Name;
                                Data dat = _output.ContainsKey(EntryName) ? _output[EntryName] : new Data();
                                lock (HiveB)
                                    dat.SetB(B.GetValue(Name), B.GetValueType(Name));
                                _output[EntryName] = dat;
                            }
                            string[] BSubKeys;
                            lock (HiveB)
                                BSubKeys = B.GetSubKeyNames();
                            tasks.AddRange(BSubKeys.AsParallel().Select(keyName =>
                                {
                                    RegistryKey bSub;
                                    lock (HiveB)
                                        bSub = B.OpenSubKey(keyName);
                                    return InnerCompare(null, bSub, root + keyName + @"\");
                                }));
                        }

                        return tasks.Aggregate(true, (ret, task) =>
                            {
                                task.Wait();
                                return ret && task.Result;
                            });
                    }
                    catch (Exception e)
                    {
                        throw;
                    }
                }, TaskCreationOptions.AttachedToParent);
        }

    }
}
