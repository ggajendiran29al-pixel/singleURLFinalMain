using System.Threading.Tasks;
using System;
using XOI_Integration.DataFactory.BaseObject;
using XOI_Integration.DataModels.Enums;
using XOI_Integration.DataverseRepository.Operations;
using XOI_Integration.XOiRepository;
using Microsoft.Extensions.Logging;
using XOI_Integration.XOiRepository.XOiDataModels;
using XOI_Integration.XOiRepository.Provider;

public class XOiToBookableResourceDataHandler
{
    private readonly ILogger _log;

    public XOiToBookableResourceDataHandler(ILogger log)
    {
        _log = log;
    }

    public async Task<XOiToBookableResourceData> HandleXOiToBookableResourceDataAsync(OperationType operationType, JobRelatedData jobRelatedData, Guid bookableResourceBookingId, string xOiJobId)
    {
        var xOiToBookableResourceData = new XOiToBookableResourceData();
        var xOiOperation = new XOiOperation(_log);

        try
        {
            if (operationType == OperationType.Create)
            {
                xOiToBookableResourceData = await xOiOperation.CreateJobAsync(jobRelatedData);

                _log.LogInformation("Update Bookable Resource Booking");
                await BookableResourceBookingOperation.UpdateBookableResourceBookingAsync(bookableResourceBookingId, xOiToBookableResourceData);
            }
            else if (operationType == OperationType.Update)
            {
                xOiToBookableResourceData = await xOiOperation.UpdateJobAsync(jobRelatedData, xOiJobId);
            }
        }
        catch (Exception ex)
        {
            xOiToBookableResourceData.operationType = operationType;
            xOiToBookableResourceData.jobResponseResult = JobResponseResult.Failure;
            xOiToBookableResourceData.Message = ex.Message;

            if (operationType == OperationType.Update)
            {
                xOiToBookableResourceData.XOiVisionJobId = xOiJobId;
            }
        }

        return xOiToBookableResourceData;
    }
}
