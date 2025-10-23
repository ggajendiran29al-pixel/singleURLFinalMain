using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XOI_Integration.DataModels;
using XOI_Integration.DataModels.Enums;

namespace XOI_Integration.XOiRepository.XOiDataModels
{
    public class XOiToBookableResourceData
    {
        public string XOiVisionJobId { get; set; }
        public string XoiVisionJobURL { get; set; }
        public string XoiVisionJobShareURL { get; set; }
        public string XoiVisionWebURL { get; set; } 
        public JobResponseResult jobResponseResult { get; set; }
        public OperationType operationType { get; set; }
        public string Message { get; set; }
    }
}
