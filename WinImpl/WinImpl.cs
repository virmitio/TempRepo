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
        private static class DiskStrings
        {
            public const string MountedStorage = "MSVM_MountedStorageImage";
            public const string DiskDrive = "Win32_DiskDrive";
            public const string DiskPartition = "Win32_DiskPartition";
            public const string LogicalDisk = "Win32_LogicalDisk";
        }

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

                //TODO: Mount to VM, then report location (if possible?)
            }

            ManagementObject SvcObj = Utility.GetServiceObject(Utility.GetScope(), Utility.ServiceNames.ImageManagement);
            int result = Utility.WaitForJob((ManagementObject)SvcObj.InvokeMethod("Mount", new object[] { VHD }));
            if (result != 0)
            {
                Status = false;
                return null;
            }
            
            List<string> ret = new List<string>();
            var image = new ManagementObjectSearcher(Utility.GetScope(), new SelectQuery(DiskStrings.MountedStorage)).Get()
                            .Cast<ManagementObject>()
                            .Where(Obj => (Obj["Name"].ToString().Equals(VHD, StringComparison.InvariantCultureIgnoreCase)))
                            .FirstOrDefault();
            var baseScope = new ManagementScope(@"root\cimv2");
            var disk = new ManagementObjectSearcher(baseScope,
                                                    new SelectQuery(DiskStrings.DiskDrive,
                                                                    "SCSILogicalUnit=" + image["Lun"] +
                                                                    " and SCSIPort=" + image["PortNumber"] +
                                                                    " and SCSITargetID=" + image["TargetID"]))
                .Get().Cast<ManagementObject>().FirstOrDefault();
            var parts = disk.GetRelated(DiskStrings.DiskPartition);
            foreach (var drives in from ManagementObject part in parts select part.GetRelated(DiskStrings.LogicalDisk))
            {
                ret.AddRange(from ManagementObject drive in drives select drive["DeviceID"].ToString());
            }

            Status = true;
            return ret.ToArray();
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
