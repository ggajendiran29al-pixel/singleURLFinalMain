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
                XOiVisionJobId = (operationType == OperationType.Update ? existingJobId : null)
            };
        }

        return result;
    }
}
