using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XOI_Integration.DataFactory.BaseObject;
using XOI_Integration.DataFactory.InheritedObjects;
using XOI_Integration.DataFactory.InheritedObjects.OperationsForInheritedObjects;
using XOI_Integration.DataverseRepository.Provider;

namespace XOI_Integration.DataverseRepository
{
    public class BookableResourceChecker
    {
        public static async Task<(bool, Guid)> CheckForOtherResourcesAndJobIdAsync(Guid bookableResourceBookingId)
        {
            if (await DataverseHelper.EntityIsRelatedAsync("sis_projectref", bookableResourceBookingId))
            {
                ProjectOperation operation = new ProjectOperation(bookableResourceBookingId);
                var projectId = await operation.GetProjectIdAsync();
                return await CheckOtherResourcesAndJobIdAsync("sis_projectref", projectId, bookableResourceBookingId);
            }
            else if (await DataverseHelper.EntityIsRelatedAsync("msdyn_workorder", bookableResourceBookingId))
            {
                WorkOrderOperation operation = new WorkOrderOperation(bookableResourceBookingId);
                var workOrderId = await operation.GetWorkOrderIdAsync();
                return await CheckOtherResourcesAndJobIdAsync("msdyn_workorder", workOrderId, bookableResourceBookingId);
            }

            throw new ArgumentException("Invalid bookableResourceBookingId");
        }

        private static async Task<(bool, Guid)> CheckOtherResourcesAndJobIdAsync(string relatedEntity, Guid relatedEntityId, Guid currentBookableResourceBookingId)
        {
            var query = new QueryExpression("bookableresourcebooking")
            {
                ColumnSet = new ColumnSet("bookableresourcebookingid", "sisps_xoi_vision_jobid"),
                Criteria = new FilterExpression
                {
                    Conditions =
                {
                    new ConditionExpression(relatedEntity, ConditionOperator.Equal, relatedEntityId),
                    new ConditionExpression("bookableresourcebookingid", ConditionOperator.NotEqual, currentBookableResourceBookingId)
                }
                }
            };

            var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);
            foreach (var resource in response.Entities)
            {
                var visionJobId = resource?.GetAttributeValue<string>("sisps_xoi_vision_jobid");
                var copyFromBookableresourcebookingid = resource.GetAttributeValue<Guid>("bookableresourcebookingid");

                if (visionJobId != null)
                {
                    return (true, copyFromBookableresourcebookingid);
                }
            }

            return (false, Guid.Empty);
        }
    }



}
