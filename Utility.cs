
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;

namespace VMProvisioningAgent
{
    public static class Utility
    {

        public enum VMState
        {
            Running = 2,
            Stopped = 3,
            Paused = 32768,
            Suspended = 32769,
            Starting = 32770,
            Snapshotting = 32771,
            Saving = 32773,
            Stopping = 32774
        }

        public enum ReturnCodes
        {
            OK = 0,
            JobStarted = 4096,
            Failed = 32768,
            AccessDenied = 32769,
            NotSupported = 32770,
            Unknown = 32771,
            Timeout = 32772,
            InvalidParameter = 32773,
            SystemInUse = 32774,
            InvalidStateForOperation = 32775,
            IncorrectDataType = 32776,
            SystemNotAvailable = 32777,
            OutOfMemory = 32778,
            FileNotFound = 32779
        }

        public enum JobStates : ushort
        {
            New = 2,
            Starting = 3,
            Running = 4,
            Suspended = 5,
            ShuttingDown = 6,
            Completed = 7,
            Terminated = 8,
            Killed = 9,
            Exception = 10,
            Service = 11
        }

        public enum ResourceTypes : ushort
        {
            Other = 1,
            ComputerSystem = 2,
            Processor = 3,
            Memory = 4,
            IDEController = 5,
            ParallelSCSIHBA = 6,
            FCHBA = 7,
            iSCSIHBA = 8,
            IBHCA = 9,
            EthernetAdapter = 10,
            OtherNetworkAdapter = 11,
            IOSlot = 12,
            IODevice = 13,
            FloppyDrive = 14,
            CDDrive = 15,
            DVDDrive = 16,
            SerialPort = 17,
            ParallelPort = 18,
            USBController = 19,
            GraphicsController = 20,
            StorageExtent = 21,
            Disk = 22,
            Tape = 23,
            OtherStorageDevice = 24,
            FirewireController = 25,
            PartitionableUnit = 26,
            BasePartitionableUnit = 27,
            PowerSupply = 28,
            CoolingDevice = 29
        }

        public static class ResourceSubTypes
        {
            public const string VHD = "Microsoft Virtual Hard Disk";
            public const string CDDVD = "Microsoft Virtual CD/DVD Disk";
            public const string ControllerIDE = "Microsoft Emulated IDE Controller";
            public const string ControllerSCSI = "Microsoft Synthetic SCSI Controller";
            public const string LegacyNIC = "Microsoft Emulated Ethernet Port";
            public const string SyntheticNIC = "Microsoft Synthetic Ethernet Port";
            public const string SerialPort = "Microsoft Serial Port";
        }

        public static class ServiceNames
        {
            public const string VSManagement = "MSVM_VirtualSystemManagementService";
            public const string ImageManagement = "MSVM_ImageManagementService";
            public const string SwitchManagement = "MsVM_VirtualSwitchManagementService";
        }

        public static class DiskStrings
        {
            public const string MountedStorage = "MSVM_MountedStorageImage";
            public const string DiskDrive = "Win32_DiskDrive";
            public const string DiskPartition = "Win32_DiskPartition";
            public const string LogicalDisk = "Win32_LogicalDisk";
        }

        public static class VMStrings
        {
            public const string GlobalSettingData = "MSVM_VirtualSystemGlobalSettingData";
            public const string ResAllocData = "MSVM_ResourceAllocationSettingData";
            public const string SettingData = "MSVM_VirtualSystemSettingData";
            public const string ComputerSystem = "MSVM_ComputerSystem";
            public const string MemorySettings = "MSVM_MemorySettingData";
            public const string ProcessorSettings = "MsVM_ProcessorSettingData";
            public const string VirtualSwitch = "Msvm_VirtualSwitch";
        }

        public static IEnumerable<ManagementObject> GetVM(string VMName = null, string VMID = null, string Server = null)
        {
            ManagementScope scope = GetScope(Server);
            return GetVM(scope, VMName, VMID);
        }

