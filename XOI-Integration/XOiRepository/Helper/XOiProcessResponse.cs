using GraphQL;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XOI_Integration.DataModels.Enums;
using XOI_Integration.XOiRepository.Provider;
using XOI_Integration.XOiRepository.XOiDataModels;

namespace XOI_Integration.XOiRepository.Helper
{
    public class XOiProcessResponse
    {
        public static XOiToBookableResourceData BuildXOiToBookableResourceData(ILogger _log, OperationType operationType, GraphQLResponse<XOiCRUDResponse> graphQlResponse)
        {
            _log.LogInformation("Start building XOi data into a bookable resource");

            if (graphQlResponse == null)
            {
                throw new Exception("GraphQL response is null.");
            }

            var xOiToBookableResourceData = new XOiToBookableResourceData();
            var responseStatus = GenerateResponseStatus(_log, operationType, graphQlResponse);

            if (responseStatus.ResponseResult == JobResponseResult.Success)
            {
                xOiToBookableResourceData.operationType = operationType;
                xOiToBookableResourceData.Message = responseStatus.Message;
                xOiToBookableResourceData.jobResponseResult = responseStatus.ResponseResult;

                switch (operationType)
                {
                    case OperationType.Create:
                        xOiToBookableResourceData.XOiVisionJobId = graphQlResponse.Data.CreateJob.Job.Id;
                        xOiToBookableResourceData.XoiVisionJobURL = graphQlResponse.Data.CreateJob.Job.DeepLinks.VisionMobile.EditJob.Url;
                        xOiToBookableResourceData.XoiVisionJobShareURL = graphQlResponse.Data.CreateJob.AdditionalActionsResults.CreatePublicShare.ShareLink;
                        xOiToBookableResourceData.XoiVisionWebURL = graphQlResponse.Data.CreateJob.Job.DeepLinks.VisionWeb.ViewJob.Url; 
                        break;
                    case OperationType.Update:
                        xOiToBookableResourceData.XOiVisionJobId = graphQlResponse.Data.UpdateJob.Job.Id;
                        break;
                    default:
                        break;
                }
            }
            else
            {
                throw new Exception(responseStatus.Message);
            }

            _log.LogInformation("Finish building XOi data into a bookable resource");

            return xOiToBookableResourceData;
        }

        public static List<XOiToCustomerAssetData> BuildXOiToCustomerAssetData(ILogger _log, GraphQLResponse<XOiJobSummaryResponse> graphQlResponse)
        {
            _log.LogInformation("Start building XOi data into a customer asset");

            if (graphQlResponse == null)
            {
                throw new Exception("GraphQL response is null.");
            }

            List<XOiToCustomerAssetData> xOiToCustomerAssentData = new List<XOiToCustomerAssetData>();

            foreach (var documentation in graphQlResponse.Data.GetJobSummary.JobSummary.Documentation)
            {
                if (documentation.Traits.Contains("processed") && documentation.Traits.Contains("dataplate") 
                    && documentation.Traits.Contains("not_a_dataplate") == false 
                    && documentation.Traits.Contains("pending") == false
                    && documentation.DerivedData != null)
                {
                    xOiToCustomerAssentData.Add(new XOiToCustomerAssetData
                    {
                        Make = documentation.DerivedData.Make,
                        Model = documentation.DerivedData.Model,
                        Serial = documentation.DerivedData.Serial,
                        Transcript = documentation.DerivedData.Transcript,
                        ManufactureDate = String.IsNullOrEmpty(documentation.DerivedData.ManufactureDate) 
                                                                                                ? null 
                                                                                                : DateTime.Parse(documentation.DerivedData.ManufactureDate)
                    });
                }   
            }

            var groupedBySerial = xOiToCustomerAssentData.GroupBy(item => item.Serial);

            List<XOiToCustomerAssetData> uniqueAssets = new List<XOiToCustomerAssetData>();

            foreach (var group in groupedBySerial)
            {
                var uniqueAsset = group.Last();
                uniqueAssets.Add(uniqueAsset);
            }

            _log.LogInformation("Finish building XOi data into a customer asset");

            return uniqueAssets;
        }

