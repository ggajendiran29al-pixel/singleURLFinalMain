using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

using XOI_Integration.XOiRepository;
using XOI_Integration.XOiRepository.XOiDataModels;
using XOI_Integration.DataverseRepository;
using XOI_Integration.DataverseRepository.Provider;
using XOI_Integration.DataverseRepository.Operations;

namespace XOI_Integration
{
    public static class XoiToCeUpdateBooking
    {
        private static readonly ConcurrentDictionary<string, DateTime> Recent = new();

        [FunctionName("XoiToCeUpdateBooking")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger _log)
        {
            DataverseApi.Initialize(
                Environment.GetEnvironmentVariable("DataverseConnectionString"));

            string raw = await new StreamReader(req.Body).ReadToEndAsync();
            _log.LogInformation($"Webhook triggered: {raw}");

            var webhook = JsonConvert.DeserializeObject<XOiWebhookRequest>(raw);

            if (webhook == null || string.IsNullOrEmpty(webhook.JobId))
                return new BadRequestObjectResult("Invalid payload");

            string jobId = webhook.JobId;
            string workflowJobId = webhook.WorkflowJobId;

            try
            {
                // =====================================================
                // 1️⃣ Deduplicate webhook events
                // =====================================================
                string dedupKey = $"{jobId}_{workflowJobId}";
                DateTime now = DateTime.UtcNow;

                if (Recent.TryGetValue(dedupKey, out DateTime last)
                    && (now - last).TotalSeconds < 45)
                {
                    _log.LogWarning($"Duplicate webhook ignored for {dedupKey}");
                    return new OkObjectResult("Duplicate webhook ignored");
                }

                Recent[dedupKey] = now;

                // =====================================================
                // 2️⃣ Load Job Info
                // =====================================================
                var xOi = new XOiOperation(_log);
                var jobInfo = await xOi.GetJobAsync(jobId);

                jobInfo.WorkflowJobId = workflowJobId;
                _log.LogInformation($"workflowJobId received = {workflowJobId}");

                // =====================================================
                // 3️⃣ Resolve booking for THIS workflow (existing logic)
                // =====================================================
                var allBookings =
                    await BookableResourceBookingOperation
                        .GetBookableResourceBookingIdsAsync(jobId);

                Guid bookingId = Guid.Empty;

                foreach (var brbId in allBookings)
                {
                    var brb = DataverseApi.Instance.Retrieve(
                        "bookableresourcebooking",
                        brbId,
                        new Microsoft.Xrm.Sdk.Query.ColumnSet("acl_xoi_workflowjobid")
                    );

                    var existingWorkflowId =
                        brb.GetAttributeValue<string>("acl_xoi_workflowjobid");

                    if (string.IsNullOrEmpty(existingWorkflowId))
                    {
                        bookingId = brbId;
                        break;
                    }
                }

                if (bookingId == Guid.Empty && allBookings.Any())
                {
                    bookingId = allBookings.Last();
                }

                // =====================================================
                // 4️⃣ Assign workflowJobId ONLY IF EMPTY
                // =====================================================
                if (bookingId != Guid.Empty && !string.IsNullOrEmpty(workflowJobId))
                {
                    var currentValue =
                        DataverseApi.Instance.Retrieve(
                            "bookableresourcebooking",
                            bookingId,
                            new Microsoft.Xrm.Sdk.Query.ColumnSet("acl_xoi_workflowjobid")
                        )
                        .GetAttributeValue<string>("acl_xoi_workflowjobid");

                    if (string.IsNullOrEmpty(currentValue))
                    {
                        await BookableResourceBookingOperation
                            .UpdateWorkflowJobIdOnBookingAsync(
                                bookingId,
                                workflowJobId);

                        _log.LogInformation(
                            $"workflowJobId mapped to booking {bookingId}");
                    }
                }

                // =====================================================
                // 5️⃣ WORKFLOW UPDATE (Asset creation + notes)
                // =====================================================
                if (!string.IsNullOrEmpty(workflowJobId))
                {
                    _log.LogInformation(
                        "🔹 Workflow update detected — technician notes scenario");

                    // Fetch workflow summary (notes)
                    var wfSummary =
                        await xOi.GetJobSummaryWorkflowAsync(jobId, workflowJobId);

                    jobInfo.WorkSummary = wfSummary;
                    // ✅ Asset creation/update — ONLY here
                    var assetData =
                        await xOi.GetJobSummaryAsync(jobId, workflowJobId);

                    var assetHandler =
                        new CustomerAssetDataHandler(_log);

                    await assetHandler.HandleCustomerAssetDataAsync(
                        assetData,
                        jobInfo,
                        jobId);

                    

                    if (wfSummary != null && wfSummary.IsFilled())
                    {
                        _log.LogInformation(
                            "🟢 Technician entered notes — creating booking note");

                        await BookableResourceBookingOperation
                            .CreateBookableResourceBookingNoteAsync(
                                _log,
                                wfSummary,
                                jobId,
                                workflowJobId);
                    }

                    // ✅ Association attempt (may or may not succeed yet)
                    await TryAssociateAssetAsync(_log, jobInfo, jobId);

                    return new OkObjectResult("Workflow job update handled");
                }

                // =====================================================
                // 6️⃣ NORMAL JOB UPDATE (Association retry ONLY)
                // =====================================================
                _log.LogInformation("🔸 Normal job update (retry association)");

                if (jobInfo.WorkSummary == null ||
                    !jobInfo.WorkSummary.IsFilled())
                {
                    jobInfo.WorkSummary =
                        await xOi.GetJobSummaryWorkflowAsync(jobId, null);
                }

                // ❌ NO asset creation here
                await TryAssociateAssetAsync(_log, jobInfo, jobId);

                _log.LogInformation(
                    "Skipping asset creation and note creation (normal job update)");

                return new OkObjectResult("Job update handled");
            }
            catch (Exception ex)
            {
                _log.LogError(
                    $"❌ Exception in XoiToCeUpdateBooking: {ex}");
                throw;
            }
        }

        // =====================================================
        // retry helper
        // =====================================================
        private static async Task TryAssociateAssetAsync(
            ILogger log,
            XOiJobInfo jobInfo,
            string jobId)
        {
            if (jobInfo?.WorkSummary?.CustomerAssetId == Guid.Empty)
            {
                log.LogInformation("No asset available for association retry.");
                return;
            }

            var bookingIds =
                await BookableResourceBookingOperation
                    .GetBookableResourceBookingIdsAsync(jobId);

            await CustomerAssetOperation
                .AssociateAssetToWorkOrderIncidentAsync(
                    log,
                    jobInfo.WorkSummary.CustomerAssetId,
                    bookingIds);
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