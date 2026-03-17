using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XOI_Integration.XOiRepository.XOiDataModels
{
    public class XOiWorkSummaryToBookableResourceData
    {
        public string WorkflowName { get; set; }
        public string CompleteDate { get; set; }  
        public string WorkSummary { get; set; }
        public string WorkflowId { get; set; }
        public string UserInitial { get; set; }
        public Guid CustomerAssetId { get; set; }

        public bool IsFilled() 
        {
            return !String.IsNullOrEmpty(WorkflowName) || !String.IsNullOrEmpty(CompleteDate) || !String.IsNullOrEmpty(WorkSummary) || !String.IsNullOrEmpty(WorkflowId);
        }
    }
}
