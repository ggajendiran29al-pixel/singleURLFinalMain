using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using XOI_Integration.DataFactory.BaseObject;
using XOI_Integration.DataFactory.InheritedObjects.OperationsForInheritedObjects;
using XOI_Integration.DataModels.Enums;
using XOI_Integration.DataverseRepository.Operations;
using XOI_Integration.XOiRepository;
using XOI_Integration.XOiRepository.XOiDataModels;

public class XOiToBookableResourceDataHandler
{
    private readonly ILogger _log;

    public XOiToBookableResourceDataHandler(ILogger log)
    {
        _log = log;
    }

    public async Task<XOiToBookableResourceData> HandleXOiToBookableResourceDataAsync(
        OperationType operationType,
        JobRelatedData jobRelatedData,
        Guid bookingId,
        string existingJobId)
    {
        _log.LogInformation("▶️ XOiToBookableResourceDataHandler STARTED");

        var xoiOp = new XOiOperation(_log);
        XOiToBookableResourceData result;

        try
        {
            // ====================================================
            // 1. CREATE or UPDATE JOB IN XOi
            // ====================================================
            result = (operationType == OperationType.Create)
                ? await xoiOp.CreateJobAsync(jobRelatedData)
                : await xoiOp.UpdateJobAsync(jobRelatedData, existingJobId);

            string jobId = result.XOiVisionJobId;

            if (string.IsNullOrEmpty(jobId))
                throw new Exception("XOi Vision Job ID was not returned by API.");

            _log.LogInformation($"📌 XOi JobId received: {jobId}");


            // ====================================================
            // 2. UPDATE WORK ORDER WITH JOB ID
            // ====================================================
            if (jobRelatedData.WorkOrderId != Guid.Empty)
            {
                await WorkOrderOperation.UpdateXOiJobIdOnWorkOrderAsync(
                    jobRelatedData.WorkOrderId,
                    jobId
                );

                _log.LogInformation("✔ WorkOrder updated with XOi Job ID");
            }
            else
            {
                _log.LogWarning("⚠ WorkOrderId was EMPTY - JobId was not stored on WorkOrder.");
            }

            // ====================================================
            // 3. UPDATE BOOKING WITH JOB ID + URL MODEL
            // ====================================================
            await BookableResourceBookingOperation.UpdateXOiJobIdOnBookingAsync(
                bookingId,
                jobId
            );

            _log.LogInformation("✔ Booking updated with XOi Job ID");

            await BookableResourceBookingOperation.UpdateBookableResourceBookingAsync(
                bookingId,
                result
            );

            // ====================================================
            // 4. FIX: Only correct Contribute URL *if truly needed*
            // ====================================================
            string finalUrl = result.ContributeToJobUrl;

            if (!string.IsNullOrEmpty(finalUrl))
            {
                // Only replace if URL is INVALID (XOi bug pattern)
                if (finalUrl.Contains("my-work/contribute?jobId="))
                {
                    _log.LogWarning("⚠ XOi bug detected: incorrect Contribute URL format. Replacing with ViewJob URL.");

                    finalUrl = result.XoiVisionWebURL;
                }

                await BookableResourceBookingOperation.UpdateWebJobUrlOnBookingAsync(
                    bookingId,
                    finalUrl
                );

                _log.LogInformation("✔ Correct WebJob URL saved to booking");
            }

            _log.LogInformation("✅ Handler completed SUCCESSFULLY");
        }
        catch (Exception ex)
        {
            _log.LogError($"❌ XOiToBookableResourceDataHandler FAILED: {ex.Message}");

            return new XOiToBookableResourceData
            {
                operationType = operationType,
                jobResponseResult = JobResponseResult.Failure,
                Message = ex.Message,
                XOiVisionJobId = (operationType == OperationType.Update ? existingJobId : null)
            };
        }

        return result;
    }
}
