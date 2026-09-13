using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XOI_Integration.DataFactory;
using XOI_Integration.DataFactory.BaseObject;
using XOI_Integration.DataModels.Enums;
using XOI_Integration.DataFactory.InheritedObjects.OperationsForInheritedObjects;
using XOI_Integration.DataverseRepository;
using XOI_Integration.DataverseRepository.Operations;
using XOI_Integration.DataverseRepository.Provider;
using XOI_Integration.Helper;
using XOI_Integration.XOiRepository;

namespace XOI_Integration
{
    public class XoiToCEWorkOrderJobShareFunc
    {
        [FunctionName("XoiToCEWorkOrderJobShare")]
        public async Task RunAsync(
            [ServiceBusTrigger("xoitoceworkorderjobshare", Connection = "SBConnection")]
            string message,
            ILogger log)
        {
            log.LogWarning("XoiToCEWorkOrderJobShare triggered.");
            DataverseApi.Initialize(Environment.GetEnvironmentVariable("DataverseConnectionString"));

            Guid bookingId = DeserializeJSON.GetBookableResourceBookingId(message);
            log.LogInformation($"Processing BRB: {bookingId}");

            JobRelatedData jobData = await JobRelatedDataFactory.CreateAsync(bookingId);
            await jobData.LoadData();

            bool isProject = jobData.ProjectId != Guid.Empty;
            bool isWorkOrder = jobData.WorkOrderId != Guid.Empty;

            log.LogInformation($"Booking type — WorkOrder: {isWorkOrder}, Project: {isProject}");

            // 1️⃣ Check if parent entity already has an XOi job
            string existingJobId = null;

            if (isWorkOrder)
                existingJobId = await WorkOrderOperation.GetXOiJobIdAsync(jobData.WorkOrderId);
            else if (isProject)
                existingJobId = await ProjectOperation.GetXOiJobIdAsync(jobData.ProjectId);

            if (!string.IsNullOrEmpty(existingJobId))
            {
                log.LogInformation($"Reusing existing job: {existingJobId}");

                Guid firstBookingId = await BookableResourceBookingOperation.GetBookableResourceBookingIdAsync(existingJobId);

                if (firstBookingId != Guid.Empty)
                {
                    log.LogInformation($"Found first booking: {firstBookingId}");

                    await BookableResourceBookingOperation.CopyJobDetailsToCurrentAsync(
                        bookingId,
                        firstBookingId
                    );

                    // XOi supports a single assignee per job — "Currently only one Assignee
                    // ID is supported" — and that assignee is the job owner, the only one
                    // who can close the job. So push the nominated owner alone rather than
                    // every technician on the work order; the others keep access through
                    // the Vision deep link and can still complete workflows.
                    string ownerEmail =
                        await BookableResourceBookingOperation.GetXOiJobOwnerEmailForJobAsync(log, existingJobId)
                        ?? await BookableResourceBookingOperation.GetTechnicianEmailFromBookingAsync(firstBookingId);

                    if (string.IsNullOrEmpty(ownerEmail))
                    {
                        log.LogWarning($"No XOi job owner could be resolved for job {existingJobId} — skipping updateJob");
                        return;
                    }

                    var allEmails = new HashSet<string>(new[] { ownerEmail }, StringComparer.OrdinalIgnoreCase);
                    jobData.AssigneeIds = allEmails.ToList();

                    var xoiOp = new XOiOperation(log);

                    // Skip the XOi mutation when the assignee already matches what XOi holds.
                    // The Booking Update plugin fires on all attributes and re-fires on the
                    // writes made just above, so without this every re-trigger sent another
                    // updateJob. Splitting on commas normalises any legacy value left by the
                    // earlier joined-string payload so those compare correctly too.
                    var currentJob = await xoiOp.GetJobAsync(existingJobId);
                    var xoiEmails = new HashSet<string>(
                        (currentJob?.AssigneeIds ?? new List<string>())
                            .SelectMany(id => (id ?? string.Empty).Split(','))
                            .Select(id => id.Trim())
                            .Where(id => !string.IsNullOrEmpty(id)),
                        StringComparer.OrdinalIgnoreCase);

                    if (xoiEmails.Count > 0 && xoiEmails.SetEquals(allEmails))
                    {
                        log.LogInformation($"XOi job {existingJobId} already owned by {ownerEmail} — skipping updateJob");
                        return;
                    }

                    log.LogInformation($"Setting XOi job {existingJobId} owner to {ownerEmail}");

                    var updateResult = await xoiOp.UpdateJobAsync(jobData, existingJobId);

                    // A rejected mutation used to be discarded here, so a failed assignee
                    // push logged the same success line as a working one.
                    if (updateResult?.jobResponseResult != JobResponseResult.Success)
                    {
                        log.LogError($"XOi updateJob failed for job {existingJobId} — assignees not applied: {updateResult?.Message ?? "no response"}");
                        return;
                    }

                    log.LogInformation("✔ Copied job details and set XOi job owner for secondary booking");
                    return;
                }

                return;
            }

            // 2️⃣ Create new job
            var bookingJobId = await BookableResourceBookingOperation.GetXOiJobIdAsync(bookingId);

            var operationType = XOiOperationType.DetermineOperationType(message, bookingJobId);

            var handler = new XOiToBookableResourceDataHandler(log);
            var xData = await handler.HandleXOiToBookableResourceDataAsync(
                operationType,
                jobData,
                bookingId,
                bookingJobId
            );

            // 3️⃣ Store XOi job ID on parent entity for reuse by subsequent bookings
            if (!string.IsNullOrEmpty(xData?.XOiVisionJobId))
            {
                if (isWorkOrder)
                    await WorkOrderOperation.UpdateXOiJobIdOnWorkOrderAsync(jobData.WorkOrderId, xData.XOiVisionJobId);
                else if (isProject)
                    await ProjectOperation.UpdateXOiJobIdOnProjectAsync(jobData.ProjectId, xData.XOiVisionJobId);
            }

            log.LogInformation("Create integration logs");
            await IntegrationLogOperation.CreateLogAsync(bookingId, xData);

            log.LogInformation("XoiToCEWorkOrderJobShare completed");
        }
    }
}
