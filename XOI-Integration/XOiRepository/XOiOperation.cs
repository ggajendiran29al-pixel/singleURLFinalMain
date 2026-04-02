using GraphQL;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XOI_Integration.DataFactory.BaseObject;
using XOI_Integration.DataModels.Enums;
using XOI_Integration.DataverseRepository.Operations;
using XOI_Integration.XOiRepository.Helper;
using XOI_Integration.XOiRepository.Provider;
using XOI_Integration.XOiRepository.XOiDataModels;

namespace XOI_Integration.XOiRepository
{
    public class XOiOperation
    {
        private readonly ILogger _log;
        private readonly XOiAPI _xoiAPI;

        private static readonly Dictionary<OperationType, string> requests =
            new Dictionary<OperationType, string>()
            {
                // CREATE JOB
{ OperationType.Create, @"
mutation CreateJob(
  $assigneeIds: [ID!]!,
  $customerName: String!,
  $jobLocation: String!,
  $workOrderNumber: String!,
  $label: String,
  $tags: [String!],
  $tagSuggestions: [String!],
  $internalNoteText: String!
) {
  createJob(
    input: {
      newJob: {
        assigneeIds: $assigneeIds
        customerName: $customerName
        jobLocation: $jobLocation
        workOrderNumber: $workOrderNumber
        label: $label
        tags: $tags
        tagSuggestions: $tagSuggestions
        internalNote: { text: $internalNoteText }
      }
      additionalActions: { createPublicShare: { enabled: true } }
    }
  ) {
    job {
      id
      createdAt
      assigneeIds
      customerName
      jobLocation
      workOrderNumber
      label
    }
    additionalActionsResults {
      createPublicShare {
        shareLink
      }
    }
  }
}"},
                // UPDATE JOB
               { OperationType.Update, @"
mutation UpdateJob(
  $id: ID!,
  $customerName: String!,
  $jobLocation: String!,
  $workOrderNumber: String!,
  $label: String,
  $tags: [String!],
  $tagSuggestions: [String!],
  $internalNoteText: String!,
  $assigneeIds: [ID!]!
) {
  updateJob(
    input: {
      id: $id
      fieldUpdates: {
        customerName: $customerName
        jobLocation: $jobLocation
        workOrderNumber: $workOrderNumber
        label: $label
        tags: $tags
        tagSuggestions: $tagSuggestions
        internalNote: { text: $internalNoteText }
        assigneeIds: $assigneeIds
      }
    }
  ) {
    job {
      id
      customerName
      workOrderNumber
      jobLocation
    }
  }
}"},
                // GET JOB
                { OperationType.GetJob, @"
query GetJob($id: ID!) {
  getJob(input: { id: $id }) {
    job {
      id
      customerName
      jobLocation
      workOrderNumber
      assigneeIds
      deepLinks {
        ContributeToJob { Url }
        VisionMobile { ContributeToJob { Url } ViewJob { Url } EditJob { Url } }
      }
    }
  }
}"},
                // GET JOB SUMMARY
                { OperationType.GetJobSummary, @"
query GetJobSummary($id: ID!, $workflowId: ID) {
  getJobSummary(input: { jobId: $id, workflowJobId: $workflowId }) {
    nextToken
    jobSummary {
      jobId
      documentation {
        workflowName
        traits
        tags
        note { text }
        choice { chosen }
        derivedData { make model serial transcript manufacture_date }
        workSummary { summary_text }
      }
      assignees { id email given_name family_name }
    }
  }
}"},
            };

        private readonly Dictionary<string, GraphQLResponse<XOiJobSummaryResponse>> jobSummaryCache =
            new Dictionary<string, GraphQLResponse<XOiJobSummaryResponse>>();

        public XOiOperation(ILogger log)
        {
            _log = log;
            _xoiAPI = new XOiAPI();
        }

        // ======================================================
        // CREATE JOB
        // ======================================================
        public async Task<XOiToBookableResourceData> CreateJobAsync(JobRelatedData job)
        {
            _log?.LogInformation("Start create job");
            // 1️⃣ Create the job (minimal mutation)

            var response = await _xoiAPI.SendRequestAsync<XOiCRUDResponse>(
                requests[OperationType.Create],
                GetVariables(job));

            LogResponse("CREATE", response);

            var result = XOiProcessResponse.BuildXOiToBookableResourceData(_log, OperationType.Create, response);

            if (result != null)
            {
                var jobData = response?.Data?.CreateJob?.Job;

                // Extract URLs
                result.ContributeToJobUrl = result.ContributeToJobUrl ?? ExtractSingleURL(jobData); // VisionMobile fallback
                result.VisionMobileJobUrl = ExtractSingleURL(jobData); // sisps_xoi_vision_joburl
                result.VisionWebJobUrl = ExtractVisionWebURL(jobData);  // sisps_xoi_vision_webjoburl
            }

            return result;
        }

        // ======================================================
        // UPDATE JOB
        // ======================================================
        public async Task<XOiToBookableResourceData> UpdateJobAsync(JobRelatedData job, string jobId)
        {
            _log?.LogInformation("Start update job");
            // 1️⃣ Update the job
            var response = await _xoiAPI.SendRequestAsync<XOiCRUDResponse>(
                requests[OperationType.Update],
                GetVariables(job, jobId));

            LogResponse("UPDATE", response);

            var result = XOiProcessResponse.BuildXOiToBookableResourceData(_log, OperationType.Update, response);

            // Merge SingleURL safely
            if (result != null)
            {
                var jobData = response?.Data?.UpdateJob?.Job;

                // Extract URLs
                result.ContributeToJobUrl = result.ContributeToJobUrl ?? ExtractSingleURL(jobData); // VisionMobile fallback
                result.VisionMobileJobUrl = ExtractSingleURL(jobData); // sisps_xoi_vision_joburl
                result.VisionWebJobUrl = ExtractVisionWebURL(jobData);  // sisps_xoi_vision_webjoburl
            }


            return result;
        }

        // ======================================================
        // GET JOB INFO
        // ======================================================
        public async Task<XOiJobInfo> GetJobAsync(string jobId)
        {
            _log?.LogInformation("Get Job info from XOi");

            var response = await _xoiAPI.SendRequestAsync<XOiCRUDResponse>(
                requests[OperationType.GetJob],
                new { id = jobId });

            LogResponse("GET JOB", response);

            var result = XOiProcessResponse.BuildXOiJobInfoData(_log, response);

            var extractedUrl = ExtractSingleURL(response?.Data?.GetJob?.Job);
            if (!string.IsNullOrEmpty(extractedUrl))
                result.SingleURL = extractedUrl;
            if (result != null)
            {
                // Extract VisionMobile URL (previous SingleURL)
                result.VisionMobileUrl = ExtractSingleURL(response?.Data?.GetJob?.Job);

                // Extract VisionWeb URL
                result.VisionWebUrl = ExtractVisionWebURL(response?.Data?.GetJob?.Job);
            }
            return result;
        }
        private string ExtractVisionWebURL(dynamic job)
        {
            try
            {
                return job?.DeepLinks?.VisionWeb?.ViewJob?.Url;
            }
            catch
            {
                return null;
            }
        }

        // ======================================================
        // JOB SUMMARY
        // ======================================================
        public async Task<List<XOiToCustomerAssetData>> GetJobSummaryAsync(string jobId, string workflowJobId)
        {
            var response = await GetJobSummaryResponseAsync(jobId, workflowJobId);
            return XOiProcessResponse.BuildXOiToCustomerAssetData(_log, response);
        }

        public async Task<XOiWorkSummaryToBookableResourceData> GetJobSummaryWorkflowAsync(string jobId, string workflowJobId)
        {
            var response = await GetJobSummaryResponseAsync(jobId, workflowJobId);
            return XOiProcessResponse.BuildXOiWorkSummaryToBookableResourceData(_log, response, workflowJobId);
        }

        private async Task<GraphQLResponse<XOiJobSummaryResponse>> GetJobSummaryResponseAsync(string jobId, string workflowJobId)
        {
            if (jobSummaryCache.ContainsKey(jobId))
                return jobSummaryCache[jobId];

            var response = await _xoiAPI.SendRequestAsync<XOiJobSummaryResponse>(
                requests[OperationType.GetJobSummary],
                new { id = jobId, workflowId = workflowJobId });

            if (response.Data != null)
            {
                jobSummaryCache[jobId] = response;
                return response;
            }

            if (response.Errors != null && response.Errors.Any())
                throw new Exception(response.Errors.First().Message);

            throw new Exception("Invalid response received.");
        }

        private dynamic GetVariables(JobRelatedData job, string jobId = null)
        {
            return new
            {
                id = jobId,
                assigneeIds = job.AssigneeIds,
                customerName = job.CustomerName,
                jobLocation = job.JobLocation,
                workOrderNumber = job.OrderNumber,
                label = job.Label,
                tags = job.Tags,
                tagSuggestions = job.TagSuggestions,
                internalNoteText = job.InternalNote
            };
        }

        private string ExtractSingleURL(dynamic job)
        {
            if (job == null || job.DeepLinks == null)
                return null;

            try
            {
                // Primary
                if (job.DeepLinks.ContributeToJob != null)
                    return job.DeepLinks.ContributeToJob.Url;

                // VisionMobile fallback
                if (job.DeepLinks.VisionMobile != null)
                {
                    var vm = job.DeepLinks.VisionMobile;
                    var contribute = vm.GetType().GetProperty("ContributeToJob")?.GetValue(vm);
                    var urlProp = contribute?.GetType().GetProperty("Url")?.GetValue(contribute)?.ToString();
                    if (!string.IsNullOrEmpty(urlProp)) return urlProp;

                    if (vm.ViewJob?.Url != null) return vm.ViewJob.Url;
                    if (vm.EditJob?.Url != null) return vm.EditJob.Url;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private void LogResponse<T>(string operation, GraphQLResponse<T> response)
        {
            if (_log == null) return;
            try
            {
                var serialized = Newtonsoft.Json.JsonConvert.SerializeObject(response);
                _log.LogInformation($"{operation} API Response: {serialized}");
            }
            catch
            {
                _log.LogWarning($"{operation} API Response could not be serialized");
            }
        }
    }
}/*using Google.Protobuf.WellKnownTypes;
using GraphQL;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Threading.Tasks;
using XOI_Integration.DataFactory.BaseObject;
using XOI_Integration.DataModels.Enums;
using XOI_Integration.DataverseRepository.Operations;
using XOI_Integration.XOiRepository.Helper;
using XOI_Integration.XOiRepository.Provider;
using XOI_Integration.XOiRepository.XOiDataModels;

namespace XOI_Integration.XOiRepository
{
    public class XOiOperation
    {
        private readonly ILogger _log;
        private readonly XOiAPI _xoiAPI;

        private static readonly Dictionary<OperationType, string> requests =
            new Dictionary<OperationType, string>()
        {
            
// CREATE JOB
{ OperationType.Create, @"
mutation CreateJob(
  $assigneeIds: [ID!]!,
  $customerName: String!,
  $jobLocation: String!,
  $workOrderNumber: String!,
  $label: String,
  $tags: [String!],
  $tagSuggestions: [String!],
  $internalNoteText: String!
) {
  createJob(
    input: {
      newJob: {
        assigneeIds: $assigneeIds
        customerName: $customerName
        jobLocation: $jobLocation
        workOrderNumber: $workOrderNumber
        label: $label
        tags: $tags
        tagSuggestions: $tagSuggestions
        internalNote: { text: $internalNoteText }
      }
      additionalActions: { createPublicShare: { enabled: true }
}
    }
  ) {
    job {
        id
        createdAt
      assigneeIds
      customerName
      jobLocation
      workOrderNumber
      label
      deepLinks {
            visionWeb {
                viewJob { url }
            }
        }
    }
    additionalActionsResults {
        createPublicShare {
            shareLink
        }
    }
}
}"},
          // UPDATE JOB
{ OperationType.Update, @"
mutation UpdateJob(
  $id: ID!,
  $customerName: String!,
  $jobLocation: String!,
  $workOrderNumber: String!,
  $label: String,
  $tags: [String!],
  $tagSuggestions: [String!],
  $internalNoteText: String!,
  $assigneeIds: [ID!]!
) {
  updateJob(
    input: {
      id: $id
      fieldUpdates: {
        customerName: $customerName
        jobLocation: $jobLocation
        workOrderNumber: $workOrderNumber
        label: $label
        tags: $tags
        tagSuggestions: $tagSuggestions
        internalNote: { text: $internalNoteText }
        assigneeIds: $assigneeIds
      }
    }
  ) {
    job {
      id
      customerName
      workOrderNumber
      jobLocation
      deepLinks {
        visionWeb {
          viewJob { url }
        }
      }
    }
  }
}"},
            // GET JOB
            { OperationType.GetJob, @"
query GetJob($id: ID!) {
  getJob(input: { id: $id }) {
    job {
      id
      customerName
      jobLocation
      workOrderNumber
      assigneeIds
      deepLinks {
        VisionMobile {
          contributeToJob { url }
          editJob { url }
          viewJob { url }
          jobLocationActivitySearch { url }
        }
        visionWeb {
          viewJob { url }
        }
      }
    }
  }
}"},
            // GET JOB SUMMARY
            { OperationType.GetJobSummary, @"
query GetJobSummary($id: ID!, $workflowId: ID) {
  getJobSummary(input: { jobId: $id, workflowJobId: $workflowId }) {
    nextToken
    jobSummary {
      jobId
      documentation {
        workflowName
        traits
        tags
        note { text }
        choice { chosen }
        derivedData {
          make
          model
          serial
          transcript
          manufacture_date
        }
        workSummary {
          summary_text
        }
      }
      assignees {
        id
        email
        given_name
        family_name
      }
    }
  }
}"},
        };

        private readonly Dictionary<string, GraphQLResponse<XOiJobSummaryResponse>> jobSummaryCache =
            new Dictionary<string, GraphQLResponse<XOiJobSummaryResponse>>();

        public XOiOperation(ILogger log)
        {
            _log = log;
            _xoiAPI = new XOiAPI();
        }

        // ======================================================
        // CREATE JOB
        // ======================================================
        public async Task<XOiToBookableResourceData> CreateJobAsync(JobRelatedData job)
        {
            _log?.LogInformation("Start create job");

            var response = await _xoiAPI.SendRequestAsync<XOiCRUDResponse>(
                requests[OperationType.Create],
                GetVariables(job));

            LogResponse("CREATE", response);

            var result = XOiProcessResponse.BuildXOiToBookableResourceData(_log, OperationType.Create, response);

            // Merge SingleURL safely
            if (result != null)
            {
                result.ContributeToJobUrl = result.ContributeToJobUrl ?? ExtractSingleURL(response?.Data?.CreateJob?.Job);
            }

            return result;
        }

        // ======================================================
        // UPDATE JOB
        // ======================================================
        public async Task<XOiToBookableResourceData> UpdateJobAsync(JobRelatedData job, string jobId)
        {
            _log?.LogInformation("Start update job");

            var response = await _xoiAPI.SendRequestAsync<XOiCRUDResponse>(
                requests[OperationType.Update],
                GetVariables(job, jobId));

            LogResponse("UPDATE", response);

            var result = XOiProcessResponse.BuildXOiToBookableResourceData(_log, OperationType.Update, response);

            // Merge SingleURL safely
            if (result != null)
            {
                result.ContributeToJobUrl = result.ContributeToJobUrl ?? ExtractSingleURL(response?.Data?.UpdateJob?.Job);
            }

            return result;
        }

        // ======================================================
        // GET JOB INFO
        // ======================================================
        public async Task<XOiJobInfo> GetJobAsync(string jobId)
        {
            _log?.LogInformation("Get Job info from XOi");

            var response = await _xoiAPI.SendRequestAsync<XOiCRUDResponse>(
                requests[OperationType.GetJob],
                new { id = jobId });

            LogResponse("GET JOB", response);

            var result = XOiProcessResponse.BuildXOiJobInfoData(_log, response);

            // Merge SingleURL safely
            var extractedUrl = ExtractSingleURL(response?.Data?.GetJob?.Job);
            if (!string.IsNullOrEmpty(extractedUrl))
                result.SingleURL = extractedUrl;

            return result;
        }

        // ======================================================
        // GET JOB SUMMARY → Customer Assets
        // ======================================================
        public async Task<List<XOiToCustomerAssetData>> GetJobSummaryAsync(string jobId, string workflowJobId)
        {
            var response = await GetJobSummaryResponseAsync(jobId, workflowJobId);
            return XOiProcessResponse.BuildXOiToCustomerAssetData(_log, response);
        }

        // ======================================================
        // GET JOB SUMMARY → Work Summary Notes
        // ======================================================
        public async Task<XOiWorkSummaryToBookableResourceData> GetJobSummaryWorkflowAsync(string jobId, string workflowJobId)
        {
            var response = await GetJobSummaryResponseAsync(jobId, workflowJobId);
            return XOiProcessResponse.BuildXOiWorkSummaryToBookableResourceData(_log, response, workflowJobId);
        }

        // ======================================================
        // Internal — Job Summary Cache
        // ======================================================
        private async Task<GraphQLResponse<XOiJobSummaryResponse>> GetJobSummaryResponseAsync(string jobId, string workflowJobId)
        {
            if (jobSummaryCache.ContainsKey(jobId))
                return jobSummaryCache[jobId];

            var response = await _xoiAPI.SendRequestAsync<XOiJobSummaryResponse>(
                requests[OperationType.GetJobSummary],
                new { id = jobId, workflowId = workflowJobId });

            if (response.Data != null)
            {
                jobSummaryCache[jobId] = response;
                return response;
            }

            if (response.Errors != null && response.Errors.Any())
                throw new Exception(response.Errors.First().Message);

            throw new Exception("Invalid response received.");
        }

        // ======================================================
        // BUILD VARIABLES FOR GRAPHQL MUTATIONS
        // ======================================================
        private dynamic GetVariables(JobRelatedData job, string jobId = null)
        {
            return new
            {
                id = jobId,
                assigneeIds = job.AssigneeIds,
                customerName = job.CustomerName,
                jobLocation = job.JobLocation,
                workOrderNumber = job.OrderNumber,
                label = job.Label,
                tags = job.Tags,
                tagSuggestions = job.TagSuggestions,
                internalNoteText = job.InternalNote
            };
        }

        // ======================================================
        // Single URL extraction helper
        // ======================================================
        private string ExtractSingleURL(dynamic job)
        {
            if (job?.DeepLinks?.VisionWeb?.ViewJob?.Url != null)
                return job.DeepLinks.VisionWeb.ViewJob.Url;

            return null;
        }

        // ======================================================
        // Logger helper for dynamic safe logging
        // ======================================================
        private void LogResponse<T>(string operation, GraphQLResponse<T> response)
        {
            if (_log == null) return;
            try
            {
                var serialized = Newtonsoft.Json.JsonConvert.SerializeObject(response);
                _log.LogInformation($"{operation} API Response: {serialized}");
            }
            catch
            {
                _log.LogWarning($"{operation} API Response could not be serialized");
            }
        }
    }
}
*/