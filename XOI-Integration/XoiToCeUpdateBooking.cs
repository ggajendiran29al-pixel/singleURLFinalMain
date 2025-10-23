using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using XOI_Integration.XOiRepository.XOiDataModels;
using System.Linq;
using XOI_Integration.XOiRepository;
using XOI_Integration.DataverseRepository.Operations;
using XOI_Integration.DataverseRepository.Provider;
using XOI_Integration.DataverseRepository;
using XOI_Integration.DataModels;
using System.Collections.Concurrent;

namespace XOI_Integration
{
    public static class XoiToCeUpdateBooking
    {
        // ✅ Deduplication cache (thread-safe)
        private static readonly ConcurrentDictionary<string, DateTime> RecentJobs = new ConcurrentDictionary<string, DateTime>();

        [FunctionName("XoiToCeUpdateBooking")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger _log)
        {
            DataverseApi.Initialize(Environment.GetEnvironmentVariable("DataverseConnectionString", EnvironmentVariableTarget.Process));

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _log.LogInformation($"Webhook is triggered and gets values: \n{requestBody}");

            var response = JsonConvert.DeserializeObject<XOiWebhookRequest>(requestBody);
            string jobId = response?.JobId;

            try
            {
                // 🧠 Dedup guard: skip if same JobId processed recently
                if (!string.IsNullOrEmpty(jobId))
                {
                    DateTime now = DateTime.UtcNow;
                    if (RecentJobs.TryGetValue(jobId, out DateTime lastRun) && (now - lastRun).TotalSeconds < 45)
                    {
                        _log.LogWarning($"⚠️ Duplicate webhook ignored for JobId: {jobId}");
                        return new OkObjectResult($"Duplicate webhook ignored for JobId: {jobId}");
                    }
                    RecentJobs[jobId] = now;
                }

                if (!string.IsNullOrEmpty(response?.Event))
                {
                    _log.LogInformation($"Processing event: {response.Event}");

                    var validEvents = new[] { "job_update", "workflow_job_update", "job_completed" };

                    if (validEvents.Contains(response.Event, StringComparer.OrdinalIgnoreCase))
                    {
                        _log.LogInformation("Webhook start operations");
                        var xOiOperation = new XOiOperation(_log);

                        // Always sync job info (even if no workflowJobId)
                        var xOiJobInfo = await xOiOperation.GetJobAsync(response.JobId);

                        if (!string.IsNullOrEmpty(response.WorkflowJobId))
                        {
                            // 🔹 Workflow-specific note creation
                            var xOiToCustomerAssetData = await xOiOperation.GetJobSummaryAsync(response.JobId, response.WorkflowJobId);
                            var customerAssetDataHandler = new CustomerAssetDataHandler(_log);
                            await customerAssetDataHandler.HandleCustomerAssetDataAsync(xOiToCustomerAssetData, xOiJobInfo, response.JobId);

                            var xOiWorkSummaryToBookableResourceData = await xOiOperation.GetJobSummaryWorkflowAsync(response.JobId, response.WorkflowJobId);
                            await BookableResourceWorkSummaryDataHandler.CreateBookableResourceBookingNoteAsync(
                                _log,
                                xOiWorkSummaryToBookableResourceData,
                                response.JobId
                            );
                        }
                        else
                        {
                            // 🔸 General job update (no workflow job)
                            _log.LogInformation($"Job update detected (no WorkflowJobId). Processing job summary for JobId: {response.JobId}");

                            var xOiToCustomerAssetData = await xOiOperation.GetJobSummaryAsync(response.JobId, null);
                            var customerAssetDataHandler = new CustomerAssetDataHandler(_log);
                            await customerAssetDataHandler.HandleCustomerAssetDataAsync(xOiToCustomerAssetData, xOiJobInfo, response.JobId);
                        }

                        _log.LogInformation("Webhook end operations");
                        return new OkObjectResult("XoiToCeUpdateBooking function completed");
                    }
                }

                _log.LogWarning("Nothing to Create or Update - Event not recognized or missing.");
                return new BadRequestObjectResult("Nothing to Create or Update");
            }
            catch (Exception ex)
            {
                _log.LogError($"❌ Exception in XoiToCeUpdateBooking: {ex.Message}");
                throw;
            }
        }
    }
}


/*using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XOI_Integration.XOiRepository.XOiDataModels;
using System.Net.Http;
using System.Text;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using GraphQL;
using XOI_Integration.XOiRepository.XOiTokenProvider;
using System.Linq;
using XOI_Integration.XOiRepository;
using XOI_Integration.DataFactory.BaseObject;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using XOI_Integration.DataverseRepository.Operations;
using XOI_Integration.DataverseRepository.Provider;
using XOI_Integration.DataverseRepository;
using XOI_Integration.Helper;
using XOI_Integration.DataModels;
using System.Collections.Concurrent;

    
*/
/*
namespace XOI_Integration
{   public static class XoiToCeUpdateBooking
    {
        [FunctionName("XoiToCeUpdateBooking")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger _log)
        {

            DataverseApi.Initialize(System.Environment.GetEnvironmentVariable("DataverseConnectionString", EnvironmentVariableTarget.Process));

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            _log.LogInformation($"Webhook is triggered and gets values: \n{requestBody}");

            XOiWebhookRequest response = JsonConvert.DeserializeObject<XOiWebhookRequest>(requestBody);

            if (response.Event == "job_update" && !String.IsNullOrEmpty(response.WorkflowJobId))
            {
                _log.LogInformation("Webhook start operations");

                XOiOperation xOiOperation = new XOiOperation(_log);
                 
                var xOiToCustomerAssetData = await xOiOperation.GetJobSummaryAsync(response.JobId, response.WorkflowJobId);
                var xOiJobInfo = await xOiOperation.GetJobAsync(response.JobId);

                var customerAssetDataHandler = new CustomerAssetDataHandler(_log);
                await customerAssetDataHandler.HandleCustomerAssetDataAsync(xOiToCustomerAssetData, xOiJobInfo, response.JobId);

                var xOiWorkSummaryToBookableResourceData = await xOiOperation.GetJobSummaryWorkflowAsync(response.JobId, response.WorkflowJobId);

                //var boolableResourceBookingWorkSummarDataHandler = new BookableResourceWorkSummaryDataHandler(_log);
                //await boolableResourceBookingWorkSummarDataHandler.HandleBookableResourceWorkSummaryToNotesData(xOiWorkSummaryToBookableResourceData, response.JobId);
                //gg
                await BookableResourceWorkSummaryDataHandler.CreateBookableResourceBookingNoteAsync(
    _log,
    xOiWorkSummaryToBookableResourceData,
    response.JobId
);

                _log.LogInformation("Webhook end operations");

                return new OkObjectResult("XoiToCeUpdateBooking function completed");
            }
            else
            {
                _log.LogInformation("Nothing to Create or Update");
                return new BadRequestObjectResult("Nothing to Create or Update");
            }
        }
    }
}*/