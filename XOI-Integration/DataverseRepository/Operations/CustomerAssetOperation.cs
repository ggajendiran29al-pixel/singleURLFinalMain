using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XOI_Integration.DataModels;
using XOI_Integration.DataModels.Enums;
using XOI_Integration.DataverseRepository.Provider;
using XOI_Integration.XOiRepository.XOiDataModels;

namespace XOI_Integration.DataverseRepository.Operations
{
    public class CustomerAssetOperation
    {
        private static readonly Dictionary<AssetProperties, Guid> properties = new Dictionary<AssetProperties, Guid>()
        {
            {AssetProperties.Make,  Guid.Parse("724543f8-7b9b-ee11-be36-000d3a31f202")},
            {AssetProperties.ModelNumber,  Guid.Parse("744543f8-7b9b-ee11-be36-000d3a31f202")},
            {AssetProperties.SerialNumber,  Guid.Parse("7e4543f8-7b9b-ee11-be36-000d3a31f202")},
            {AssetProperties.Transcript,  Guid.Parse("de2b1de8-f3e6-ee11-904c-6045bd04f81e")}
        };

        public static async Task UpdateCustomerAssetAsync(ILogger _log, List<CustomerAssetToUpdate> customerAssetsData, string cusomerName, string jobId)
        {
            _log.LogInformation("Start customer asset update");

            try
            {
                foreach (var customerAsset in customerAssetsData)
                {
                    await SetAssetPropertyAsync(_log, AssetProperties.Transcript, customerAsset.Transcript, customerAsset.AssetId);
                }

                _log.LogInformation("Finish customer asset update");

                await IntegrationLogOperation.CreateAssetsLogAsync(jobId, JobResponseResult.Success, OperationType.UpdateAsset, $"Assets was successfully updated for {cusomerName} customer.");

            }
            catch (Exception ex)
            {
                _log.LogInformation(ex.Message);

                await IntegrationLogOperation.CreateAssetsLogAsync(jobId, JobResponseResult.Failure, OperationType.UpdateAsset, ex.Message);
                throw;
            }
        }

