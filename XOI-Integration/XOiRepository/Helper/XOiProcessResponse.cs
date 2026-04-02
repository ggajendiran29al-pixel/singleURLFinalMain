using GraphQL;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using XOI_Integration.DataModels.Enums;
using XOI_Integration.XOiRepository.XOiDataModels;

namespace XOI_Integration.XOiRepository.Helper
{
    public static class XOiProcessResponse
    {
        // ---------------------------------------------------------
        // BUILD BOOKABLE RESOURCE DATA (USED IN CREATE & UPDATE)
        // ---------------------------------------------------------
        public static XOiToBookableResourceData BuildXOiToBookableResourceData(
            ILogger log,
            OperationType operationType,
            GraphQLResponse<XOiCRUDResponse> response)
        {
            var result = new XOiToBookableResourceData
            {
                operationType = operationType
            };

            try
            {
                if (response == null)
                {
                    result.jobResponseResult = JobResponseResult.Failure;
                    result.Message = "Null response.";
                    return result;
                }

                if (response.Errors != null && response.Errors.Any())
                {
                    string err = response.Errors.First().Message;
                    result.jobResponseResult = JobResponseResult.Failure;
                    result.Message = err;
                    return result;
                }

                var node = operationType switch
                {
                    OperationType.Create => response.Data?.CreateJob,
                    OperationType.Update => response.Data?.UpdateJob,
                    _ => null
                };

                if (node == null)
                {
                    result.jobResponseResult = JobResponseResult.Failure;
                    result.Message = "Missing job data.";
                    return result;
                }
                var job = node.Job;
                if (job == null)
                {
                    log.LogError("XOi API returned null job object.");

                    result.jobResponseResult = JobResponseResult.Failure;
                    result.Message = "Job block missing from API response.";

                    return result;
                }

                // SUCCESS
                result.jobResponseResult = JobResponseResult.Success;
                result.Message = "OK";

                // JOB ID
                if (string.IsNullOrEmpty(job.Id))
                {
                    log.LogError("XOi API returned job without ID.");

                    result.jobResponseResult = JobResponseResult.Failure;
                    result.Message = "XOi Vision Job ID was not returned by API.";

                    return result;
                }

                result.XOiVisionJobId = job.Id;

                // WEB VIEW URL
                result.XoiVisionWebURL = job.DeepLinks?.VisionWeb?.ViewJob?.Url;

                // PUBLIC SHARE URL
                result.XoiVisionJobShareURL = node.AdditionalActionsResults?.CreatePublicShare?.ShareLink;

                // CONTRIBUTION URL (CORRECT WORKING VERSION)
                result.ContributeToJobUrl =
    job.DeepLinks?.VisionMobile?.ContributeToJob?.Url
    ?? job.DeepLinks?.ContributeToJob?.Url;
                //result.ContributeToJobUrl = job.DeepLinks?.ContributeToJob?.Url;

                // MOBILE EDIT JOB URL
                result.XoiVisionJobURL = job.DeepLinks?.VisionMobile?.EditJob?.Url;

                return result;
            }
            catch (Exception ex)
            {
                result.jobResponseResult = JobResponseResult.Failure;
                result.Message = ex.Message;
                return result;
            }
        }

        // ---------------------------------------------------------
        // BUILD JOB INFO
        // ---------------------------------------------------------
        public static XOiJobInfo BuildXOiJobInfoData(
            ILogger log,
            GraphQLResponse<XOiCRUDResponse> response)
        {
            var result = new XOiJobInfo();

            try
            {
                var job = response.Data?.GetJob?.Job;
                if (job == null)
                    return result;

                result.CustomerName = job.CustomerName;
                result.OrderNumber = job.WorkOrderNumber;
                result.AssigneeIds = job.AssigneeIds;

                return result;
            }
            catch
            {
                return result;
            }
        }

        // ---------------------------------------------------------
        // BUILD CUSTOMER ASSET DATA
        // ---------------------------------------------------------
        public static List<XOiToCustomerAssetData> BuildXOiToCustomerAssetData(
            ILogger log,
            GraphQLResponse<XOiJobSummaryResponse> response)
        {
            var list = new List<XOiToCustomerAssetData>();

            try
            {
                var summary = response.Data?.GetJobSummary?.JobSummary;
                if (summary?.Documentation == null)
                    return list;

                foreach (var doc in summary.Documentation)
                {
                    list.Add(new XOiToCustomerAssetData
                    {
                        Make = doc.DerivedData?.Make,
                        Model = doc.DerivedData?.Model,
                        Serial = doc.DerivedData?.Serial,
                        Transcript = doc.DerivedData?.Transcript
                    });
                }

                return list;
            }
            catch
            {
                return list;
            }
        }

        // ---------------------------------------------------------
        // BUILD WORK SUMMARY NOTE
        // ---------------------------------------------------------
        public static XOiWorkSummaryToBookableResourceData BuildXOiWorkSummaryToBookableResourceData(
            ILogger log,
            GraphQLResponse<XOiJobSummaryResponse> response,
            string workflowJobId)
        {
            try
            {
                var summary = response.Data?.GetJobSummary?.JobSummary;
                if (summary?.Documentation == null)
                    return null;

                var doc = summary.Documentation.FirstOrDefault();
                var assignee = summary.Assignees?.FirstOrDefault();

                return new XOiWorkSummaryToBookableResourceData
                {
                    WorkflowName = doc?.WorkflowName,
                    CompleteDate = DateTime.Now.ToString("MM/dd/yyyy"),
                    WorkSummary = doc?.WorkSummary?.SummaryText ?? "WO Summary is empty",
                    WorkflowId = workflowJobId,
                    UserInitial = $"{assignee?.GivenName} {assignee?.FamilyName}"
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
