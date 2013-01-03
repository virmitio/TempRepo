using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using System.Management.Automation;
using ClrPlus.Powershell.Core.Commands;

namespace VMProvisioningAgent
{
    [Cmdlet("Provision", "VM")]
    public class ProvisionVM : RestableCmdlet<ProvisionVM>
    {
        [Parameter(Mandatory = true, ValueFromPipeline = true)]
        [ValidateNotNullOrEmpty]
        public string Name;
        
        [Parameter][ValidateRange(1, int.MaxValue)] public int Memory = 1024; //1GB default == 1024 MB
        [Parameter] public SwitchParameter DynamicMemory = false;
        [Parameter][ValidateRange(0, int.MaxValue)]public int DynamicMemoryLimit = 0;
        [Parameter] [ValidateRange(1, 64)] public int Processors = 1;
        [Parameter] [ValidateRange(1, 1000)] public int CPULimit = 1000; // 100% in tenths of a percent
        [Parameter] public string[] NIC = null;
        [Parameter] public string[] LegacyNIC = null;
        [Parameter] public string[] VHD = null;

        protected override void ProcessRecord()
        {
            // must use this to support processing record remotely.
            if (!string.IsNullOrEmpty(Remote))
            {
                ProcessRecordViaRest();
                return;
            }

            // Validate / normalize inputs...
            int MaxMem;
            int MaxProc;
            int MaxDynMem;

            // Presently, I assume that values over 1TB (1024 * 1024 MB) were really intended to be sizes in bytes.
            if (Memory > (1024*1024))
                Memory /= (1024*1024);
            if (DynamicMemoryLimit > (1024 * 1024))
                DynamicMemoryLimit /= (1024 * 1024);

            // I'm using the OS version right now because I don't have a better way to determine Hyper-V capabilities.
            var SysVer = System.Environment.OSVersion;
            if (SysVer.Version.Major < 6)
            {
                //Not running at least Vista/2008, no Hyper-V
                WriteWarning("This command only operates on Hyper-V on Windows Server 2008 and newer.");
                return;
            }
            switch (SysVer.Version.Minor)
            {
                case 0:  // Vista / Server 2008
                case 1:  // Win7 / Server 2008 R2
                    MaxMem = MaxDynMem = 64*1024; // 64 GB limit
                    MaxProc = 4;
                    break;
                case 2:  // Win8 / Server 2012
                    MaxMem = MaxDynMem = 1024*1024; // 1 TB limit
                    MaxProc = 64;
                    break;
                default:
                    WriteWarning("Unknown version of Windows.  No action taken.");
                    return;
            }
            // Common items
            // Only 8 Synthetic NICs allowed
            if (NIC.Length > 8) NIC = NIC.Take(8).ToArray();
            
            // Only 4 Legacy NICs allowed
            if (LegacyNIC.Length > 4) LegacyNIC = LegacyNIC.Take(4).ToArray();

            if (Memory > MaxMem) Memory = MaxMem;
            if (DynamicMemoryLimit > MaxDynMem) DynamicMemoryLimit = MaxDynMem;
            if (Processors > MaxProc) Processors = MaxProc;
            // if (DynamicMemoryLimit < 1) DynamicMemoryLimit = Memory;

            var VM = Utility.NewVM(Name);
            if (!VM.ModifyVMConfig(Processors, CPULimit, Memory, DynamicMemoryLimit, DynamicMemory))
            {
                VM.DestroyVM();
                WriteWarning("Error in VM creation.  See Hyper-V event log for details.");
                return;
            }

            string[] SCSI = new string[0];
            string[] IDE0 = new string[0];
            string[] IDE1 = new string[0];
            
            if (VHD.Length > 4)
            {
                SCSI = VHD.Skip(4).ToArray();
                VHD = VHD.Take(4).ToArray();
            }

            if (VHD.Length > 2)
            {
                IDE1 = VHD.Skip(2).ToArray();
                IDE0 = VHD.Take(2).ToArray();
            }
            else
            {
                IDE0 = VHD;
            }

            // Attach IDE drives first
            ManagementObject[] IDEcontrolers = VM.GetDevices()
                  .Where(device => device["ResourceSubType"].ToString().Equals(Utility.ResourceSubTypes.ControllerIDE)).ToArray();
            foreach (string drive in IDE0.Where(drive => !String.IsNullOrEmpty(drive)))
                if (!VM.AttachVHD(drive, IDEcontrolers[0])) WriteWarning("Failed to attach drive to IDE controller 0:  " + drive);
            foreach (string drive in IDE1.Where(drive => !String.IsNullOrEmpty(drive)))
                if (!VM.AttachVHD(drive, IDEcontrolers[1])) WriteWarning("Failed to attach drive to IDE controller 1:  " + drive);

            // Attach SCSI controllers and drives
            int numCtrl = (SCSI.Length % 64 > 0) ? 1 : 0;
            numCtrl += SCSI.Length / 64;
            numCtrl = Math.Min(numCtrl, 4);  // maximum of 4 SCSI controllers allowed in Hyper-V
            for (int i = 0; i < numCtrl; i++)
            {
                var SCSIctrl = Utility.NewResource(Utility.ResourceTypes.ParallelSCSIHBA, Utility.ResourceSubTypes.ControllerSCSI,
                                    VM.GetScope());
                if (!VM.AddDevice(SCSIctrl))
                {
                    WriteWarning("Failed to add SCSI controller ("+i+")");
                    continue;
                }
                int num = Math.Min(SCSI.Length, 64);
                var tmp = SCSI.Take(num);
                SCSI = SCSI.Skip(num).ToArray();
                foreach (string drive in tmp)
                {
                    if (!VM.AttachVHD(drive, SCSIctrl))
                        WriteWarning("Failed to attach drive to SCSI controller ("+i+"):  "+drive);
                }
            }

            // Add NICs
            foreach (string nic in NIC)
            {
                if (!VM.AddNIC(nic))
                    WriteWarning("Failed to add Synthetic NIC: "+nic);
            }
            foreach (string nic in LegacyNIC)
            {    
                if(!VM.AddNIC(nic, true))
                    WriteWarning("Failed to add Legacy NIC: " + nic);
            }

            WriteObject(VM);
        }
    }


}
