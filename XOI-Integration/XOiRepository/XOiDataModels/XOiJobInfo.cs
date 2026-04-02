using System.Collections.Generic;

namespace XOI_Integration.XOiRepository.XOiDataModels
{
    /// <summary>
    /// Represents the top-level Job object returned from XOi,
    /// enriched with workflow information needed for correct note processing.
    /// </summary>
    public class XOiJobInfo
    {
        public string CustomerName { get; set; }
        public string OrderNumber { get; set; }
        public List<string> AssigneeIds { get; set; }

        /// <summary>
        /// TRUE job-level summary from XOi. May be empty.
        /// </summary>
        public XOiWorkSummaryToBookableResourceData WorkSummary { get; set; }

        /// <summary>
        /// WorkflowJobId sent by webhook (may be empty for normal job_update).
        /// This MUST exist for proper note creation logic.
        /// </summary>
        public string WorkflowJobId { get; set; }

        /// <summary>
        /// WorkflowId sent by webhook (tracks workflow type).
        /// </summary>
        public string WorkflowId { get; set; }

        /// <summary>
        /// Workflow name from XOi ("Inspection", "Repair", etc.)
        /// </summary>
        public string WorkflowName { get; set; }

        /// <summary>
        /// Used internally to flag whether WorkSummary was fetched from workflow fallback.
        /// Not sent by XOi but used in note logic.
        /// </summary>
        public bool IsWorkflowSummary { get; set; }
        //update
        public string SingleURL { get; set; }

        // booking URL fields
        public string VisionMobileUrl { get; set; }   // sisps_xoi_vision_joburl
        public string VisionWebUrl { get; set; }      // sisps_xoi_vision_webjoburl

    }
}
