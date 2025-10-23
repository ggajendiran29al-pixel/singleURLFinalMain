using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;
using XOI_Integration.DataModels.Enums;
using XOI_Integration.DataverseRepository.Provider;
using XOI_Integration.XOiRepository.XOiDataModels;

namespace XOI_Integration.DataverseRepository.Operations
{
    public static class IntegrationLogOperation
    {
        public static async Task CreateJobSummaryLogAsync(JobResponseResult result, string jobId, XOiJobSummaryResponse xoiJobSummaryResponse = null, string message = null)
        {
            Entity xoiIntegrationLog = new Entity("sisps_xoiintegrationlog");

            var bookableResourceBookingId = await BookableResourceBookingOperation.GetBookableResourceBookingIdAsync(jobId);

            xoiIntegrationLog["sisps_result"] = Convert.ToBoolean(result);
            xoiIntegrationLog["sisps_operationtype"] = OperationType.GetJobSummary.ToString();
            xoiIntegrationLog["sisps_lookuptobookableresourcebookingid"] = new EntityReference("bookableresourcebooking", bookableResourceBookingId);
            xoiIntegrationLog["sisps_xoijobid"] = jobId;

            if (xoiJobSummaryResponse != null)
            {
                xoiIntegrationLog["sisps_integrationmessage"] =  ConcatenateMessage(xoiJobSummaryResponse);
            }
            else if (!string.IsNullOrEmpty(message))
            {
                xoiIntegrationLog["sisps_integrationmessage"] = message;
            }

            await DataverseApi.Instance.CreateAsync(xoiIntegrationLog);
        }

        public static async Task CreateAssetsLogAsync(string jobId, JobResponseResult result, OperationType operationType, string responseMessage)
        {
            var bookableResourceBookingId = await BookableResourceBookingOperation.GetBookableResourceBookingIdAsync(jobId);
            
            Entity xoiIntegrationLog = new Entity("sisps_xoiintegrationlog");

            xoiIntegrationLog["sisps_lookuptobookableresourcebookingid"] = new EntityReference("bookableresourcebooking", bookableResourceBookingId);
            xoiIntegrationLog["sisps_xoijobid"] = jobId;
            xoiIntegrationLog["sisps_result"] = Convert.ToBoolean(result);
            xoiIntegrationLog["sisps_operationtype"] = operationType.ToString();
            xoiIntegrationLog["sisps_integrationmessage"] = responseMessage;

            await DataverseApi.Instance.CreateAsync(xoiIntegrationLog);
        }


        public static async Task CreateLogAsync(Guid bookableResourceBookingId, XOiToBookableResourceData xOiToBookableResourceData)
        {
            Entity xoiIntegrationLog = new Entity("sisps_xoiintegrationlog");

            xoiIntegrationLog["sisps_lookuptobookableresourcebookingid"] = new EntityReference("bookableresourcebooking", bookableResourceBookingId);
            xoiIntegrationLog["sisps_xoijobid"] = xOiToBookableResourceData.XOiVisionJobId;
            xoiIntegrationLog["sisps_result"] = Convert.ToBoolean(xOiToBookableResourceData.jobResponseResult);
            xoiIntegrationLog["sisps_operationtype"] = xOiToBookableResourceData.operationType.ToString();
            xoiIntegrationLog["sisps_integrationmessage"] = xOiToBookableResourceData.Message;

            await DataverseApi.Instance.CreateAsync(xoiIntegrationLog);
        }