        public static IEnumerable<ManagementObject> GetVM(ManagementScope scope, string VMName = null, string VMID = null)
        {
            VMID = VMID ?? "%";
            var VMs = new ManagementObjectSearcher(scope,
                    new SelectQuery(VMStrings.ComputerSystem, 
                                    "ProcessID >= 0 and ElementName like '" +
                                    VMName ?? "%" + "' and Name like '" +
                                    VMID.ToUpperInvariant() + "'")
                );
            return VMs.Get().Cast<ManagementObject>().ToList();
        }

        public static IEnumerable<string> GetVHDs(this ManagementObject VM)
        {
            List<string> vhds = new List<string>();
            switch (VM["__CLASS"].ToString().ToUpperInvariant())
            {
                case "MSVM_COMPUTERSYSTEM":
                case "MSVM_VIRTUALSYSTEMSETTINGDATA":
                    vhds.AddRange(VM.GetDevices().Where(Dev => (Dev["ElementName"].ToString().Equals(
                        "Hard Disk Image"))).Select(HD => ((String[]) (HD["Connection"])).First()));
                    break;
                case "MSVM_RESOURCEALLOCATIONSETTINGDATA":
                    if (VM["ElementName"].ToString().Equals("Hard Disk Image",StringComparison.InvariantCultureIgnoreCase))
                    {
                        vhds.Add(((String[])(VM["Connection"])).First());
                    }
                    break;
                default:
                    break;
            }
            return vhds;
        }

        /// <summary>
        /// Returns the MSVM_VirtualSystemSettingData associated with the VM.
        /// </summary>
        /// <param name="VM"></param>
        /// <returns></returns>
        public static ManagementObject GetSettings(this ManagementObject VM)
        {
            switch (VM["__CLASS"].ToString().ToUpperInvariant())
            {
                case "MSVM_COMPUTERSYSTEM":
                    foreach (ManagementObject item in VM.GetRelated(VMStrings.SettingData))
                    {
                        return item;
                    }
                    break;
                default:
                    break;
            }
            return null;
        }

        public static IEnumerable<ManagementObject> GetDevices(this ManagementObject VM)
        {
            switch (VM["__CLASS"].ToString().ToUpperInvariant())
            {
                case "MSVM_COMPUTERSYSTEM":
                    return
                        VM.GetSettings()
                        .GetRelated(VMStrings.ResAllocData)
                        .Cast<ManagementObject>();
                case "MSVM_VIRTUALSYSTEMSETTINGDATA":
                    return
                        VM.GetRelated(VMStrings.ResAllocData)
                        .Cast<ManagementObject>();
                default:
                    break;
            }
            return null;
        }

        public static IEnumerable<ManagementObject> Filter(this IList<ManagementObject> list, string key, string value, bool NOT = false)
        {
            if (list == null)
                return null;
            List<ManagementObject> ret = new List<ManagementObject>();
            foreach (ManagementObject obj in list)
            {
                if (NOT)
                {
                    if (!obj[key].ToString().Equals(value, StringComparison.InvariantCultureIgnoreCase))
                        ret.Add(obj);
                }
                else
                {
                    if (obj[key].ToString().Equals(value, StringComparison.InvariantCultureIgnoreCase))
                        ret.Add(obj);
                }
            }
            return ret;
        }

