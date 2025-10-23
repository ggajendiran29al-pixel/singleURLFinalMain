using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XOI_Integration.XOiRepository.XOiDataModels
{
    public class XOiWebhookRequest
    {
        [JsonProperty("orgId")]
        public string OrgId { get; set; }

        [JsonProperty("event")]
        public string Event { get; set; }

        [JsonProperty("jobId")]
        public string JobId { get; set; }

        [JsonProperty("workflowJobId")]
        public string WorkflowJobId { get; set; }

        [JsonProperty("traits")]
        public string[] Traits { get; set; }

        [JsonProperty("firedAt")]
        public DateTime FiredAt { get; set; }
    }
}
