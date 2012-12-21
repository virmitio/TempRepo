using System;
using System.Collections.Generic;
using System.Linq;
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
        [Parameter][ValidateRange(1, int.MaxValue)]public int DynamicMemoryLimit = 0;
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

            // Validate inputs...
        }
    }
}
