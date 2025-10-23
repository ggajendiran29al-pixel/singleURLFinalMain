using Google.Protobuf.WellKnownTypes;
using GraphQL;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        private static readonly Dictionary<OperationType, string> requests = new Dictionary<OperationType, string>()
        {
            {OperationType.Create, @"mutation CreateJob(
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
                  createdBy
                  assigneeIds
                  customerName
                  jobLocation
                  workOrderNumber
                  label
                  tags
                  tagSuggestions
                  deepLinks {
                     visionWeb {
                      viewJob {
                        url
                      }
                    }
                    visionMobile {
                      editJob {
                        url
                      }
                      jobLocationActivitySearch {
                        url
                      }
                    }
                  }
                }
                additionalActionsResults {
                  createPublicShare {
                    shareLink
                  }
                }
              }
            }" },
            {OperationType.Update, @"mutation UpdateJob(
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
                  createdAt
                  createdBy
                  assigneeIds
                  customerName
                  jobLocation
                  workOrderNumber
                  label
                  tags
                  tagSuggestions
                  internalNote {
                    text
                  }
                  deepLinks {
                    visionWeb {
                      viewJob {
                        url
                      }
                    }
                    visionMobile {
                      viewJob {
                        url
                      }
                      editJob {
                        url
                      }
                      jobLocationActivitySearch {
                        url
                      }
                    }
                  }
                }
              }
            }" },
            {OperationType.GetJobSummary, @"query GetJobSummary(
              $id: ID!, $workflowId: ID) {
              getJobSummary(input: { jobId: $id, workflowJobId: $workflowId }) {
                nextToken
                jobSummary {
                  jobId
                  documentation {
                    workflowName
                    traits
                    tags
                    note {
                      text
                    }
                    choice {
                      chosen
                    }
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
            }" },
            {OperationType.GetJob, @"query GetJob(
                $id: ID!) {
                getJob(input: { id: $id }) {
                  job {
                    id
                    createdAt
                    createdBy
                    assigneeIds
                    customerName
                    jobLocation
                    workOrderNumber
                    label
                    tags
                    tagSuggestions
                    deepLinks {
                      visionWeb {
                        viewJob {
                          url
                        }
                      }
                      visionMobile {
                        viewJob {
                          url
                        }
                        editJob {
                          url
                        }
                        jobLocationActivitySearch {
                          url
                        }
                      }
                    }
                  }
                }
              }" }
        };

        private Dictionary<string, GraphQLResponse<XOiJobSummaryResponse>> jobSummaryCache = new Dictionary<string, GraphQLResponse<XOiJobSummaryResponse>>();

        private ILogger _log;
        private readonly XOiAPI _xoiAPI;

        public XOiOperation(ILogger log) 
        { 
            _log = log;
            _xoiAPI = new XOiAPI();
        }

        public async Task<XOiToBookableResourceData> CreateJobAsync(JobRelatedData jobRelatedData)
        {
            _log.LogInformation("Start create job");

            var query = requests[OperationType.Create];
            var variables = GetVariables(jobRelatedData);

            var response = await _xoiAPI.SendRequestAsync<XOiCRUDResponse>(query, variables);

            _log.LogInformation("Job created");

            return XOiProcessResponse.BuildXOiToBookableResourceData(_log, OperationType.Create, response);
        }

        public async Task<XOiToBookableResourceData> UpdateJobAsync(JobRelatedData jobRelatedData, string jobId)
        {
            _log.LogInformation("Start update job");

            var query = requests[OperationType.Update];
            var variables = GetVariables(jobRelatedData, jobId);

            var response = await _xoiAPI.SendRequestAsync<XOiCRUDResponse>(query, variables);

            _log.LogInformation("Job updated");

            return XOiProcessResponse.BuildXOiToBookableResourceData(_log, OperationType.Update, response);
        }

        public async Task<XOiJobInfo> GetJobAsync(string jobId)
        {
            _log.LogInformation("Get Job info from XOi");

            var query = requests[OperationType.GetJob];
            var variables = new
            {
                id = jobId
            };

            var response = await _xoiAPI.SendRequestAsync<XOiCRUDResponse>(query, variables);
           

            return XOiProcessResponse.BuildXOiJobInfoData(_log, response);
        }

        public async Task<List<XOiToCustomerAssetData>> GetJobSummaryAsync(string jobId, string workflowJobId)
        {
            var response = await GetJobSummaryResponseAsync(jobId, workflowJobId);

            return XOiProcessResponse.BuildXOiToCustomerAssetData(_log,response);
        }

        public async Task<XOiWorkSummaryToBookableResourceData> GetJobSummaryWorkflowAsync(string jobId, string workflowJobId)
        {
            var response = await GetJobSummaryResponseAsync(jobId, workflowJobId);

            return XOiProcessResponse.BuildXOiWorkSummaryToBookableResourceData(_log, response, workflowJobId);
        }


        private async Task<GraphQLResponse<XOiJobSummaryResponse>> GetJobSummaryResponseAsync(string jobId, string workflowJobId)
        {
            _log.LogInformation("Start receiving a job summary");

            if (jobSummaryCache.ContainsKey(jobId))
            {
                _log.LogInformation("Finish receiving a job summary");

                return jobSummaryCache[jobId];
            }

            var query = requests[OperationType.GetJobSummary];
            var variables = new
            {
                id = jobId,
                workflowId = workflowJobId
            };

            var response = await _xoiAPI.SendRequestAsync<XOiJobSummaryResponse>(query, variables);

            if (response.Data != null)
            {
                _log.LogInformation("Job summary successfully recieved");

                jobSummaryCache.Add(jobId, response);

                await IntegrationLogOperation.CreateJobSummaryLogAsync(result: JobResponseResult.Success, xoiJobSummaryResponse: response.Data, jobId: jobId);

                return response;
            }
            else if (response.Errors != null && response.Errors.Any()) 
            {
                var errorMessage = response.Errors.FirstOrDefault().Message;

                await IntegrationLogOperation.CreateJobSummaryLogAsync(result: JobResponseResult.Failure, message: errorMessage, jobId: jobId);

                throw new Exception(errorMessage);
            }

            _log.LogError("Invalid responce recived");

            throw new Exception("Invalid responce recived");
        }


        private dynamic GetVariables(JobRelatedData jobRelatedData, string jobId = null)
        {
            var variables = new
            {
                id = jobId,
                assigneeIds = jobRelatedData.AssigneeIds,
                customerName = jobRelatedData.CustomerName,
                jobLocation = jobRelatedData.JobLocation,
                workOrderNumber = jobRelatedData.OrderNumber,
                label = jobRelatedData.Label,
                tags = jobRelatedData.Tags,
                tagSuggestions = jobRelatedData.TagSuggestions,
                internalNoteText = jobRelatedData.InternalNote
            };

            return variables;
        }
    }
}
