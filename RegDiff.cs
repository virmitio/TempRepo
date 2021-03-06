﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Spatial;
using System.Text;
using System.Text.RegularExpressions;
using ClrPlus.Core.Collections;
using DiscUtils.Registry;

namespace VMProvisioningAgent
{
    internal class RegDiff
    {
        internal class ValueObject
        {
            internal RegistryValueType Type { get; private set; }
            internal object Value { get; private set; }
            internal ValueObject(RegistryValueType Kind, object Object)
            {
                Type = Kind;
                Value = Object;
            }
        }

        private readonly XDictionary<string, XDictionary<string, ValueObject>> Data;

        internal RegDiff()
        {
            Data = new XDictionary<string, XDictionary<string, ValueObject>>();
        }

        internal RegDiff(RegistryComparison Source, RegistryComparison.Side Side)
            : this()
        {
            var origin = Source.Output;

            foreach (var data in origin)
            {
                int tmpIndex = data.Key.IndexOf("::");
                string path = data.Key.Substring(0, tmpIndex);
                string name = data.Key.Substring(tmpIndex + 2);

                if (!data.Value.Same)
                    if (Side == RegistryComparison.Side.A &&
                        !(data.Value.TypeA == RegistryValueType.None && data.Value.ValueA == null))
                        Data[path][name] = new ValueObject(data.Value.TypeA, data.Value.ValueA);
                    else if (!(data.Value.TypeB == RegistryValueType.None && data.Value.ValueB == null)) // Side == B
                        Data[path][name] = new ValueObject(data.Value.TypeB, data.Value.ValueB);
            }

        }

        /*        internal static RegDiff ReadFromStream(StreamReader Input)
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
                        RegistryValueType kind = (RegistryValueType)Enum.Parse(typeof(RegistryValueType), test.Groups["type"].Value, true);
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
        */

        internal static RegDiff ReadFromStream(Stream Input)
        {
            try { return ReadFromHive(new RegistryHive(Input, DiscUtils.Ownership.None)); }
            catch (Exception e) { return new RegDiff(); }
        }

        internal static RegDiff ReadFromHive(RegistryHive Input)
        {
            var Out = new RegDiff();
            var Root = Input.Root;
            foreach (var name in Root.GetValueNames())
            {
                Out.Data[String.Empty][name] = new ValueObject(Root.GetValueType(name), Root.GetValue(name));
            }
            foreach (var sub in Root.GetSubKeyNames())
            {
                InnerRead(Root.OpenSubKey(sub), Out.Data);
            }
            return Out;
        }

        private static void InnerRead(RegistryKey key, XDictionary<string, XDictionary<string, ValueObject>> data)
        {
            foreach (var name in key.GetValueNames())
            {
                data[key.Name][name] = new ValueObject(key.GetValueType(name), key.GetValue(name));
            }
            foreach (var sub in key.GetSubKeyNames())
            {
                InnerRead(key.OpenSubKey(sub), data);
            }
        }

        /*        internal string Flatten()
        {
            if (Data == null)
                return String.Empty;

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
                        case RegistryValueType.Binary:
                        case RegistryValueType.Dword:
                        case RegistryValueType.ExpandString:
                        case RegistryValueType.MultiString:
                        case RegistryValueType.QWord:
                        case RegistryValueType.String:
                            tmp += value.Replace("\n", "\\\n");
                            break;
                        //case RegistryValueType.Unknown:
                        case RegistryValueType.None:
                        default:
                            throw new ParseErrorException(String.Format("Unable to output value of '{0}::{1}'. Unable to identify value type.", path.Key, item.Key));
                    }
                    tmp += "\"\n";
                    output += tmp;
                }
            }
            return output;
        }
        */

        internal void WriteToStream(Stream Output)
        {
            //Output.Write(Flatten());
            RegistryHive Out;

            try { Out = new RegistryHive(Output, DiscUtils.Ownership.None); }
            catch (Exception e) { Out = RegistryHive.Create(Output, DiscUtils.Ownership.None); }

            var root = Out.Root;

            foreach (var path in Data)
            {
                var currentKey = root.CreateSubKey(path.Key);
                foreach (var val in path.Value)
                    currentKey.SetValue(val.Key, val.Value.Value, val.Value.Type);
            }
        }

        internal bool ApplyTo(RegistryKey Root, Action<string> Log = null)
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