        private static string ConcatenateMessage(XOiJobSummaryResponse obj)
        {
            StringBuilder concatenatedString = new StringBuilder();

            concatenatedString.AppendLine($"JobId: {obj.GetJobSummary.JobSummary.JobId}");
            concatenatedString.AppendLine();
            concatenatedString.AppendLine("Assigned:");

            foreach (Assginees assignee in obj.GetJobSummary.JobSummary.Assignees)
            {
                concatenatedString.AppendLine($"  ● Assignee Id: {assignee.Id}");
                concatenatedString.AppendLine($"  ● Assignee Email: {assignee.Email}");
                concatenatedString.AppendLine($"  ● Assignee Given Name: {assignee.GivenName}");
                concatenatedString.AppendLine($"  ● Assignee Family Name: {assignee.FamilyName}");
                concatenatedString.AppendLine();
            }

            bool isFirstIteration = true;

            foreach (Documentation documentation in obj.GetJobSummary.JobSummary.Documentation)
            {
                if (documentation.Traits != null && documentation.Traits.Count != 0 && !ContainsChoiceTrait(documentation))
                {
                    if (!isFirstIteration)
                    {
                        concatenatedString.AppendLine("--------------------------------------------------------------------------------");
                    }
                    else
                    {
                        concatenatedString.AppendLine($"[Workflow Name: { documentation.WorkflowName}]");
                        isFirstIteration = false;
                    }

                    ConcatenateTraits(concatenatedString, documentation.Traits);
                    ConcatenateTags(concatenatedString, documentation.Tags);
                    ConcatenateNoteText(concatenatedString, documentation.Note?.Text);
                    ConcatenateDeliveredData(concatenatedString, documentation.Choice?.Chosen, documentation.DerivedData);
                    ConcatenateWorkSummary(concatenatedString, documentation.WorkSummary);
                }
                else
                {
                    continue;
                }
            }

            Console.WriteLine(concatenatedString.ToString());
            return concatenatedString.ToString();
        }

        private static void ConcatenateTraits(StringBuilder stringBuilder, List<string> traits)
        {
            stringBuilder.Append("\tTraits:");

            if (traits != null && traits.Count > 0)
            {
                stringBuilder.AppendLine();

                foreach (string trait in traits)
                {
                    stringBuilder.AppendLine($"\t\t● {trait}");
                }
            }
            else
            {
                stringBuilder.AppendLine(" - ");
            }

            stringBuilder.AppendLine();
        }

        private static void ConcatenateTags(StringBuilder stringBuilder, List<object> tags)
        {
            stringBuilder.Append("\tTags:");

            if (tags != null && tags.Count > 0)
            {
                stringBuilder.AppendLine();

                foreach (object tag in tags)
                {
                    string tagJson = JsonConvert.SerializeObject(tag);
                    stringBuilder.AppendLine($"\t\t● {tagJson}");
                }
            }
            else
            {
                stringBuilder.AppendLine(" - ");
            }

            stringBuilder.AppendLine();
        }

        private static void ConcatenateNoteText(StringBuilder stringBuilder, string noteText)
        {
            stringBuilder.AppendLine($"\tNote Text: {(noteText ?? " - ")}");
        }

        private static void ConcatenateDeliveredData(StringBuilder stringBuilder, List<string> chosenChoices, DerivedData derivedData)
        {
            if (chosenChoices != null && chosenChoices.Count > 0)
            {
                stringBuilder.AppendLine("\t\t● Chosen Choices:");

                foreach (string choice in chosenChoices)
                {
                    stringBuilder.AppendLine($" - {choice}");
                }

                if (derivedData != null)
                {
                    ConcatenateDerivedData(stringBuilder, derivedData);
                }

                return;
            }

            if (derivedData != null)
            {
                ConcatenateDerivedData(stringBuilder, derivedData);
            }
        }

        private static void ConcatenateDerivedData(StringBuilder stringBuilder, DerivedData derivedData)
        {
            stringBuilder.AppendLine("\tDerived Data:");

            if (!string.IsNullOrEmpty(derivedData.Make))
            {
                stringBuilder.AppendLine($"\t\t● Make: {derivedData.Make}");
            }

            if (!string.IsNullOrEmpty(derivedData.Model))
            {
                stringBuilder.AppendLine($"\t\t● Model: {derivedData.Model}");
            }

            if (!string.IsNullOrEmpty(derivedData.Serial))
            {
                stringBuilder.AppendLine($"\t\t● Serial: {derivedData.Serial}");
            }

            if (!string.IsNullOrEmpty(derivedData.ManufactureDate))
            {
                stringBuilder.AppendLine($"\t\t● Manufacture Date: {derivedData.ManufactureDate}");
            }
        }

        private static void ConcatenateWorkSummary(StringBuilder stringBuilder, WorkSummary workSummary)
        {
            if (workSummary != null)
            {
                stringBuilder.AppendLine($"\tWork Summary Text: \n");
                stringBuilder.AppendLine($"{workSummary.SummaryText} \n");
            }
        }

        private static bool ContainsChoiceTrait(Documentation documentation)
        {
            if (documentation.Traits != null && documentation.Traits.Count != 0)
            {
                foreach (string trait in documentation.Traits)
                {
                    if (trait.IndexOf("choice", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
