using Azure.Messaging.ServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XOI_Integration.XOiRepository.XOiDataModels
{
    public class XOiJobInfo
    {
        public string CustomerName { get; set; }
        public string OrderNumber { get; set; }
        public List<string> AssigneeIds { get; set; }  
    }
}
