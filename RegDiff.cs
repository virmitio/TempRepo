using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ClrPlus.Core.Collections;
using Microsoft.Win32;

namespace VMProvisioningAgent
{
    class RegDiff
    {
        public class ValueObject
        {
            public RegistryValueKind Type { get; private set; }
            public object Value { get; private set; }

        }

        private XDictionary<string, XDictionary<string, ValueObject>> Data;

        public RegDiff()
        {
            // ??
        }

        public RegDiff(RegistryComparison Source, RegistryComparison.Side Side)
        {
            
        }

        public static RegDiff ReadFromStream(StreamReader Input)
        {
            throw new NotImplementedException();
        }

        public string Flatten()
        {
            throw new NotImplementedException();
        }

        public void WriteToStream(StreamWriter Output)
        {
            Output.Write(Flatten());
        }


    }
}
