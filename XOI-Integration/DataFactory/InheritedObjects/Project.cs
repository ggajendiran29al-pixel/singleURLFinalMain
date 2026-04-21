using System;
using System.Threading.Tasks;
using XOI_Integration.DataModels;
using XOI_Integration.DataFactory.BaseObject;
using XOI_Integration.DataFactory.InheritedObjects.OperationsForInheritedObjects;

namespace XOI_Integration.DataFactory.InheritedObjects
{
    public class Project : JobRelatedData
    {
        public Project(Guid bookableResourceBookingId) : base(bookableResourceBookingId)
        {
        }

        public override async Task LoadData()
        {
            ProjectOperation operation = new ProjectOperation(BookableResourceBookingId);
            CustomerInfo customerInfo = await operation.ProjectGetCustomerInfoAsync();

            ProjectId = await operation.GetProjectIdAsync();
            AssigneeIds = string.Join(",", await GetResourcesAsync());
            CustomerName = CustomerName = customerInfo.Name;
            JobLocation = await operation.ProjectGetJobLocationAsync(customerInfo);
            OrderNumber = $"PR-{await operation.ProjectGetProjectNumberAsync()}";
            Label = $"{CustomerName}\n{OrderNumber}\n{JobLocation}";
            Tags = Array.Empty<string>();
            TagSuggestions = Array.Empty<string>();
            InternalNote = string.IsNullOrEmpty(await operation.ProjectGetInternalNoteAsync())
                ? "---"
                : await operation.ProjectGetInternalNoteAsync();
        }
    }
}