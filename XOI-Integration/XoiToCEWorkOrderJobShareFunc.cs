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

            // 1️⃣ Check if WorkOrder already has an XOi job
            var existingJobId = await WorkOrderOperation.GetXOiJobIdAsync(jobData.WorkOrderId);

            if (!string.IsNullOrEmpty(existingJobId))
            {
                log.LogInformation($"Reusing WorkOrder job: {existingJobId}");

                Guid firstBookingId = await BookableResourceBookingOperation.GetBookableResourceBookingIdAsync(existingJobId);

                if (firstBookingId != Guid.Empty)
                {
                    log.LogInformation($"Found first booking: {firstBookingId}");

                    await BookableResourceBookingOperation.CopyJobDetailsToCurrentAsync(
                        bookingId,
                        firstBookingId
                    );

                    // Get all bookings for this job and merge all technician emails into XOi assigneeIds
                    // This ensures XOi knows about all technicians so per-workflow assignee email is correct
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

            log.LogInformation("Create integration logs");
            await IntegrationLogOperation.CreateLogAsync(bookingId, xData);

            log.LogInformation("XoiToCEWorkOrderJobShare completed");
        }
    }
}
