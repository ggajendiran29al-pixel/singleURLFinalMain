using System;
using System.Linq;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Threading.Tasks;
using XOI_Integration.DataModels;
using XOI_Integration.DataverseRepository.Provider;

namespace XOI_Integration.DataFactory.InheritedObjects.OperationsForInheritedObjects
{
    public class ProjectOperation
    {
        private readonly Guid _bookableResourceBookingId;

        public ProjectOperation(Guid bookableResourceBookingId)
        {
            _bookableResourceBookingId = bookableResourceBookingId;
        }

        public async Task<string> ProjectGetCustomerNameAsync()
        {
            CustomerInfo customerInfo = await ProjectGetCustomerInfoAsync();
            return customerInfo.Name;
        }

        public async Task<Guid> GetProjectIdAsync()
        {
            QueryExpression query = new QueryExpression("bookableresourcebooking")
            {
                ColumnSet = new ColumnSet("sis_projectref"),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                {
                    new ConditionExpression("bookableresourcebookingid", ConditionOperator.Equal, _bookableResourceBookingId)
                }
                }
            };

            var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);

            return response.Entities.FirstOrDefault()?.GetAttributeValue<EntityReference>("sis_projectref")?.Id ?? Guid.Empty;
        }

        public async Task<CustomerInfo> ProjectGetCustomerInfoAsync()
        {
            Guid projectId = await GetProjectIdAsync();

            QueryExpression query = new QueryExpression("sis_project")
            {
                ColumnSet = new ColumnSet("sis_worksite", "sis_customer"),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                {
                    new ConditionExpression("sis_projectid", ConditionOperator.Equal, projectId)
                }
                }
            };

            var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);

            CustomerInfo customerInfo = new CustomerInfo();

            foreach (var entity in response.Entities)
            {
                if (entity.Attributes.Contains("sis_worksite"))
                {
                    customerInfo.Name = entity.GetAttributeValue<EntityReference>("sis_worksite").Name;
                    customerInfo.Id = entity.GetAttributeValue<EntityReference>("sis_worksite").Id;
                }
                else if (entity.Attributes.Contains("sis_customer"))
                {
                    customerInfo.Name = entity.GetAttributeValue<EntityReference>("sis_customer").Name;
                    customerInfo.Id = entity.GetAttributeValue<EntityReference>("sis_customer").Id;
                }
                else
                {
                    customerInfo.Name = "Unspecified";
                    customerInfo.Id = Guid.Empty;
                }
            }

            return customerInfo;
        }

        public async Task<string> ProjectGetJobLocationAsync(CustomerInfo customerInfo)
        {
            if (customerInfo.Id == Guid.Empty)
            {
                return "Unspecified job location.";
            }

            QueryExpression query = new QueryExpression("account")
            {
                ColumnSet = new ColumnSet("address1_line1", "address1_city", "address1_stateorprovince", "address1_postalcode", "address1_country"),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression("accountid", ConditionOperator.Equal, customerInfo.Id)
                    }
                }
            };

            var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);

            StringBuilder sb = new StringBuilder();

            foreach (var entity in response.Entities)
            {
                sb.AppendLine($"{entity.GetAttributeValue<string>("address1_line1")} ");
                sb.Append($"{entity.GetAttributeValue<string>("address1_city")} ");
                sb.Append(entity.GetAttributeValue<string>("address1_stateorprovince"));
                sb.AppendLine(entity.GetAttributeValue<string>("address1_postalcode"));
                sb.Append(entity.GetAttributeValue<string>("address1_country"));
            }

            return sb.ToString();
        }

        public async Task<string> ProjectGetProjectNumberAsync()
        {
            Guid projectId = await GetProjectIdAsync();

            QueryExpression query = new QueryExpression("sis_project")
            {
                ColumnSet = new ColumnSet("sis_projectnumber"),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression("sis_projectid", ConditionOperator.Equal, projectId)
                    }
                }
            };

            var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);

            return response.Entities.FirstOrDefault()?.GetAttributeValue<string>("sis_projectnumber");
        }

        public async Task<string> ProjectGetInternalNoteAsync()
        {
            QueryExpression query = new QueryExpression("bookableresourcebooking")
            {
                ColumnSet = new ColumnSet("sis_workdetails"),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression("bookableresourcebookingid", ConditionOperator.Equal, _bookableResourceBookingId)
                    }
                }
            };

            var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);

            return response.Entities.FirstOrDefault()?.GetAttributeValue<string>("sis_workdetails");
        }

        // -------------------------------------------------------
        // XOi JOB ID STORAGE ON PROJECT
        // -------------------------------------------------------
        public static async Task<string> GetXOiJobIdAsync(Guid projectId)
        {
            if (projectId == Guid.Empty)
                return null;

            var entity = await Task.Run(() =>
                DataverseApi.Instance.Retrieve(
                    "sis_project",
                    projectId,
                    new Microsoft.Xrm.Sdk.Query.ColumnSet("acl_xoi_vision_jobid")
                ));

            return entity?.GetAttributeValue<string>("acl_xoi_vision_jobid");
        }

        public static async Task UpdateXOiJobIdOnProjectAsync(Guid projectId, string xOiJobId)
        {
            if (projectId == Guid.Empty || string.IsNullOrEmpty(xOiJobId))
                return;

            Entity project = new Entity("sis_project") { Id = projectId };
            project["acl_xoi_vision_jobid"] = xOiJobId;
            await Task.Run(() => DataverseApi.Instance.Update(project));
        }
    }
}
