using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XOI_Integration.DataFactory;
using XOI_Integration.DataFactory.BaseObject;
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

                    // Merge all technician emails and update XOi assignees
                    var allBookingIds = await BookableResourceBookingOperation.GetBookableResourceBookingIdsAsync(existingJobId);
                    var allEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var bId in allBookingIds)
                    {
                        var techEmail = await BookableResourceBookingOperation.GetTechnicianEmailFromBookingAsync(bId);
                        if (!string.IsNullOrEmpty(techEmail))
                            allEmails.Add(techEmail);
                    }

                    jobData.AssigneeIds = string.Join(",", allEmails);
                    log.LogInformation($"Updating XOi job {existingJobId} with merged assignees: {jobData.AssigneeIds}");

                    var xoiOp = new XOiOperation(log);
                    await xoiOp.UpdateJobAsync(jobData, existingJobId);

                    log.LogInformation("✔ Copied job details and updated XOi assignees for secondary booking");
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
