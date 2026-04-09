using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Threading.Tasks;
using System.Collections.Generic;
using XOI_Integration.DataverseRepository.Provider;

namespace XOI_Integration.DataFactory.BaseObject
{
    public abstract class JobRelatedData
    {
        public string AssigneeIds { get; set; }
        public string CustomerName { get; protected set; }
        public string JobLocation { get; protected set; }
        public string OrderNumber { get; protected set; }
        public string Label { get; protected set; }
        public string[] Tags { get; protected set; }
        public string[] TagSuggestions { get; protected set; }
        public string InternalNote { get; protected set; }

        protected Guid BookableResourceBookingId;

        public Guid WorkOrderId { get; set; }
        public Guid ProjectId { get; set; }

        protected JobRelatedData(Guid bookableResourceBookingId)
        {
            BookableResourceBookingId = bookableResourceBookingId;
        }

        public abstract Task LoadData();

        protected async Task<List<string>> GetResourcesAsync()
        {
            QueryExpression query = new QueryExpression("bookableresourcebooking")
            {
                ColumnSet = new ColumnSet("resource"),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                {
                    new ConditionExpression("bookableresourcebookingid", ConditionOperator.Equal, BookableResourceBookingId)
                }
                }
            };
            LinkEntity linkToResource = query.AddLink("bookableresource", "resource", "bookableresourceid", JoinOperator.Inner);
            linkToResource.Columns.AddColumns("userid");

            LinkEntity linkToSystemUser = linkToResource.AddLink("systemuser", "userid", "systemuserid", JoinOperator.Inner);
            linkToSystemUser.Columns.AddColumns("internalemailaddress");

            var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);
            List<string> resourceList = response.Entities
                .Select(entity => entity.GetAttributeValue<AliasedValue>("systemuser2.internalemailaddress").Value as string)
                .ToList();

            return resourceList;
        }
    }
}
