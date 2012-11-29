
using System;
using System.Collections.Generic;
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

        }

        public static class ServiceNames
        {
            public const string VSManagement = "MSVM_VirtualSystemManagementService";
            public const string ImageManagement = "MSVM_ImageManagementService";
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
                    new SelectQuery("MSVM_ComputerSystem", 
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

        public static ManagementObject GetSettings(this ManagementObject VM)
        {
            switch (VM["__CLASS"].ToString().ToUpperInvariant())
            {
                case "MSVM_COMPUTERSYSTEM":
                    foreach (ManagementObject item in VM.GetRelated("MSVM_VirtualSystemSettingData"))
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
                        .GetRelated("MSVM_ResourceAllocationSettingData")
                        .Cast<ManagementObject>();
                case "MSVM_VIRTUALSYSTEMSETTINGDATA":
                    return
                        VM.GetRelated("MSVM_ResourceAllocationSettingData")
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
                                           select device).FirstOrDefault();
                    ManagementObject drive = NewObject(ResourceTypes.StorageExtent, ResourceSubTypes.VHD);
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

        public static int AddDevice(this ManagementObject VM, ManagementObject Device)
        {
            ManagementObject Job;
            return AddDevice(VM, Device, out Job);

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

        public static ManagementObject GetServiceObject(ManagementScope Scope, string ServiceName)
        {
            return new ManagementClass(Scope, 
                                new ManagementPath(ServiceName),
                                null).GetInstances()
                                     .Cast<ManagementObject>()
                                     .FirstOrDefault();
        }

        public static ManagementObject NewObject(ResourceTypes ResType, string SubType)
        {
            var AllocCap = new ManagementObjectSearcher(
                new SelectQuery(
                    "MSVM_AllocationCapabilities",
                    "ResourceType = " + ResType +
                    " and ResourceSubType = '" + SubType + "'"
                    )
                ).Get().Cast<ManagementObject>().FirstOrDefault();
            var objType = new ManagementObjectSearcher(
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
                            new SelectQuery("MSVM_ResourceAllocationSettingData",
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
                string id = ((string) vhd["InstanceID"]).Split('\\')[0].Split(':')[-1];
                VMs.AddRange(GetVM(scope, VMID: id));
            }
            return VMs;
        }
    }
}
