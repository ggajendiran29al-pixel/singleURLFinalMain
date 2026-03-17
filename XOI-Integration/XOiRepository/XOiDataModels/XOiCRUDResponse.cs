using System;
using System.Collections.Generic;

namespace XOI_Integration.XOiRepository.XOiDataModels
{
    public class XOiCRUDResponse
    {
        public CreateJob CreateJob { get; set; }
        public UpdateJob UpdateJob { get; set; }
        public GetJob GetJob { get; set; }
    }

    public class UpdateJob : CreateJob { }
    public class GetJob : CreateJob { }

    // ContributeToJob (TOP-LEVEL under deepLinks)
    public class ContributeToJob
    {
        public string Url { get; set; }
    }

    public class AdditionalActionsResults
    {
        public CreatePublicShare CreatePublicShare { get; set; }
    }

    public class CreateJob
    {
        public Job Job { get; set; }
        public AdditionalActionsResults AdditionalActionsResults { get; set; }
    }

    public class CreatePublicShare
    {
        public string ShareLink { get; set; }
    }

    // ------------------------------------------
    // CORRECT DEEPLINKS STRUCTURE (WORKING VERSION)
    // ------------------------------------------
    public class DeepLinks
    {
        public ContributeToJob ContributeToJob { get; set; }     // MUST BE HERE (not inside visionMobile)
        public VisionMobile VisionMobile { get; set; }
        public VisionWeb VisionWeb { get; set; }
    }

    public class VisionMobile
    {
        public EditJob EditJob { get; set; }
        public ViewJob ViewJob { get; set; }
        public JobLocationActivitySearch JobLocationActivitySearch { get; set; }
    }

    public class VisionWeb
    {
        public ViewJob ViewJob { get; set; }
    }

    public class EditJob
    {
        public string Url { get; set; }
    }

    public class ViewJob
    {
        public string Url { get; set; }
    }

    public class JobLocationActivitySearch
    {
        public string Url { get; set; }
    }

    // Main Job Record
    public class Job
    {
        public string Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; }
        public List<string> AssigneeIds { get; set; }
        public string CustomerName { get; set; }
        public string JobLocation { get; set; }
        public string WorkOrderNumber { get; set; }
        public string Label { get; set; }
        public List<string> Tags { get; set; }
        public List<string> TagSuggestions { get; set; }
        public DeepLinks DeepLinks { get; set; }
    }
}
