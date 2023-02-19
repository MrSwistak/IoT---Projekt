using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureFactoryIOT
{
    public class LineSettings
    {
        public string? DeviceNameRegex { get; set; }
        public string? MachineNodeIdRegex { get; set; }
        public int ReadingValuesDelay { get; set; }
        public int DeviceMaxCount { get; set; }
        public string? OpcUAServer { get; set; }
        public Dictionary<string, string>? ConnectionStrings { get; set; }

    }
}
