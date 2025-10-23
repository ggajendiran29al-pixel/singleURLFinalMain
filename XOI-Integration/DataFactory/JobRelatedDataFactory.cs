using System;
using System.Threading.Tasks;
using XOI_Integration.DataFactory.BaseObject;
using XOI_Integration.DataFactory.InheritedObjects;
using XOI_Integration.DataverseRepository;

namespace XOI_Integration.DataFactory
{
    public class JobRelatedDataFactory
    {
        public static async Task<JobRelatedData> CreateAsync(Guid bookableResourceBookingId)
        {
            DataverseHelper dataverseHelper = new DataverseHelper();

            if (await DataverseHelper.EntityIsRelatedAsync("sis_projectref", bookableResourceBookingId))
                return new Project(bookableResourceBookingId);
            else if (await DataverseHelper.EntityIsRelatedAsync("msdyn_workorder", bookableResourceBookingId))
                return new WorkOrder(bookableResourceBookingId);

            throw new ArgumentException("Invalid variable value");
        }
    }
}