        public static XOiWorkSummaryToBookableResourceData BuildXOiWorkSummaryToBookableResourceData(ILogger _log, GraphQLResponse<XOiJobSummaryResponse> graphQlResponse, string workflowId)
        {
            _log.LogInformation("Start building XOi Work Summary data into a bookable resource data");

            if (graphQlResponse == null)
            {
                throw new Exception("GraphQL response is null.");
            }

            List<XOiWorkSummaryToBookableResourceData> xOiWorkSummaryToBookableResourceData = new List<XOiWorkSummaryToBookableResourceData>();

            foreach (var documentation in graphQlResponse.Data.GetJobSummary.JobSummary.Documentation)
            {
                xOiWorkSummaryToBookableResourceData.Add(new XOiWorkSummaryToBookableResourceData
                {
                    WorkflowName = documentation.WorkflowName,
                    CompleteDate = DateTime.Now.ToString("MM/dd/yyyy"),
                    WorkSummary = documentation.WorkSummary != null ? documentation.WorkSummary.SummaryText : "WO Summary is empty",
                    WorkflowId = workflowId,
                    UserInitial = $"{graphQlResponse.Data.GetJobSummary.JobSummary.Assignees.FirstOrDefault().GivenName} {graphQlResponse.Data.GetJobSummary.JobSummary.Assignees.FirstOrDefault().FamilyName}"
                }); ;
                 
            }

            var groupedByWorkflow = xOiWorkSummaryToBookableResourceData.GroupBy(item => item.WorkflowName);

            XOiWorkSummaryToBookableResourceData uniqueNote = groupedByWorkflow.FirstOrDefault().FirstOrDefault();

            _log.LogInformation("Finish building XOi Work Summary data into a bookable resource data");
            
            return uniqueNote;
        }


        public static XOiJobInfo BuildXOiJobInfoData(ILogger _log, GraphQLResponse<XOiCRUDResponse> graphQlResponse)
        {
            _log.LogInformation("Start building XOi Job Info data");

            XOiJobInfo xOiJobInfo = new XOiJobInfo
            {
                CustomerName = graphQlResponse.Data.GetJob.Job.CustomerName,
                OrderNumber = graphQlResponse.Data.GetJob.Job.WorkOrderNumber,
                AssigneeIds = graphQlResponse.Data.GetJob.Job.AssigneeIds
            };

            _log.LogInformation("Finish building XOi Job Info data");

            return xOiJobInfo;
        }

        private static XOiResponseStatus GenerateResponseStatus(ILogger _log, OperationType operationType, GraphQLResponse<XOiCRUDResponse> graphQLResponse)
        {
            _log.LogInformation("Start the generation of the response status");

            var sb = new StringBuilder();
            var responseStatus = new XOiResponseStatus();

            if (graphQLResponse.Errors != null && graphQLResponse.Errors.Any())
            {
                foreach (var error in graphQLResponse.Errors)
                {
                    sb.AppendLine(error.Message);
                }

                responseStatus.Message = sb.ToString();
                responseStatus.ResponseResult = JobResponseResult.Failure;
                return responseStatus;
            }
            else
            {
                switch (operationType)
                {
                    case OperationType.Create:
                        sb.AppendLine("Job successfully created");
                        break;
                    case OperationType.Update:
                        sb.AppendLine("Job successfully updated");
                        break;
                    default:
                        sb.AppendLine("Unknown operation");
                        break;
                }
            }
            responseStatus.Message = sb.ToString();
            responseStatus.ResponseResult = JobResponseResult.Success;

            _log.LogInformation("Finish of generation of response status");

            return responseStatus;
        }
    }
}
