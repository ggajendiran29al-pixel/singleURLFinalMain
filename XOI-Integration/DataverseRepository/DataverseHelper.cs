using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading.Tasks;
using XOI_Integration.DataverseRepository.Provider;

namespace XOI_Integration.DataverseRepository
{
    public class DataverseHelper
    {
        public static async Task<bool> EntityIsRelatedAsync(string relatedEntity, Guid bookableResourceBookingId)
        {
            QueryExpression query = new QueryExpression("bookableresourcebooking")
            {
                ColumnSet = new ColumnSet(relatedEntity),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
            {
                new ConditionExpression("bookableresourcebookingid", ConditionOperator.Equal, bookableResourceBookingId)
            }
                }
            };

            var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);

            bool isExist = false;
            foreach (var entity in response.Entities)
                if (entity.Attributes.Contains(relatedEntity))
                    isExist = true;

            return isExist;
        }
    }
}