        public static ManagementObject GetObject(string WMIPath)
        {
            try
            {
                return new ManagementObject(WMIPath);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static bool NewDifferencingDisk(string SourceVHD, string OutputVHD, string Server = null)
        {
            ManagementObject SvcObj = GetServiceObject(GetScope(Server), ServiceNames.ImageManagement);
            ManagementObject job = new ManagementObject();
            int result = (int)SvcObj.InvokeMethod("CreateDifferencingVirtualHardDisk", new object[] {OutputVHD, SourceVHD, job});
            switch (result)
            {
                case (int)ReturnCodes.OK:
                    return true;
                case (int)ReturnCodes.JobStarted:
                    return (WaitForJob(job) == 0);
                default:
                    return false;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="VM"></param>
        /// <param name="SourceVHD"></param>
        /// <param name="ControllerDevice">Controller device to attach to.  If null, will attach to first contoller discovered on VM.</param>
        /// <returns></returns>
        public static bool AttachVHD(this ManagementObject VM, string SourceVHD, ManagementObject ControllerDevice = null)
        {
            switch (VM["__CLASS"].ToString().ToUpperInvariant())
            {
                case "MSVM_COMPUTERSYSTEM":
                    ControllerDevice = ControllerDevice ??
                                       (from device in VM.GetDevices() where 
                                           device["ResourceSubType"].ToString().Equals(ResourceSubTypes.ControllerIDE)
                                           || device["ResourceSubType"].ToString().Equals(ResourceSubTypes.ControllerSCSI)
                                           select device).First();
                    ManagementObject drive = NewResource(ResourceTypes.StorageExtent, ResourceSubTypes.VHD);
                    drive["Connection"] = SourceVHD;
                    drive["Parent"] = ControllerDevice.Path;
                    ManagementObject job;
                    int ret = VM.AddDevice(drive, out job);
                    switch (ret)
                    {
                        case (int)ReturnCodes.OK:
                            return true;
                        case (int)ReturnCodes.JobStarted:
                            return (WaitForJob(job) == 0);
                        default:
                            return false;
                    }
                default:
                    return false;
            }

        }

        public static int WaitForJob(ManagementObject Job)
        {
            try
            {
                while ((ushort)Job["JobState"] == (ushort)JobStates.Running ||
                       (ushort)Job["JobState"] == (ushort)JobStates.Starting)
                {
                    System.Threading.Thread.Sleep(250);
                    Job.Get();
                }
                return (ushort) Job["JobState"] == (ushort) JobStates.Completed
                           ? 0
                           : (ushort) Job["ErrorCode"];
            }
            catch (Exception)
            {
                return -1;
            }
        }

        public static bool AddDevice(this ManagementObject VM, ManagementObject Device)
        {
            ManagementObject Job;
            uint ret = (uint)AddDevice(VM, Device, out Job);
            switch (ret)
            {
                case (int)ReturnCodes.OK:
                    return true;
                case (int)ReturnCodes.JobStarted:
                    return (WaitForJob(Job) == 0);
                default:
                    return false;
            }
        }

        public static int AddDevice(this ManagementObject VM, ManagementObject Device, out ManagementObject Job)
        {
            Job = null;
            switch (VM["__CLASS"].ToString().ToUpperInvariant())
            {
                case "MSVM_COMPUTERSYSTEM":
                    ManagementObject ServiceObj = GetServiceObject(VM.GetScope(), ServiceNames.VSManagement);
                    Job = new ManagementObject();
                    return (Int32)ServiceObj.InvokeMethod("AddVirtualSystemResources",
                                            new object[] {VM.Path, Device.GetText(TextFormat.WmiDtd20), null, Job});
                default:
                    return -1;
            }
        }

        public static bool ModifyDevice(this ManagementObject VM, ManagementObject Device)
        {
            ManagementObject Job;
            uint ret = (uint)ModifyDevice(VM, Device, out Job);
            switch (ret)
            {
                case (int)ReturnCodes.OK:
                    return true;
                case (int)ReturnCodes.JobStarted:
                    return (WaitForJob(Job) == 0);
                default:
                    return false;
            }
        }

        public static int ModifyDevice(this ManagementObject VM, ManagementObject Device, out ManagementObject Job)
        {
            Job = null;
            switch (VM["__CLASS"].ToString().ToUpperInvariant())
            {
                case "MSVM_COMPUTERSYSTEM":
                    ManagementObject ServiceObj = GetServiceObject(VM.GetScope(), ServiceNames.VSManagement);
                    Job = new ManagementObject();
                    return (Int32)ServiceObj.InvokeMethod("ModifyVirtualSystemResources",
                                            new object[] { VM.Path, Device.GetText(TextFormat.WmiDtd20), null, Job });
                default:
                    return -1;
            }
        }

        public static ManagementObject GetServiceObject(ManagementScope Scope, string ServiceName)
        {
            return new ManagementClass(Scope, 
                                new ManagementPath(ServiceName),
                                null).GetInstances()
                                     .Cast<ManagementObject>()
                                     .FirstOrDefault();
        }

        public static ManagementObject NewResource(ResourceTypes ResType, string SubType, string Server = null)
        {
            return NewResource(ResType, SubType, GetScope(Server));
        }

        public static ManagementObject NewResource(ResourceTypes ResType, string SubType, ManagementScope Scope)
        {
            var AllocCap = new ManagementObjectSearcher(Scope,
                new SelectQuery(
                    "MSVM_AllocationCapabilities",
                    "ResourceType = " + ResType +
                    " and ResourceSubType = '" + SubType + "'"
                    )
                ).Get().Cast<ManagementObject>().FirstOrDefault();
            var objType = new ManagementObjectSearcher(Scope,
                new SelectQuery(
                    "MSVM_SettingsDefineCapabilities",
                    "ValueRange = 0 and GroupComponent = '" + AllocCap["__Path"] + "'"
                    )
                ).Get().Cast<ManagementObject>().FirstOrDefault();
            return new ManagementClass(objType["PartComponent"].ToString()).CreateInstance();
        }

        public static ManagementScope GetScope(string Server = null)
        {
            var scope = new ManagementScope(@"\\" + Server ?? Environment.MachineName +
                                            @"\root\virtualization");
            scope.Connect();
            return scope;
        }

        public static ManagementScope GetScope(this ManagementObject Item)
        {
            if (Item == null)
                return null;
            var scope = new ManagementScope(@"\\" + Item["__SERVER"] ?? Environment.MachineName +
                                            @"\" + Item["__NAMESPACE"] ?? @"\root\virtualization");
            scope.Connect();
            return scope;
        }

        /// <summary>
        /// Returns a list of VM ManagementObjects which have the specified VHD attached.
        /// </summary>
        /// <param name="VHD"></param>
        /// <param name="Server"></param>
        /// <returns></returns>
        public static IEnumerable<ManagementObject> LocateVHD(string VHD, string Server = null)
        {
            ManagementScope scope = GetScope(Server);
            var VHDs = new ManagementObjectSearcher(scope,
                            new SelectQuery(VMStrings.ResAllocData,
                                            "ResourceSubType like '" +
                                            ResourceSubTypes.VHD + "'")).Get().Cast<ManagementObject>();
            VHDs = VHDs.Where(obj =>
                                  {
                                      string HD = ((string[]) obj["Connection"])[0];
                                      return HD.Equals(VHD, StringComparison.InvariantCultureIgnoreCase);
                                  });
            List<ManagementObject> VMs = new List<ManagementObject>();
            foreach (var vhd in VHDs)
            {
                string id = ((string) vhd["InstanceID"]).Split('\\')[0].Split(':').Last();
                VMs.AddRange(GetVM(scope, VMID: id));
            }
            return VMs;
        }

        public static bool PathIsFromVHD(string Location)
        {
            string root = Path.GetPathRoot(Location);
            if (root == null)
                return false;
            var scope = new ManagementScope(@"root\cimv2");
            scope.Connect();
            var drives = new ManagementObjectSearcher(scope,
                                         new SelectQuery(DiskStrings.LogicalDisk,
                                                         "DeviceID like '" +
                                                         root.Substring(0, root.IndexOf(':')) +
                                                         "'")).Get();
            return
                drives.Cast<ManagementObject>().Any(
                    drive =>
                    drive.GetRelated(DiskStrings.DiskPartition).Cast<ManagementObject>().Any(
                        part =>
                        part.GetRelated(DiskStrings.DiskDrive).Cast<ManagementObject>().Any(
                            device =>
                            new ManagementObjectSearcher(GetScope(),
                                                         new SelectQuery(DiskStrings.MountedStorage,
                                                                         "Lun=" + device["SCSILogicalUnit"] +
                                                                         " and PortNumber=" + device["SCSIPort"] +
                                                                         " and TargetID=" + device["SCSITargetID"]))
                                .Get().Cast<ManagementObject>().Any())));
        }

        public static string PathToVHD(string Location)
        {
            string root = Path.GetPathRoot(Location);
            if (root == null)
                return null;
            var scope = new ManagementScope(@"root\cimv2");
            scope.Connect();
            var drives = new ManagementObjectSearcher(scope,
                                         new SelectQuery(DiskStrings.LogicalDisk,
                                                         "DeviceID like '" +
                                                         root.Substring(0, root.IndexOf(':')) +
                                                         "'")).Get();

            return (from ManagementObject drive in drives
                    from part in drive.GetRelated(DiskStrings.DiskPartition).Cast<ManagementObject>()
                    from device in part.GetRelated(DiskStrings.DiskDrive).Cast<ManagementObject>()
                    select device).Select(
                        device =>
                        new ManagementObjectSearcher(GetScope(),
                                                     new SelectQuery(DiskStrings.MountedStorage,
                                                                     "Lun=" + device["SCSILogicalUnit"] +
                                                                     " and PortNumber=" + device["SCSIPort"] +
                                                                     " and TargetID=" + device["SCSITargetID"]))
                            .Get().Cast<ManagementObject>()
                            .First()["Name"]
                            .ToString())
                .FirstOrDefault();
        }

        public static ManagementObject NewVM(string VMName, string Server = null)
        {
            if (VMName == null)
                return null;
            var MgmtSvc = GetServiceObject(GetScope(Server), ServiceNames.VSManagement);
            var settings = new ManagementClass(GetScope(Server),
                                               new ManagementPath(VMStrings.GlobalSettingData),
                                               new ObjectGetOptions()).CreateInstance();
            if (settings == null)
                return null;
            settings["ElementName"] = VMName;
            ManagementObject Comp = new ManagementObject();
            ManagementObject Job = new ManagementObject();
            var result = (int)MgmtSvc.InvokeMethod("DefineVirtualSystem",
                                                           new object[]
                                                               {
                                                                   settings.GetText(TextFormat.WmiDtd20),
                                                                   null, null, Comp, Job
                                                               });
            switch (result)
            {
                case (int)ReturnCodes.OK:
                    return Comp;
                case (int)ReturnCodes.JobStarted:
                    return WaitForJob(Job) == 0 ? Comp : null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Modifies the CPU and/or Memory configuration of a Virtual Machine.
        /// </summary>
        /// <param name="VMName">Name of the VM to modify.</param>
        /// <param name="Server">The server on which the VM resides.</param>
        /// <param name="ProcCount">Number of processors to allocate.  'null' to leave unchanged.</param>
        /// <param name="ProcLimit">Percent limit of processor usage to allow. Range: 1 to 1000.  'null' to leave unchanged.</param>
        /// <param name="Memory">Amount of initial RAM (in MB) to allocate.  'null' to leave unchanged.</param>
        /// <param name="MemoryLimit">Maximum RAM that may be allocated when using Dynamic Memory.  Only applicable if Dynamic Memory is enabled.  'null' to leave unchanged.</param>
        /// <param name="EnableDynamicMemory">Enables or disables Dynamic Memory on this VM.  'null' to leave unchanged.</param>
        /// <returns>True if a change was succesfully made.</returns>
        public static bool ModifyVMConfig(string VMName, string Server = null, int? ProcCount = null, int? ProcLimit = null, int? Memory = null, int? MemoryLimit = null, bool? EnableDynamicMemory = null)
        {
            // Do we have inputs?
            if (ProcCount == null && ProcLimit == null && Memory == null && MemoryLimit == null && EnableDynamicMemory == null)
                return false;

            if (VMName == null || VMName.Equals(String.Empty))
                throw new ArgumentException();

            ManagementScope scope = GetScope(Server);
            ManagementObject VM = GetVM(scope, VMName).FirstOrDefault();
            if (VM == null)
                throw new ArgumentException();

            return ModifyVMConfig(VM, ProcCount, ProcLimit, Memory, MemoryLimit, EnableDynamicMemory);
        }

        /// <summary>
        /// Modifies the CPU and/or Memory configuration of a Virtual Machine.
        /// </summary>
        /// <param name="VM">The Virtual Machine to modify.</param>
        /// <param name="ProcCount">Number of processors to allocate.  'null' to leave unchanged.</param>
        /// <param name="ProcLimit">Percent limit of processor usage to allow. Range: 1 to 1000.  'null' to leave unchanged.</param>
        /// <param name="Memory">Amount of initial RAM (in MB) to allocate.  'null' to leave unchanged.</param>
        /// <param name="MemoryLimit">Maximum RAM that may be allocated when using Dynamic Memory.  Only applicable if Dynamic Memory is enabled.  'null' to leave unchanged.</param>
        /// <param name="EnableDynamicMemory">Enables or disables Dynamic Memory on this VM.  'null' to leave unchanged.</param>
        /// <returns>True if a change was succesfully made.</returns>
        public static bool ModifyVMConfig(this ManagementObject VM, int? ProcCount = null, int? ProcLimit = null, int? Memory = null, int? MemoryLimit = null, bool? EnableDynamicMemory = null)
        {
            // Do we have inputs?
            if (ProcCount == null && ProcLimit == null && Memory == null && MemoryLimit == null && EnableDynamicMemory == null)
                return false;

            // Rough verify of inputs
            if (VM == null || !VM["__CLASS"].ToString().Equals(VMStrings.ComputerSystem,StringComparison.InvariantCultureIgnoreCase))
                throw new ArgumentException();
            if ((ProcCount != null && (ProcCount < 1 || ProcCount > 4)) ||
                (ProcLimit != null && (ProcLimit < 1 || ProcLimit > 1000)) ||
                (Memory != null && Memory < 1) ||
                (MemoryLimit != null && ((Memory != null && MemoryLimit < Memory) || MemoryLimit < 1)))
                throw new ArgumentOutOfRangeException();

            ManagementScope scope = VM.GetScope();

            ManagementObject VMSettings = VM.GetSettings();
            List<string> NewSettings = new List<string>();

            if (ProcCount != null || ProcLimit != null)
            {
                var set = VMSettings.GetRelated(VMStrings.ProcessorSettings).Cast<ManagementObject>().First();
                if (ProcCount != null)
                    set["VirtualQuantity"] = ProcCount;
                if (ProcLimit != null)
                    set["Limit"] = (ProcLimit*100); // Base unit is %0.001, but I'm only operating in tenths of a percent.
                NewSettings.Add(set.GetText(TextFormat.WmiDtd20));
            }

            if (Memory != null || MemoryLimit != null || EnableDynamicMemory != null)
            {
                var set = VMSettings.GetRelated(VMStrings.MemorySettings).Cast<ManagementObject>().First();
                
                if (EnableDynamicMemory != null)
                    set["DynamicMemoryEnabled"] = EnableDynamicMemory;

                if (MemoryLimit != null)
                {
                    if ((bool)set["DynamicMemoryEnabled"])
                    {
                        set["Limit"] = MemoryLimit;
                        if ((int)set["VirtualQuantity"] > MemoryLimit)
                            set["VirtualQuantity"] = MemoryLimit;
                    }
                    else if (Memory == null)
                        set["VirtualQuantity"] = MemoryLimit;
                }
                if (Memory != null)
                {
                    set["VirtualQuantity"] = Memory;
                    set["Reservation"] = Memory;
                    if ((bool)set["DynamicMemoryEnabled"])
                        set["Limit"] = Memory;
                }
                NewSettings.Add(set.GetText(TextFormat.WmiDtd20));
            }

            ManagementObject Job = new ManagementObject();
            var MgmtSvc = GetServiceObject(scope, ServiceNames.VSManagement);
            uint ret = (uint)MgmtSvc.InvokeMethod("ModifyVirtualSystemResources", new object[]
                                                                     {
                                                                         VM,
                                                                         NewSettings.ToArray(),
                                                                         Job
                                                                     });
            switch (ret)
            {
                case (int)ReturnCodes.OK:
                    return true;
                case (int)ReturnCodes.JobStarted:
                    return (WaitForJob(Job) == 0);
                default:
                    return false;
            }
        }

        public static bool AddNIC(this ManagementObject VM, string VirtualSwitch = null, bool Legacy = false)
        {
            // Sanity Check
            if (VM == null || !VM["__CLASS"].ToString().Equals(VMStrings.ComputerSystem, StringComparison.InvariantCultureIgnoreCase))
            {
                return false;
            }

            ManagementScope scope = GetScope(VM);
            ManagementObject NIC = NewResource(ResourceTypes.EthernetAdapter,
                                               (Legacy ? ResourceSubTypes.LegacyNIC : ResourceSubTypes.SyntheticNIC),
                                               scope);
            NIC["ElementName"] = (Legacy ? "Legacy " : "") + "Network Adapter";
            NIC["VirtualSystemIdentifiers"] = Guid.NewGuid().ToString("B");
            if (VirtualSwitch != null)
            {
                ManagementObject Switch =
                    new ManagementObjectSearcher(scope,
                                                 new SelectQuery(VMStrings.VirtualSwitch,
                                                                 "ElementName like '" + VirtualSwitch + "'")).Get().Cast
                        <ManagementObject>().FirstOrDefault();
                if (Switch == null)
                {
                    NIC.Delete();
                    return false;
                }
                
                ManagementObject Port = new ManagementObject();
                string PortGUID = Guid.NewGuid().ToString();
                ManagementObject SwitchSvc = GetServiceObject(scope, ServiceNames.SwitchManagement);
                uint ret = (uint)SwitchSvc.InvokeMethod("CreateSwitchPort", new object[]
                                                                          {
                                                                              Switch,
                                                                              PortGUID,
                                                                              PortGUID,
                                                                              String.Empty,
                                                                              Port
                                                                          });
                if (ret != (int)ReturnCodes.OK)
                {
                    NIC.Delete();
                    return false;
                }
                NIC["Connection"] = new [] {Port.Path.ToString()};
            }
            return VM.AddDevice(NIC);
        }

        public static bool SetSerialPort(this ManagementObject VM, string PipeName, int PortNumber = 1)
        {
            // Sanity Check
            if (VM == null || !VM["__CLASS"].ToString().Equals(VMStrings.ComputerSystem, StringComparison.InvariantCultureIgnoreCase))
            {
                return false;
            }

            ManagementObject SP = VM.GetDevices().FirstOrDefault(D => D["ResourceSubType"].ToString()
                                                                                          .Equals(ResourceSubTypes.SerialPort, StringComparison.InvariantCultureIgnoreCase) &&
                                                                      D["Caption"].ToString().EndsWith(""+PortNumber));
            if (SP == null)
                return false;

            SP["Connection"] = new string[] {PipeName};
            return VM.ModifyDevice(SP);
        }

        public static bool DestroyVM(this ManagementObject VM)
        {
            if (VM == null)
                return false;
            var MgmtSvc = GetServiceObject(GetScope(VM), ServiceNames.VSManagement);
            ManagementObject Job = new ManagementObject();
            var result = (int)MgmtSvc.InvokeMethod("DestroyVirtualSystem", new object[] {VM.GetText(TextFormat.WmiDtd20), Job});
            return (result == (int)ReturnCodes.OK || WaitForJob(Job) == 0);
        }
    }

}
