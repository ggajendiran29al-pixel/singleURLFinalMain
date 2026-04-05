using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XOI_Integration.XOiRepository.XOiDataModels
{
    public class XOiJobSummaryResponse
    {
        public GetJobSummary GetJobSummary { get; set; }
    }

    public class GetJobSummary
    {
        public string NextToken { get; set; }
        public JobSummary JobSummary { get; set; }
    }

    public class JobSummary
    {
        public string JobId { get; set; }
        public List<Documentation> Documentation { get; set; }
        public List<Assginees> Assignees { get; set; }
    }

    public class Assginees
    {
        public string Id { get; set; }
        public string Email { get; set; }
        [JsonProperty("given_name")]
        public string GivenName { get; set; }
        [JsonProperty("family_name")]
        public string FamilyName { get; set; }
    }

    public class Documentation
    {
        public string WorkflowName { get; set; }
        public List<string> Traits { get; set; }
        public List<object> Tags { get; set; }
        public Note Note { get; set; }
        public Choice Choice { get; set; }
        public DerivedData DerivedData { get; set; }
        public WorkSummary WorkSummary { get; set; }
    }

    public class Note
    {
        public string Text { get; set; }
    }

    public class Choice
    {
        public List<string> Chosen { get; set; }
    }

    public class DerivedData
    {
        public string Make { get; set; }
        public string Model { get; set; }
        public string Serial { get; set; }
        public string Transcript { get; set; }

        [JsonProperty("manufacture_date")]
        public string ManufactureDate { get; set; }
    }

    public class WorkSummary
    {
        [JsonProperty("summary_text")]
        public string SummaryText { get; set; }
    }
}
