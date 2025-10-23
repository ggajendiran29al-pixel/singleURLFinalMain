using System;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Threading.Tasks;
using XOI_Integration.DataverseRepository.Provider;

namespace XOI_Integration.DataFactory.InheritedObjects.OperationsForInheritedObjects
{
    public class WorkOrderOperation
    {
        private readonly Guid _bookableResourceBookingId;

        public WorkOrderOperation(Guid bookableResourceBookingId)
        {
            _bookableResourceBookingId = bookableResourceBookingId;
        }

        public async Task<Guid> GetWorkOrderIdAsync()
        {
            QueryExpression query = new QueryExpression("bookableresourcebooking")
            {
                ColumnSet = new ColumnSet("msdyn_workorder"),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                {
                    new ConditionExpression("bookableresourcebookingid", ConditionOperator.Equal, _bookableResourceBookingId)
                }
                }
            };

            var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);

            Guid workOrderId = Guid.Empty;
            foreach (var entity in response.Entities)
            {
                workOrderId = entity.GetAttributeValue<EntityReference>("msdyn_workorder").Id;
            }

            return workOrderId;
        }

        private async Task<T> GetSingleAttributeValueAsync<T>(QueryExpression query, string attributeName)   //TODO: delete method, make return existing methods by LinQ
        {
            var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);

            T attributeValue = default;
            foreach (var entity in response.Entities)
            {
                attributeValue = entity.GetAttributeValue<T>(attributeName);
            }

            return attributeValue;
        }

        public async Task<string> WorkOrderGetCustomerInfoAsync()
        {
            Guid workOrderId = await GetWorkOrderIdAsync();

            QueryExpression query = new QueryExpression("msdyn_workorder")
            {
                ColumnSet = new ColumnSet("msdyn_serviceaccount"),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                {
                    new ConditionExpression("msdyn_workorderid", ConditionOperator.Equal, workOrderId)
                }
                }
            };

            var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);

            string customerName = default;
            foreach (var entity in response.Entities)
            {
                customerName = entity.GetAttributeValue<EntityReference>("msdyn_serviceaccount").Name;
            }

            return customerName;
        }

        public async Task<string> WorkOrderGetJobLocationAsync()
        {
            Guid workOrderId = await GetWorkOrderIdAsync();

            QueryExpression query = new QueryExpression("msdyn_workorder")
            {
                ColumnSet = new ColumnSet(),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                {
                    new ConditionExpression("msdyn_workorderid", ConditionOperator.Equal, workOrderId)
                }
                }
            };

            LinkEntity linkToAccount = query.AddLink("account", "msdyn_serviceaccount", "accountid", JoinOperator.Inner);

            linkToAccount.Columns.AddColumns(
                "address1_line1",
                "address1_line2",
                "address1_line3",
                "address1_city",
                "address1_stateorprovince",
                "address1_postalcode",
                "address1_country"
             );

            string address = await GetFormattedAddressAsync(query);

            return address;
        }

        public async Task<string> WorkOrderGetProjectNumberAsync()
        {
            Guid workOrderId = await GetWorkOrderIdAsync();

            QueryExpression query = new QueryExpression("msdyn_workorder")
            {
                ColumnSet = new ColumnSet("msdyn_name"),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                 {
                     new ConditionExpression("msdyn_workorderid", ConditionOperator.Equal, workOrderId)
                 }
                }
            };

            string workOrderNumber = await GetSingleAttributeValueAsync<string>(query, "msdyn_name");

            return workOrderNumber;
        }

        public async Task<string> WorkOrderGetInternalNoteAsync()
        {
            Guid workOrderId = await GetWorkOrderIdAsync();

            QueryExpression query = new QueryExpression("msdyn_workorder")
            {
                ColumnSet = new ColumnSet("msdyn_workordersummary"),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                 {
                     new ConditionExpression("msdyn_workorderid", ConditionOperator.Equal, workOrderId)
                 }
                }
            };

            string internalNote = await GetSingleAttributeValueAsync<string>(query, "msdyn_workordersummary");

            return internalNote;
        }
        // Commneted By GG on 19/08/2025
        /* private async Task<string> GetFormattedAddressAsync(QueryExpression query)
         {
             var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);

             string address = string.Empty;
             foreach (var entity in response.Entities)
             {
                 StringBuilder sb = new StringBuilder();
                 sb.AppendLine(entity.GetAttributeValue<AliasedValue>("account1.address1_line1").Value as string);
                 if (entity.Attributes.Contains("account1.address1_line2")) sb.AppendLine(entity.GetAttributeValue<AliasedValue>("account1.address1_line2").Value as string);
                 if (entity.Attributes.Contains("account1.address1_line3")) sb.AppendLine(entity.GetAttributeValue<AliasedValue>("account1.address1_line3").Value as string);
                 sb.Append($"{entity.GetAttributeValue<AliasedValue>("account1.address1_city").Value as string} ");
                 sb.Append($"{entity.GetAttributeValue<AliasedValue>("account1.address1_stateorprovince").Value as string} ");
                 sb.AppendLine(entity.GetAttributeValue<AliasedValue>("account1.address1_postalcode").Value as string);
                 sb.Append(entity.GetAttributeValue<AliasedValue>("account1.address1_country").Value as string);

                 address = sb.ToString();
             }

             return address;
         }*/
        // Started By GG on 19/08/2025
        private async Task<string> GetFormattedAddressAsync(QueryExpression query)
        {
            var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);

            string address = string.Empty;

            foreach (var entity in response.Entities)
            {
                StringBuilder sb = new StringBuilder();

                string line1 = GetAliasedValue(entity, "account1.address1_line1");
                if (!string.IsNullOrEmpty(line1)) sb.AppendLine(line1);
                string line2 = GetAliasedValue(entity, "account1.address1_line2");
                if (!string.IsNullOrWhiteSpace(line2)) sb.AppendLine(line2);
                string line3 = GetAliasedValue(entity, "account1.address1_line3");
                if (!string.IsNullOrWhiteSpace(line3)) sb.AppendLine(line3);

                string city = GetAliasedValue(entity, "account1.address1_city");
                string state = GetAliasedValue(entity, "account1.address1_stateorprovince");
                string postal = GetAliasedValue(entity, "account1.address1_postalcode");
                string country = GetAliasedValue(entity, "account1.address1_country");

                sb.AppendLine($"{city} {state} {postal}".Trim());
                sb.Append(country);

                address = sb.ToString();
            }

            return address;
        }
        // Started By GG on 19/08/2025
        // Helper method to extract aliased string values
        private string GetAliasedValue(Entity entity, string alias)
        {
            if (entity.Attributes.TryGetValue(alias, out var aliasedObj) && aliasedObj is AliasedValue av && av.Value != null)
            {
                return av.Value as string ?? string.Empty;
            }

            return string.Empty;
        }

    }
}