        /*public static async Task CreateCustomerAssetAsync(ILogger _log, List<CustomerAssetToCreate> customerAssetsData, string cusomerName, string jobId)
        {
            _log.LogInformation("Start the creation of customer assets");

            var customerId = await GetCustomerGuidAsync(_log, cusomerName);
            string assetCategory = Environment.GetEnvironmentVariable("DefaultAssetCategory", EnvironmentVariableTarget.Process);

            // Get existing assets for this customer
            var existingAssetIds = await GetCustomerAssetIdsAsync(_log, cusomerName);

            var tasks = new List<Task>();
            try
            {
                foreach (var asset in customerAssetsData)
                {
                    *//*//gg logging 
                    _log.LogInformation($"[XOI] Checking Existing Asset For Acccount ={cusomerName},Make ={asset.Make}, Model={asset.Model}, Serial={asset.Serial}");
                        // Check if asset already exists for this customer by serial number
                        var query = new QueryExpression("msdyn_customerasset")
                        {
                            ColumnSet = new ColumnSet("msdyn_customerassetid"),
                            Criteria = new FilterExpression(LogicalOperator.And)
                            {
                                Conditions =
                        {
                            new ConditionExpression("msdyn_account", ConditionOperator.Equal, customerId),
                            new ConditionExpression("sis_serialid", ConditionOperator.Equal, asset.Serial),
                            new ConditionExpression("msdyn_name", ConditionOperator.Equal, $"{asset.Make} | {asset.Model} | {asset.Serial}")
                        }
                            }
                    };

                    var existingAssets = await DataverseApi.Instance.RetrieveMultipleAsync(query);

                    if (existingAssets.Entities.Any())
                    {
                        var existingAssetId = existingAssets.Entities.First().Id;
                        _log.LogInformation($"Asset already exists with ID {existingAssetId}. Updating properties.");

                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.Make, asset.Make, existingAssetId));
                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.ModelNumber, asset.Model, existingAssetId));
                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.SerialNumber, asset.Serial, existingAssetId));
                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.Transcript, asset.Transcript, existingAssetId));
                    }
                    else
                    {
                        _log.LogInformation($"Creating new asset for {asset.Make} | {asset.Model}");

                        Entity customerAsset = new Entity("msdyn_customerasset");
                        customerAsset["msdyn_name"] = $"{asset.Make} | {asset.Model}";
                        customerAsset["msdyn_account"] = new EntityReference("account", customerId);
                        customerAsset["msdyn_customerassetcategory"] = new EntityReference("msdyn_customerassetcategory", Guid.Parse(assetCategory));
                        customerAsset["msdyn_manufacturingdate"] = asset.ManufactureDate;

                        Guid customerAssetId = await DataverseApi.Instance.CreateAsync(customerAsset);

                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.Make, asset.Make, customerAssetId));
                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.ModelNumber, asset.Model, customerAssetId));
                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.SerialNumber, asset.Serial, customerAssetId));
                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.Transcript, asset.Transcript, customerAssetId));
                    }*//*
                    _log.LogInformation($"[XOI] Checking existing asset for Account={cusomerName}, Make={asset.Make}, Model={asset.Model}, Serial={asset.Serial}");

                    var query = new QueryExpression("msdyn_customerasset")
                    {
                        ColumnSet = new ColumnSet("msdyn_customerassetid"),
                        Criteria = new FilterExpression(LogicalOperator.And)
                        {
                            Conditions =
        {
            new ConditionExpression("msdyn_account", ConditionOperator.Equal, customerId),
            new ConditionExpression("sis_model", ConditionOperator.Equal, asset.Model),
            new ConditionExpression("sis_serialid", ConditionOperator.Equal, asset.Serial)
        }
                        }
                    };

                    // 🔹 Join to property log for Make
                    var linkToMake = query.AddLink("msdyn_propertylog", "msdyn_customerassetid", "msdyn_customerasset", JoinOperator.Inner);
                    linkToMake.EntityAlias = "makeprop";
                    linkToMake.LinkCriteria = new FilterExpression(LogicalOperator.And)
                    {
                        Conditions =
    {
        new ConditionExpression("msdyn_property", ConditionOperator.Equal, properties[AssetProperties.Make]),
        new ConditionExpression("msdyn_stringvalue", ConditionOperator.Equal, asset.Make)
    }
                    };

                    var existingAssets = await DataverseApi.Instance.RetrieveMultipleAsync(query);

                    if (existingAssets.Entities.Any())
                    {
                        var existingAssetId = existingAssets.Entities.First().Id;
                        _log.LogInformation($"✅ Asset already exists with ID {existingAssetId} (Make={asset.Make}, Model={asset.Model}, Serial={asset.Serial}). Updating properties.");

                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.Make, asset.Make, existingAssetId));
                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.ModelNumber, asset.Model, existingAssetId));
                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.SerialNumber, asset.Serial, existingAssetId));
                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.Transcript, asset.Transcript, existingAssetId));
                    }
                    else
                    {
                        _log.LogInformation($"🆕 Creating new asset for {asset.Make} | {asset.Model} | {asset.Serial}");

                        Entity customerAsset = new Entity("msdyn_customerasset");
                        customerAsset["msdyn_name"] = $"{asset.Make} | {asset.Model} | {asset.Serial}";
                        customerAsset["msdyn_account"] = new EntityReference("account", customerId);
                        customerAsset["msdyn_customerassetcategory"] = new EntityReference("msdyn_customerassetcategory", Guid.Parse(assetCategory));
                        customerAsset["sis_model"] = asset.Model;
                        customerAsset["sis_serialid"] = asset.Serial;
                        customerAsset["msdyn_manufacturingdate"] = asset.ManufactureDate;

                        Guid customerAssetId = await DataverseApi.Instance.CreateAsync(customerAsset);

                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.Make, asset.Make, customerAssetId));
                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.ModelNumber, asset.Model, customerAssetId));
                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.SerialNumber, asset.Serial, customerAssetId));
                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.Transcript, asset.Transcript, customerAssetId));
                    }

                }

                await Task.WhenAll(tasks);

                _log.LogInformation($"Finished processing customer assets for {cusomerName}");
                await IntegrationLogOperation.CreateAssetsLogAsync(jobId, JobResponseResult.Success, OperationType.CreateAsset, $"Assets processed for {cusomerName}.");
            }
            catch (Exception ex)
            {
                _log.LogInformation(ex.Message);
                await IntegrationLogOperation.CreateAssetsLogAsync(jobId, JobResponseResult.Failure, OperationType.CreateAsset, ex.Message);
                throw;
            }
        }
*/
        public static async Task CreateCustomerAssetAsync(ILogger _log, List<CustomerAssetToCreate> customerAssetsData, string customerName, string jobId)
        {
            _log.LogInformation("Start the creation of customer assets");

            var customerId = await GetCustomerGuidAsync(_log, customerName);
            string assetCategory = Environment.GetEnvironmentVariable("DefaultAssetCategory", EnvironmentVariableTarget.Process);

            var tasks = new List<Task>();

            try
            {
                foreach (var asset in customerAssetsData)
                {
                    _log.LogInformation($"[XOI] Checking existing asset for Account={customerName}, Make={asset.Make}, Model={asset.Model}, Serial={asset.Serial}");

                    /* //  Build the main query to find existing asset (using sis_model, sis_serialid) and Property Log
                      var query = new QueryExpression("msdyn_customerasset")
                      {
                          ColumnSet = new ColumnSet("msdyn_customerassetid"),
                          Criteria = new FilterExpression(LogicalOperator.And)
                          {
                              Conditions =
                      {
                          new ConditionExpression("msdyn_account", ConditionOperator.Equal, customerId),
                          new ConditionExpression("sis_model", ConditionOperator.Equal, asset.Model),
                          new ConditionExpression("sis_serialid", ConditionOperator.Equal, asset.Serial)
                      }
                          }
                      };

                      // 🔹 Join property log to match Make
                      var linkToMake = query.AddLink("msdyn_propertylog", "msdyn_customerassetid", "msdyn_customerasset", JoinOperator.Inner);
                      linkToMake.EntityAlias = "makeprop";
                      linkToMake.LinkCriteria = new FilterExpression(LogicalOperator.And)
                      {
                          Conditions =
                  {
                      new ConditionExpression("msdyn_property", ConditionOperator.Equal, properties[AssetProperties.Make]),
                      new ConditionExpression("msdyn_stringvalue", ConditionOperator.Equal, asset.Make)
                  }
                      };

                      var existingAssets = await DataverseApi.Instance.RetrieveMultipleAsync(query);
                    */
                    //Updated 23102025
                    //✅ Build the main query to find existing asset using full Make + Model + Serial combination
                 
                    var query = new QueryExpression("msdyn_customerasset")
                    {
                        ColumnSet = new ColumnSet("msdyn_customerassetid"),
                        Criteria = new FilterExpression(LogicalOperator.And)
                        {
                            Conditions =
        {
            // Match the same customer (Account)
            new ConditionExpression("msdyn_account", ConditionOperator.Equal, customerId)
        },
                            Filters =
        {
            // Match possible variations of the name (legacy and new)
            new FilterExpression(LogicalOperator.Or)
            {
                Conditions =
                {
                    // New format (with spaces)
                    new ConditionExpression("msdyn_name", ConditionOperator.Equal, $"{asset.Make} | {asset.Model} | {asset.Serial}"),

                    // Legacy format (no spaces)
                    new ConditionExpression("msdyn_name", ConditionOperator.Equal, $"{asset.Make}|{asset.Model}|{asset.Serial}"),
                }
            }
        }
                        }
                    };
                    // 🔹 Execute the query
                    var existingAssets = await DataverseApi.Instance.RetrieveMultipleAsync(query);

                    // 🔹 Log what was found
                    _log.LogInformation($"[XOI] Found {existingAssets.Entities.Count} existing asset(s) for {asset.Make} | {asset.Model} | {asset.Serial}");
                    foreach (var e in existingAssets.Entities)
                    {
                        _log.LogInformation($"Matched existing asset: {e.Id} | Name: {e.GetAttributeValue<string>("msdyn_name")}");
                    }
                    if (existingAssets.Entities.Any())
                    {
                        // ✅ Update existing asset properties
                        var existingAssetId = existingAssets.Entities.First().Id;
                        _log.LogInformation($"Asset already exists with ID {existingAssetId}. Updating properties.");

                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.Make, asset.Make, existingAssetId));
                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.ModelNumber, asset.Model, existingAssetId));
                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.SerialNumber, asset.Serial, existingAssetId));
                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.Transcript, asset.Transcript, existingAssetId));
                    }
                    else
                    {
                        // ✅ Create new unique asset
                        _log.LogInformation($"🆕 Creating new asset for {asset.Make} | {asset.Model} | {asset.Serial}");

                        Entity customerAsset = new Entity("msdyn_customerasset");
                        customerAsset["msdyn_name"] = $"{asset.Make} | {asset.Model} | {asset.Serial}";
                        customerAsset["msdyn_account"] = new EntityReference("account", customerId);
                        customerAsset["msdyn_customerassetcategory"] = new EntityReference("msdyn_customerassetcategory", Guid.Parse(assetCategory));
                        customerAsset["msdyn_manufacturingdate"] = asset.ManufactureDate;

                        // Include model and serial as schema fields
                        customerAsset["sis_model"] = asset.Model;
                        customerAsset["sis_serialid"] = asset.Serial;

                        Guid customerAssetId;

                        try
                        {
                            customerAssetId = await DataverseApi.Instance.CreateAsync(customerAsset);
                        }
                        catch (Exception ex)
                        {
                            // Catch plugin duplicate exceptions gracefully
                            if (ex.Message.Contains("AssetNameDuplicateCheckOnPropertyLogPlugin"))
                            {
                                _log.LogWarning($"Duplicate detected by plugin for {asset.Make} | {asset.Model} | {asset.Serial}. Skipping creation.");
                                continue;
                            }
                            throw;
                        }

                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.Make, asset.Make, customerAssetId));
                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.ModelNumber, asset.Model, customerAssetId));
                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.SerialNumber, asset.Serial, customerAssetId));
                        tasks.Add(SetAssetPropertyAsync(_log, AssetProperties.Transcript, asset.Transcript, customerAssetId));
                    }
                }

                await Task.WhenAll(tasks);

                _log.LogInformation($"Finished processing customer assets for {customerName}");
                await IntegrationLogOperation.CreateAssetsLogAsync(jobId, JobResponseResult.Success, OperationType.CreateAsset, $"Assets processed for {customerName}.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error during asset creation.");
                await IntegrationLogOperation.CreateAssetsLogAsync(jobId, JobResponseResult.Failure, OperationType.CreateAsset, ex.Message);
                throw;
            }
        }

        public static async Task<List<Guid>> GetCustomerAssetIdsAsync(ILogger _log, string customerName)
        {
            _log.LogInformation("Start to get the customer asset IDs");

            QueryExpression query = new QueryExpression("account");
            query.ColumnSet = new ColumnSet("accountid");
            query.Criteria.AddCondition("name", ConditionOperator.Equal, customerName);

            LinkEntity linkToAsset = query.AddLink("msdyn_customerasset", "accountid", "msdyn_account", JoinOperator.Inner);
            linkToAsset.Columns.AddColumns("msdyn_customerassetid");
            linkToAsset.EntityAlias = "asset";

            var customerAssets = await DataverseApi.Instance.RetrieveMultipleAsync(query);

            List<Guid> customerAssetIds = new List<Guid>();

            foreach (var result in customerAssets.Entities)
            {
                if (result.Attributes.Contains("asset.msdyn_customerassetid"))
                {
                    var customerAssetId = (Guid)result.GetAttributeValue<AliasedValue>("asset.msdyn_customerassetid").Value;

                    _log.LogInformation($"Receive customer asset with ID {customerAssetId}");

                    customerAssetIds.Add(customerAssetId);
                }
            }

            _log.LogInformation("Finish to get the customer asset IDs");

            return customerAssetIds;
        }

        public static async Task<List<RelatedAssetProperty>> GetCustomerAssetPropertiesAsync(ILogger _log, List<Guid> customerAssetIds)
        {
            _log.LogInformation("Start to get the customer asset properties");

            var relatedAssetProperties = new List<RelatedAssetProperty>();

            foreach (var assetId in customerAssetIds)
            {
                QueryExpression propertyQuery = new QueryExpression("msdyn_propertylog");
                propertyQuery.ColumnSet = new ColumnSet("msdyn_property", "msdyn_stringvalue");
                propertyQuery.Criteria.AddCondition("msdyn_customerasset", ConditionOperator.Equal, assetId);

                var properties = await DataverseApi.Instance.RetrieveMultipleAsync(propertyQuery);

                var relatedAssetProperty = new RelatedAssetProperty();

                relatedAssetProperty.AssetId = assetId;

                foreach (var property in properties.Entities)
                {
                    var propertyName = property.GetAttributeValue<EntityReference>("msdyn_property").Name;
                    var propertyValue = property.GetAttributeValue<string>("msdyn_stringvalue");

                    switch (propertyName)
                    {
                        case "Make":
                            relatedAssetProperty.Make = propertyValue;
                            break;
                        case "Model Number":
                            relatedAssetProperty.Model = propertyValue;
                            break;
                        case "Serial Number":
                            relatedAssetProperty.Serial = propertyValue;
                            break;
                        case "Transcript":
                            relatedAssetProperty.Transcript = propertyValue;
                            break;
                    }
                }

                _log.LogInformation($"Property for Asset with Id:{assetId}\n" +
                    $"Make = {relatedAssetProperty.Make};\n" +
                    $"Model = {relatedAssetProperty.Model};\n" +
                    $"Serial = {relatedAssetProperty.Serial};");

                relatedAssetProperties.Add(relatedAssetProperty);
            }

            _log.LogInformation("Finish to get the customer asset properties");

            return relatedAssetProperties;
        }
        //gg updated code
        public static async Task<Guid> GetCustomerGuidAsync(ILogger _log, string customerName)
        {
            _log.LogInformation($"Start getting customer ID with name {customerName}");

            QueryExpression query = new QueryExpression("account")
            {
                ColumnSet = new ColumnSet("accountid"),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression("name", ConditionOperator.Equal, customerName)
                    }
                }
            };

            var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);

            var customerId = response.Entities.FirstOrDefault().GetAttributeValue<Guid>("accountid");

            _log.LogInformation($"Customer with name {customerName}, has ID value: {customerId}");

            _log.LogInformation("Finish getting customer ID");

            return customerId;
        }
        //gg changed this class
        public static async Task SetAssetPropertyAsync(ILogger _log, AssetProperties assetProperty, string value, Guid customerAssetId)
        {
            _log.LogInformation($"Setting property {assetProperty} for Asset {customerAssetId} with value '{value}'");

            // Check if the property log already exists for this asset, property, and value
            var existingPropertyQuery = new QueryExpression("msdyn_propertylog")
            {
                ColumnSet = new ColumnSet("msdyn_propertylogid"),
                Criteria = new FilterExpression
                {
                    Conditions =
            {
                new ConditionExpression("msdyn_customerasset", ConditionOperator.Equal, customerAssetId),
                new ConditionExpression("msdyn_property", ConditionOperator.Equal, properties[assetProperty]),
                new ConditionExpression("msdyn_stringvalue", ConditionOperator.Equal, value)
            }
                }
            };

            var existingProperty = await DataverseApi.Instance.RetrieveMultipleAsync(existingPropertyQuery);

            if (existingProperty.Entities.Any())
            {
                _log.LogInformation($"✅ Property log for {assetProperty} already exists on Asset {customerAssetId}. Skipping creation.");
                return;
            }

            // Create the property log if it doesn't exist
            Entity assetPropertiesEntity = new Entity("msdyn_propertylog")
            {
                ["msdyn_customerasset"] = new EntityReference("msdyn_customerasset", customerAssetId),
                ["msdyn_property"] = new EntityReference("msdyn_property", properties[assetProperty]),
                ["msdyn_stringvalue"] = value
            };

            await DataverseApi.Instance.CreateAsync(assetPropertiesEntity);
            _log.LogInformation($"Created property log for {assetProperty} on Asset {customerAssetId}");
        }

        //Commented to Property Log based duplication
        /* public static async Task SetAssetPropertyAsync(ILogger _log, AssetProperties assetProperty, string value, Guid customerAssetId)
         {
             _log.LogInformation($"Property assets {assetProperty} to Customer {customerAssetId} with value {value}");

             Entity assetProperties = new Entity("msdyn_propertylog");
             assetProperties["msdyn_customerasset"] = new EntityReference("msdyn_customerasset", customerAssetId);
             assetProperties["msdyn_property"] = new EntityReference("msdyn_property", properties[assetProperty]);
             assetProperties["msdyn_stringvalue"] = value;

             await DataverseApi.Instance.CreateAsync(assetProperties);
         }*/
        //gg
        public static async Task AssociateAssetToWorkOrderIncidentAsync(ILogger _log, Guid assetId, List<Guid> bookingIds)
        {
            if (bookingIds == null || !bookingIds.Any())
            {
                _log.LogWarning("No booking IDs provided to associate the asset.");
                return;
            }

            _log.LogInformation($"Associating asset {assetId} to Work Order Incidents for {bookingIds.Count} booking(s).");

            foreach (var bookingId in bookingIds)
            {
                // 1. Retrieve the booking to get the related Work Order
                var booking = await DataverseApi.Instance.RetrieveAsync(
                    "bookableresourcebooking",
                    bookingId,
                    new ColumnSet("msdyn_workorder")
                );
             
                if (booking == null || !booking.Contains("msdyn_workorder"))
                {
                    _log.LogWarning($"No Work Order found for Booking ID {bookingId}");
                    continue;
                }


                var workOrderRef = booking.GetAttributeValue<EntityReference>("msdyn_workorder");
              


                // 2. Retrieve incidents associated with this Work Order
                var incidentQuery = new QueryExpression("msdyn_workorderincident")
                {
                    ColumnSet = new ColumnSet("msdyn_workorderincidentid"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                {
                    new ConditionExpression("msdyn_workorder", ConditionOperator.Equal, workOrderRef.Id)
                }
                    }
                };

                var incidentResponse = await DataverseApi.Instance.RetrieveMultipleAsync(incidentQuery);

                if (incidentResponse.Entities.Count == 0)
                {
                    _log.LogWarning($"No Incident found for Work Order {workOrderRef.Id} (Booking ID {bookingId})");
                    continue;
                }

                // 3. Associate the asset to each incident
                foreach (var incident in incidentResponse.Entities)
                {
                    Entity incidentToUpdate = new Entity("msdyn_workorderincident", incident.Id)
                    {
                        ["msdyn_customerasset"] = new EntityReference("msdyn_customerasset", assetId)
                    };

                    await DataverseApi.Instance.UpdateAsync(incidentToUpdate);
                    _log.LogInformation($"✅ Associated asset {assetId} to Work Order Incident {incident.Id} (Booking ID {bookingId})");
                }
            }

            _log.LogInformation("Finished associating asset to Work Order Incidents for all bookings.");
        }

        /*public static async Task AssociateAssetToWorkOrderIncidentAsync(ILogger _log, Guid assetId, XOiJobInfo xOiJobInfo)
        {
            _log.LogInformation($"Associating asset {assetId} to Work Order Incident for job {xOiJobInfo.OrderNumber}");

            // Use the actual field containing XOi job ID
            var bookingIds = await BookableResourceBookingOperation.GetBookableResourceBookingIdsAsync(xOiJobInfo.OrderNumber);

            // Instead, query using sisps_xoi_vision_jobid
            var query = new QueryExpression("bookableresourcebooking")
            {
                ColumnSet = new ColumnSet("bookableresourcebookingid", "msdyn_workorder"),
                Criteria = new FilterExpression
                {
                    Conditions =
            {
                new ConditionExpression("sisps_xoi_vision_jobid", ConditionOperator.Equal, xOiJobInfo.OrderNumber)
            }
                }
            };

            var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);
            var bookings = response.Entities;

            if (!bookings.Any())
            {
                _log.LogWarning($"No bookings found for XOi job ID {xOiJobInfo.OrderNumber}");
                return;
            }

            foreach (var booking in bookings)
            {
                if (!booking.Contains("msdyn_workorder"))
                {
                    _log.LogWarning($"No Work Order found for Booking {booking.Id}");
                    continue;
                }

                var workOrderRef = booking.GetAttributeValue<EntityReference>("msdyn_workorder");

                // Get Work Order Incidents
                var incidentQuery = new QueryExpression("msdyn_workorderincident")
                {
                    ColumnSet = new ColumnSet("msdyn_workorderincidentid"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                {
                    new ConditionExpression("msdyn_workorder", ConditionOperator.Equal, workOrderRef.Id)
                }
                    }
                };

                var incidentResponse = await DataverseApi.Instance.RetrieveMultipleAsync(incidentQuery);

                foreach (var incident in incidentResponse.Entities)
                {
                    Entity incidentToUpdate = new Entity("msdyn_workorderincident", incident.Id);
                    incidentToUpdate["msdyn_customerasset"] = new EntityReference("msdyn_customerasset", assetId);

                    await DataverseApi.Instance.UpdateAsync(incidentToUpdate);
                    _log.LogInformation($"✅ Associated asset {assetId} to Work Order Incident {incident.Id}");
                }
            }
        }*/

        /* public static async Task AssociateAssetToWorkOrderIncidentAsync(ILogger _log, Guid assetId, XOiJobInfo xOiJobInfo)
         {
             _log.LogInformation($"Associating asset {assetId} to Work Order Incident");

             var bookingIds = await BookableResourceBookingOperation.GetBookableResourceBookingIdsAsync(xOiJobInfo.OrderNumber);

             foreach (var bookingId in bookingIds)
             {
                 // 1. Get Work Order from Booking
                 var booking = await DataverseApi.Instance.RetrieveAsync(
                     "bookableresourcebooking",
                     bookingId,
                     new ColumnSet("msdyn_workorder")
                 );

                 if (booking == null || !booking.Contains("msdyn_workorder"))
                 {
                     _log.LogWarning($"No Work Order found for Booking {bookingId}");
                     continue;
                 }

                 var workOrderRef = booking.GetAttributeValue<EntityReference>("msdyn_workorder");
                 if (workOrderRef == null)
                 {
                     _log.LogWarning($"Booking {bookingId} has no Work Order attached.");
                     continue;
                 }
                 _log.LogInformation($"Booking {bookingId} is linked to Work Order {workOrderRef.Id}");

                 // 2. Get Work Order Incident(s) for this Work Order
                 var incidentQuery = new QueryExpression("msdyn_workorderincident")
                 {
                     ColumnSet = new ColumnSet("msdyn_workorderincidentid"),
                     Criteria = new FilterExpression
                     {
                         Conditions =
                 {
                     new ConditionExpression("msdyn_workorder", ConditionOperator.Equal, workOrderRef.Id)
                 }
                     }
                 };

                 var incidentResponse = await DataverseApi.Instance.RetrieveMultipleAsync(incidentQuery);
                 _log.LogInformation($"Found {incidentResponse.Entities.Count} Incident(s) for Work Order {workOrderRef.Id}");


                 if (incidentResponse.Entities.Count == 0)
                 {
                     _log.LogWarning($"No Incident found for Work Order {workOrderRef.Id}");
                     continue;
                 }

                 // 3. Link asset to each Incident found
                 foreach (var incident in incidentResponse.Entities)

                 {
                     Entity incidentToUpdate = new Entity("msdyn_workorderincident", incident.Id);
                     incidentToUpdate["msdyn_customerasset"] = new EntityReference("msdyn_customerasset", assetId);

                     await DataverseApi.Instance.UpdateAsync(incidentToUpdate);
                     _log.LogInformation($"✅ Associated asset {assetId} to Work Order Incident {incident.Id}");
                 }*/
    }
}
