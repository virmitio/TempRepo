using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Management;
using VMProvisioningAgent;

namespace WinImpl
{
    public class WinImpl : IProvisioner
    {
        public string[] MountVHD(out bool Status, string VHD, string ProxyVM, string AlternateInterface)
        {
            if (AlternateInterface != null)
            {
                Type Plug = PluginLoader.FindType(AlternateInterface);
                if (Plug == null)
                {
                    PluginLoader.ScanForPlugins();
                    Plug = PluginLoader.FindType(AlternateInterface);
                }
                if (Plug != null)
                {
                    dynamic Alt = Activator.CreateInstance(Plug);
                    return Alt.MountVHD(out Status, VHD, ProxyVM);
                }
                Status = false;
                return null;
            }

            if (ProxyVM != null)
            {
                
            }

            ManagementObject SvcObj = Utility.GetServiceObject(Utility.GetScope(), Utility.ServiceNames.ImageManagement);
            int result = Utility.WaitForJob((ManagementObject)SvcObj.InvokeMethod("Mount", new object[] { VHD }));
            if (result != 0)
            {
                Status = false;
                return null;
            }


            throw new NotImplementedException();
        }

        public bool UnmountVHD(string VHD, string ProxyVM, string AlternateInterface)
        {
            throw new NotImplementedException();
        }

        public bool WriteFile(byte[] Data, string Location, string ProxyVM, string AlternateInterface)
        {
            throw new NotImplementedException();
        }

        public byte[] ReadFile(out bool Status, string Location, string ProxyVM, string AlternateInterface)
        {
            throw new NotImplementedException();
        }

        public bool WriteUserRegistry(string VHD, string Username, string DataPath, object Data, string DataType, string ProxyVM, string AlternateInterface)
        {
            throw new NotImplementedException();
        }

        public bool WriteMachineRegistry(string VHD, string DataPath, object Data, string DataType, string ProxyVM, string AlternateInterface)
        {
            throw new NotImplementedException();
        }

        public object ReadUserRegistry(out bool Status, string VHD, string Username, string DataPath, string ProxyVM, string AlternateInterface)
        {
            throw new NotImplementedException();
        }

        public object ReadMachineRegistry(out bool Status, string VHD, string DataPath, string ProxyVM, string AlternateInterface)
        {
            throw new NotImplementedException();
        }
    }
}
