    using Microsoft.Xrm.Sdk;
    using Microsoft.Xrm.Sdk.Query;
    using System;
    using System.Text;
    using System.Threading.Tasks;
    using XOI_Integration.DataverseRepository.Provider;
using static Grpc.Core.Metadata;

    namespace XOI_Integration.DataFactory.InheritedObjects.OperationsForInheritedObjects
    {
        public class WorkOrderOperation
        {
            private readonly Guid _bookableResourceBookingId;

            public WorkOrderOperation(Guid bookableResourceBookingId)
            {
                _bookableResourceBookingId = bookableResourceBookingId;
            }

            // -------------------------------------------------------
            // 🔹 Resolve WorkOrder from BRB
            // -------------------------------------------------------
            public async Task<Guid> GetWorkOrderIdAsync()
            {
                QueryExpression query = new QueryExpression("bookableresourcebooking")
                {
                    ColumnSet = new ColumnSet("msdyn_workorder"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("bookableresourcebookingid",
                                ConditionOperator.Equal, _bookableResourceBookingId)
                        }
                    }
                };

                var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);

                foreach (var entity in response.Entities)
                {
                    return entity.GetAttributeValue<EntityReference>("msdyn_workorder").Id;
                }

                return Guid.Empty;
            }


            // -------------------------------------------------------
            // 🔹 Context Helpers
            // -------------------------------------------------------

            private async Task<T> GetSingleAttributeValueAsync<T>(QueryExpression query, string attribute)
            {
                var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);

                foreach (var entity in response.Entities)
                {
                    return entity.GetAttributeValue<T>(attribute);
                }

                return default;
            }

            // -------------------------------------------------------
            // 🔹 CUSTOMER NAME
            // -------------------------------------------------------
            public async Task<string> WorkOrderGetCustomerInfoAsync()
            {
                Guid woId = await GetWorkOrderIdAsync();

                QueryExpression query = new QueryExpression("msdyn_workorder")
                {
                    ColumnSet = new ColumnSet("msdyn_serviceaccount"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("msdyn_workorderid",
                                ConditionOperator.Equal, woId)
                        }
                    }
                };

                var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);

                foreach (var entity in response.Entities)
                {
                    return entity.GetAttributeValue<EntityReference>("msdyn_serviceaccount")?.Name;
                }

                return null;
            }


            // -------------------------------------------------------
            // 🔹 FORMATTED ADDRESS
            // -------------------------------------------------------

            public async Task<string> WorkOrderGetJobLocationAsync()
            {
                Guid woId = await GetWorkOrderIdAsync();

                QueryExpression query = new QueryExpression("msdyn_workorder")
                {
                    ColumnSet = new ColumnSet(),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("msdyn_workorderid",
                                ConditionOperator.Equal, woId)
                        }
                    }
                };

                LinkEntity link = query.AddLink("account", "msdyn_serviceaccount", "accountid", JoinOperator.Inner);
                link.Columns.AddColumns(
                    "address1_line1",
                    "address1_line2",
                    "address1_line3",
                    "address1_city",
                    "address1_stateorprovince",
                    "address1_postalcode",
                    "address1_country"
                );

                return await GetFormattedAddressAsync(query);
            }

            private async Task<string> GetFormattedAddressAsync(QueryExpression query)
            {
                var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);

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

                    return sb.ToString();
                }

                return null;
            }

            private string GetAliasedValue(Entity entity, string alias)
            {
                if (entity.Attributes.TryGetValue(alias, out var obj) &&
                    obj is AliasedValue av &&
                    av.Value != null)
                {
                    return av.Value as string ?? "";
                }

                return "";
            }

            // -------------------------------------------------------
            // 🔹 PROJECT NUMBER
            // -------------------------------------------------------
            public async Task<string> WorkOrderGetProjectNumberAsync()
            {
                Guid woId = await GetWorkOrderIdAsync();

                QueryExpression query = new QueryExpression("msdyn_workorder")
                {
                    ColumnSet = new ColumnSet("msdyn_name"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("msdyn_workorderid",
                                ConditionOperator.Equal, woId)
                        }
                    }
                };

                return await GetSingleAttributeValueAsync<string>(query, "msdyn_name");
            }

            // -------------------------------------------------------
            // 🔹 INTERNAL NOTE
            // -------------------------------------------------------
            public async Task<string> WorkOrderGetInternalNoteAsync()
            {
                Guid woId = await GetWorkOrderIdAsync();

                QueryExpression query = new QueryExpression("msdyn_workorder")
                {
                    ColumnSet = new ColumnSet("msdyn_workordersummary"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("msdyn_workorderid",
                                ConditionOperator.Equal, woId)
                        }
                    }
                };

                return await GetSingleAttributeValueAsync<string>(query, "msdyn_workordersummary");
            }


            // -------------------------------------------------------
            // 🔥 XOi JOB ID STORAGE ON WORKORDER
            // -------------------------------------------------------

            public static async Task<string> GetXOiJobIdAsync(Guid workOrderId)
            {
                if (workOrderId == Guid.Empty)
                    return null;

                var entity = await Task.Run(() =>
                    DataverseApi.Instance.Retrieve(
                        "msdyn_workorder",
                        workOrderId,
                        new ColumnSet("acl_xoi_vision_jobid")
                    ));

                if (entity != null && entity.Contains("acl_xoi_vision_jobid"))
                    return (string)entity["acl_xoi_vision_jobid"];

                return null;
            }

        public static async Task UpdateXOiJobIdOnWorkOrderAsync(Guid workOrderId, string xOiJobId)
        {
            if (workOrderId == Guid.Empty || string.IsNullOrEmpty(xOiJobId))
                return;

            Entity wo = new Entity("msdyn_workorder") { Id = workOrderId };
            wo["acl_xoi_vision_jobid"] = xOiJobId;
            await Task.Run(() => DataverseApi.Instance.Update(wo));
        }

    }
}
