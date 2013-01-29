using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Spatial;
using System.Text;
using System.Text.RegularExpressions;
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
            public ValueObject(RegistryValueKind Kind, object Object)
            {
                Type = Kind;
                Value = Object;
            }
        }

        private XDictionary<string, XDictionary<string, ValueObject>> Data;

        public RegDiff()
        {
            Data = new XDictionary<string, XDictionary<string, ValueObject>>();
        }

        public RegDiff(RegistryComparison Source, RegistryComparison.Side Side)
        {
            var origin = Source.Output;
            foreach (var data in origin)
            {
                int tmpIndex = data.Key.IndexOf("::");
                string path = data.Key.Substring(0, tmpIndex);
                string name = data.Key.Substring(tmpIndex + 2);

                if (!data.Value.Same)
                    Data[path][name] = Side == RegistryComparison.Side.A
                                           ? new ValueObject(data.Value.TypeA, data.Value.ValueA)
                                           : new ValueObject(data.Value.TypeB, data.Value.ValueB);
            }

        }

        public static RegDiff ReadFromStream(StreamReader Input)
        {
            RegDiff output = new RegDiff();

            Regex RegPath = new Regex(@"\A\[(?<path>.+]\z");
            Regex ValueStr = new Regex(@"\A""(?<name>.+""=(?<type>.+):""(?<value>.*""\z");
            string CurrentPath = String.Empty;
            while (!Input.EndOfStream)
            {
                var line = (Input.ReadLine() ?? String.Empty);
                // concat lines as needed
                while (line.EndsWith(@"\"))
                    line = line.Substring(0, line.LastIndexOf(@"\")) + '\n' + (Input.ReadLine() ?? String.Empty);
                // skip empty lines (which is not the same as empty strings in a multi-string variable)
                if (line.Trim().Equals(String.Empty))
                    continue;
                // check for new path specifier
                var test = RegPath.Match(line);
                if (test.Success)
                    CurrentPath = test.Groups["path"].Value;
                // check for value statement
                else
                {
                    test = ValueStr.Match(line);
                    if (test.Success)
                    {
                        RegistryValueKind kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), test.Groups["type"].Value, true);
                        output.Data[CurrentPath][test.Groups["name"].Value] = new ValueObject(kind, test.Groups["value"].Value);
                    }
                    else
                    {
                        throw new ParseErrorException(String.Format("Unexpected input.  Unable to parse:  '{0}'.", line));
                    }
                }
            }
            return output;
        }

        public string Flatten()
        {
            string output = String.Empty;

            foreach (var path in Data)
            {
                output += String.Format("[{0}]\n", path.Key);
                foreach (var item in path.Value)
                {
                    string tmp = String.Format(@"""{0}""={1}:""", item.Key, item.Value.Type);
                    string value = item.Value.Value.ToString();
                    var realVal = item.Value.Value;
                    switch (item.Value.Type)
                    {
                        case RegistryValueKind.Binary:
                        case RegistryValueKind.DWord:
                        case RegistryValueKind.ExpandString:
                        case RegistryValueKind.MultiString:
                        case RegistryValueKind.QWord:
                        case RegistryValueKind.String:
                            tmp += value.Replace("\n", "\\\n");
                            break;
                        case RegistryValueKind.Unknown:
                        case RegistryValueKind.None:
                        default:
                            throw new ParseErrorException(String.Format("Unable to output value of '{0}::{1}'. Unable to identify value type.", path.Key, item.Key));
                    }
                    tmp += "\"\n";
                    output += tmp;
                }
            }
            return output;
        }

        public void WriteToStream(StreamWriter Output)
        {
            Output.Write(Flatten());
        }

        public bool Apply(RegistryKey Root, Action<string> Log = null)
        {
            if (Root == null)
                throw new ArgumentNullException("Root");
            bool status = true;
            try
            {
                foreach (var path in Data)
                {
                    try
                    {
                        RegistryKey currentPath = path.Key.Split('\\')
                                                      .Aggregate(Root, (current, sub) => current.CreateSubKey(sub));
                        foreach (var item in path.Value)
                        {
                            try
                            {
                                currentPath.SetValue(item.Key, item.Value.Value, item.Value.Type);
                            }
                            catch (Exception e)
                            {
                                if (Log != null)
                                    Log(e.Message + '\n' + e.StackTrace);
                                status = false;
                            }

                        }
                    }
                    catch (Exception e)
                    {
                        if (Log != null)
                            Log(e.Message + '\n' + e.StackTrace);
                        status = false;
                    }
                }

            }
            catch (Exception e)
            {
                if (Log != null)
                    Log(e.Message + '\n' + e.StackTrace);
                status = false;
            }
            return status;
        }

    }
}
