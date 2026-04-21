using System;
using System.Threading.Tasks;
using XOI_Integration.DataFactory.BaseObject;
using XOI_Integration.DataFactory.InheritedObjects.OperationsForInheritedObjects;

namespace XOI_Integration.DataFactory.InheritedObjects
{
    public class WorkOrder : JobRelatedData
    {
        public WorkOrder(Guid bookableResourceBookingId) : base(bookableResourceBookingId)
        {
        }

        public override async Task LoadData()
        {
            WorkOrderOperation operation = new WorkOrderOperation(BookableResourceBookingId);
            WorkOrderId = await operation.GetWorkOrderIdAsync();

            AssigneeIds = string.Join(",", await GetResourcesAsync());
            CustomerName = await operation.WorkOrderGetCustomerInfoAsync();
            JobLocation = await operation.WorkOrderGetJobLocationAsync();
            OrderNumber = $"WO-{await operation.WorkOrderGetProjectNumberAsync()}";
            Label = $"{CustomerName}\n{OrderNumber}\n{JobLocation}";
            Tags = Array.Empty<string>();
            TagSuggestions = Array.Empty<string>();
            InternalNote =
                string.IsNullOrEmpty(await operation.WorkOrderGetInternalNoteAsync())
                    ? "---"
                    : await operation.WorkOrderGetInternalNoteAsync();
        }
    }
}