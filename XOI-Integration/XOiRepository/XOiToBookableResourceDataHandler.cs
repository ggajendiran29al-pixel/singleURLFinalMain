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

        // Kept outside the try so the catch can still report a job XOi already created.
        string jobId = null;

        try
        {
            //GG added on 9/24/2026
            // ====================================================
            // 0. SET WORK ORDER XOi JOB OWNER FROM FIRST BOOKING
            // ====================================================
            // Isolated try/catch: an owner-sync problem must never block XOi job creation.
            if (jobRelatedData.WorkOrderId != Guid.Empty)
            {
                try
                {
                    await BookableResourceBookingOperation
                        .SetWorkOrderOwnerFromFirstBookingAsync(_log, jobRelatedData.WorkOrderId);
                }
                catch (Exception ownerEx)
                {
                    _log.LogError(ownerEx, "Setting work order XOi job owner failed — continuing.");
                }
            }
            // ====================================================
            // 1. CREATE or UPDATE JOB IN XOi
            // ====================================================
            result = (operationType == OperationType.Create)
                ? await xoiOp.CreateJobAsync(jobRelatedData)
                : await xoiOp.UpdateJobAsync(jobRelatedData, existingJobId);

            if (string.IsNullOrEmpty(result.XOiVisionJobId))
                throw new Exception("XOi Vision Job ID was not returned by API.");

            jobId = result.XOiVisionJobId;

            _log.LogInformation($"📌 XOi JobId received: {jobId}");

            // The booking is written BEFORE the work order on purpose. Messages waiting on
            // the job-creation claim are released the moment the work order shows the job
            // ID, and they then look up the first booking by that ID to copy its details —
            // so the booking must already carry it.

            // ====================================================
            // 2. UPDATE BOOKING WITH JOB ID + URL MODEL
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
            // 3. UPDATE WORK ORDER WITH JOB ID
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

            // 03042026 Removed step 4: UpdateWebJobUrlOnBookingAsync was overwriting sisps_xoi_vision_webjoburl with ContributeToJob URL after it was correctly set in step 3
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
                // If XOi already created the job and only a Dataverse write failed, keep its
                // ID so the caller still stores it on the parent. Dropping it here used to
                // orphan the job, and the next booking update created a second one.
                XOiVisionJobId = jobId ?? (operationType == OperationType.Update ? existingJobId : null)
            };
        }

        return result;
    }
}
