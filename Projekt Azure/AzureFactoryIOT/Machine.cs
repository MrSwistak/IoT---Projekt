using AzureFactoryIOT.Enums;
using Microsoft.Azure.Devices.Client;
using Opc.UaFx.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureFactoryIOT
{
    public class Machine
    {

        public string? Id { get; set; }
        public readonly DeviceClient DeviceClient;
        public readonly OpcClient OpcClient;
        public string DeviceId { get; set; }
        public ProductionStatusEnum ProductionStatus { get; set; }
        public Guid WorkOrderId { get; set; }
        public int Rate { get; set; }
        public long CountGood { get; set; }
        public long CountBad { get; set; }
        public double Temperature { get; set; }
        public DeviceErrorEnum DeviceError { get; set; }


        public string ProductrionStatusNode
        {
            get
            {
                return $"{Id}/ProductionStatus";
            }
        }
        public string WorkOrderIdNode
        {
            get
            {
                return $"{Id}/WorkorderId";
            }
        }
        public string CountGoodNode
        {
            get
            {
                return $"{Id}/GoodCount";
            }
        }
        public string CountBadNode
        {
            get
            {
                return $"{Id}/BadCount";
            }
        }
        public string TemperatureNode
        {
            get
            {
                return $"{Id}/Temperature";
            }
        }
        public string ErrorNode
        {
            get
            {
                return $"{Id}/DeviceError";
            }
        }
        public string RateNode
        {
            get
            {
                return $"{Id}/ProductionRate";
            }
        }

        public string ResetErrorsNode
        {
            get
            {
                return $"{Id}/ResetErrorStatus";
            }
        }

        public string EmergencyStopNode
        {
            get
            {
                return $"{Id}/EmergencyStop";
            }
        }

        public Machine(string id, DeviceClient deviceClient, OpcClient client, string deviceId)
        {
            Id = id;
            DeviceClient = deviceClient;
            DeviceId = deviceId;
            OpcClient = client;
        }
    }
}
