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
            // ======================================================
            // CREATE JOB — includes contributeToJob + shareLink
            // ======================================================
            {
                OperationType.Create,
@"
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

      deepLinks {
        visionMobile {
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

    additionalActionsResults {
      createPublicShare {
        shareLink
      }
    }
  }
}
"
            },

            // ======================================================
            // UPDATE JOB — include deepLinks (support contributeToJob)
            // ======================================================
            {
                OperationType.Update,
@"
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
        visionMobile {
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
}
"
            },

            // ======================================================
            // GET JOB — includes contributeToJob
            // ======================================================
            {
                OperationType.GetJob,
@"
query GetJob($id: ID!) {
  getJob(input: { id: $id }) {
    job {
      id
      customerName
      jobLocation
      workOrderNumber
      assigneeIds

      deepLinks {
        visionMobile {
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
}
"
            },

            // ======================================================
            // GET JOB SUMMARY — used for Customer Assets + Booking Notes
            // ======================================================
            {
                OperationType.GetJobSummary,
@"
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
}
"
            }
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
            _log.LogInformation("Start create job");

            var response = await _xoiAPI.SendRequestAsync<XOiCRUDResponse>(
                requests[OperationType.Create],
                GetVariables(job));

            return XOiProcessResponse.BuildXOiToBookableResourceData(
                _log,
                OperationType.Create,
                response);
        }

        // ======================================================
        // UPDATE JOB
        // ======================================================
        public async Task<XOiToBookableResourceData> UpdateJobAsync(
            JobRelatedData job, string jobId)
        {
            _log.LogInformation("Start update job");

            var response = await _xoiAPI.SendRequestAsync<XOiCRUDResponse>(
                requests[OperationType.Update],
                GetVariables(job, jobId));

            return XOiProcessResponse.BuildXOiToBookableResourceData(
                _log,
                OperationType.Update,
                response);
        }

        // ======================================================
        // GET JOB INFO (used in webhook)
        // ======================================================
        public async Task<XOiJobInfo> GetJobAsync(string jobId)
        {
            _log.LogInformation("Get Job info from XOi");

            var response = await _xoiAPI.SendRequestAsync<XOiCRUDResponse>(
                requests[OperationType.GetJob],
                new { id = jobId });

            return XOiProcessResponse.BuildXOiJobInfoData(_log, response);
        }

        // ======================================================
        // GET JOB SUMMARY → Customer Assets
        // ======================================================
        public async Task<List<XOiToCustomerAssetData>> GetJobSummaryAsync(
            string jobId, string workflowJobId)
        {
            var response = await GetJobSummaryResponseAsync(jobId, workflowJobId);

            return XOiProcessResponse.BuildXOiToCustomerAssetData(_log, response);
        }

        // ======================================================
        // GET JOB SUMMARY → Work Summary Notes
        // ======================================================
        public async Task<XOiWorkSummaryToBookableResourceData> GetJobSummaryWorkflowAsync(
            string jobId, string workflowJobId)
        {
            var response = await GetJobSummaryResponseAsync(jobId, workflowJobId);

            return XOiProcessResponse.BuildXOiWorkSummaryToBookableResourceData(
                _log, response, workflowJobId);
        }

        // ======================================================
        // Internal — Job Summary Cache
        // ======================================================
        private async Task<GraphQLResponse<XOiJobSummaryResponse>> GetJobSummaryResponseAsync(
            string jobId, string workflowJobId)
        {
            // Cache key must include workflowJobId — different workflows on same job return different assignees
            string cacheKey = $"{jobId}_{workflowJobId}";
            if (jobSummaryCache.ContainsKey(cacheKey))
                return jobSummaryCache[cacheKey];

            var response = await _xoiAPI.SendRequestAsync<XOiJobSummaryResponse>(
                requests[OperationType.GetJobSummary],
                new { id = jobId, workflowId = workflowJobId });

            if (response.Data != null)
            {
                jobSummaryCache[cacheKey] = response;
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
    }
}
