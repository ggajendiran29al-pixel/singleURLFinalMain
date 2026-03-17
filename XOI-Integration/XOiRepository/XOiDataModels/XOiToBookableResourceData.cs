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
        public string XOiVisionJobId { get; set; }              // Raw job ID (cjob-xxxx)
        public string XoiVisionJobURL { get; set; }             // View Job URL (VisionWeb.ViewJob.Url)
        public string XoiVisionJobShareURL { get; set; }        // Public share URL
        public string XoiVisionWebURL { get; set; }             // View Job URL (fallback)
        public string ContributeToJobUrl { get; set; }          // The preferred URL

        public JobResponseResult jobResponseResult { get; set; }
        public OperationType operationType { get; set; }
        public string Message { get; set; }
    }
}
