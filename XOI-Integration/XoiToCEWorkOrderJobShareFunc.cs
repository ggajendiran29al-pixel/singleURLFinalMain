using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using XOI_Integration.DataFactory;
using XOI_Integration.DataFactory.BaseObject;
using XOI_Integration.DataverseRepository;
using XOI_Integration.DataverseRepository.Operations;
using XOI_Integration.DataverseRepository.Provider;
using XOI_Integration.Helper;

namespace XOI_Integration
{
    public class XoiToCEWorkOrderJobShareFunc
    {
        [FunctionName("XoiToCEWorkOrderJobShare")]
        public async Task RunAsync([ServiceBusTrigger("xoitoceworkorderjobshare", Connection = "SBConnection")] string myQueueItem, ILogger log)
        {
            DataverseApi.Initialize(System.Environment.GetEnvironmentVariable("DataverseConnectionString", EnvironmentVariableTarget.Process));

            Guid bookableResourceBookingId = DeserializeJSON.GetBookableResourceBookingId(myQueueItem);


            (bool hasOtherResources, Guid copyFromBookableresourcebookingid) = await BookableResourceChecker.CheckForOtherResourcesAndJobIdAsync(bookableResourceBookingId);

            log.LogInformation($"A job has already been created for the corresponding WorkOrder/Project. Resource ID: {copyFromBookableresourcebookingid}");

            /*if (hasOtherResources)
            {
                log.LogInformation("Initiating the copy operation");
                await BookableResourceBookingOperation.CopyJobDetailsToCurrentAsync(bookableResourceBookingId, copyFromBookableresourcebookingid);

                log.LogInformation("Copy operation completed successfully");
            }
            else
            {*/
                JobRelatedData jobRelatedData = await JobRelatedDataFactory.CreateAsync(bookableResourceBookingId);
                await jobRelatedData.LoadData();

                var xOiJobId = await BookableResourceBookingOperation.GetXOiJobIdAsync(bookableResourceBookingId);

                var operationType = XOiOperationType.DetermineOperationType(myQueueItem, xOiJobId);

                var xOiToBookableResourceDataHandler = new XOiToBookableResourceDataHandler(log);
                var xOiToBookableResourceData = await xOiToBookableResourceDataHandler.HandleXOiToBookableResourceDataAsync(operationType, jobRelatedData, bookableResourceBookingId, xOiJobId);

                log.LogInformation("Create integration logs");

                await IntegrationLogOperation.CreateLogAsync(bookableResourceBookingId, xOiToBookableResourceData);

                log.LogInformation("XoiToCEWorkOrderJobShare function completed");
            //}
        }
    }
}
