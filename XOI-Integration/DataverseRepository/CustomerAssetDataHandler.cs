using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XOI_Integration.DataModels;
using XOI_Integration.DataModels.Enums;
using XOI_Integration.DataverseRepository.Operations;
using XOI_Integration.DataverseRepository.Provider;
using XOI_Integration.XOiRepository.XOiDataModels;

namespace XOI_Integration.DataverseRepository
{
    public class CustomerAssetDataHandler
    {
        private ILogger _log;

        public CustomerAssetDataHandler(ILogger log)
        {
            _log = log;
        }

        public async Task HandleCustomerAssetDataAsync(
            List<XOiToCustomerAssetData> customerAssetData,
            XOiJobInfo xOiJobInfo,
            string jobId)
        {
            if (customerAssetData == null || !customerAssetData.Any())
            {
                _log.LogInformation("No customer assets to process.");
                return;
            }

            _log.LogInformation($"Start processing {customerAssetData.Count} customer assets for job {jobId}");

            var customerId = await CustomerAssetOperation.GetCustomerGuidAsync(_log, xOiJobInfo.CustomerName);
            string assetCategory = Environment.GetEnvironmentVariable("DefaultAssetCategory", EnvironmentVariableTarget.Process);

            foreach (var asset in customerAssetData)
            {
                //string assetName = $"{asset.Make} | {asset.Model} | {asset.Serial}";
                
                string assetName = $"{asset.Make} | {asset.Model} | {asset.Serial}".Trim();
                _log.LogInformation($"Processing asset: {assetName}");
                if (string.IsNullOrWhiteSpace(asset.Make)
                    && string.IsNullOrWhiteSpace(asset.Model)
                    && string.IsNullOrWhiteSpace(asset.Serial))
                {
                    _log.LogWarning("Skipping asset creation — Make, Model, and Serial are all empty.");
                    continue;
                }


                // Step 1: Try to find existing asset
                var query = new QueryExpression("msdyn_customerasset")
                {
                    ColumnSet = new ColumnSet("msdyn_customerassetid"),
                    Criteria = new FilterExpression(LogicalOperator.And)
                    {
                        Conditions =
        {
            new ConditionExpression("msdyn_account", ConditionOperator.Equal, customerId),
            new ConditionExpression("sis_serialid", ConditionOperator.Equal, asset.Serial)
        }
                    }
                };


                var existingAssets = await DataverseApi.Instance.RetrieveMultipleAsync(query);
                Guid assetId;

                if (existingAssets.Entities.Any())
                {
                    // Asset already exists
                    var existingAsset = existingAssets.Entities.First();
                    assetId = existingAsset.Id;

                    _log.LogInformation($"Existing asset found: {assetName} ({assetId})");

                    // Important: Pass asset ID to summary so BU update can happen later
                    if (xOiJobInfo.WorkSummary != null)
                        xOiJobInfo.WorkSummary.CustomerAssetId = assetId;

                    if (!string.IsNullOrEmpty(asset.Transcript))
                    {
                        _log.LogInformation($"Updating transcript for existing asset {assetId}");

                        await CustomerAssetOperation.UpdateCustomerAssetAsync(
                            _log,
                            new List<CustomerAssetToUpdate>
                            {
                                new CustomerAssetToUpdate
                                {
                                    AssetId = assetId,
                                    Transcript = asset.Transcript
                                }
                            },
                            xOiJobInfo.CustomerName,
                            jobId
                        );
                    }
                }
                else
                {
                    // Step 2: Create new asset
                    _log.LogInformation($"Creating new asset: {assetName}");

                    Entity newAsset = new Entity("msdyn_customerasset")
                    {
                        ["msdyn_name"] = assetName,
                        ["msdyn_account"] = new EntityReference("account", customerId),
                        ["msdyn_customerassetcategory"] = new EntityReference("msdyn_customerassetcategory", Guid.Parse(assetCategory))
                    };

                    if (asset.ManufactureDate != null)
                        newAsset["msdyn_manufacturingdate"] = asset.ManufactureDate;

                    newAsset["sis_model"] = asset.Model;
                    newAsset["sis_serialid"] = asset.Serial;

                    // Step 3: Apply BU before creation
                    _log.LogInformation("[BU] Attempting to resolve owning team for new asset");

                    var bookingIds = await BookableResourceBookingOperation.GetBookableResourceBookingIdsAsync(jobId);

                    if (bookingIds != null && bookingIds.Any())
                    {
                        var owningTeamId = await CustomerAssetOperation.GetOwningTeamFromBookingAsync(
                            _log,
                            bookingIds.First());

                        if (owningTeamId.HasValue)
                        {
                            newAsset["ownerid"] = new EntityReference("team", owningTeamId.Value);
                            _log.LogInformation($"[BU] Owning team applied: {owningTeamId.Value}");
                        }
                        else
                        {
                            _log.LogInformation("[BU] No owning team resolved. Default owner will apply.");
                        }
                    }

                    try
                    {
                        // Create asset
                        assetId = await DataverseApi.Instance.CreateAsync(newAsset);
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("AssetNameDuplicateCheckOnPropertyLogPlugin"))
                        {
                            _log.LogWarning($"Duplicate detected. Skipping creation for {assetName}");
                            continue;
                        }

                        throw;
                    }

                    // Important: Pass asset ID to summary for later BU updates
                    if (xOiJobInfo.WorkSummary != null)
                        xOiJobInfo.WorkSummary.CustomerAssetId = assetId;

                    // Step 4: Create property logs
                    var propertyTasks = new List<Task>
                    {
                        CustomerAssetOperation.SetAssetPropertyAsync(_log, AssetProperties.Make, asset.Make, assetId),
                        CustomerAssetOperation.SetAssetPropertyAsync(_log, AssetProperties.ModelNumber, asset.Model, assetId),
                        CustomerAssetOperation.SetAssetPropertyAsync(_log, AssetProperties.SerialNumber, asset.Serial, assetId)
                    };

                    if (!string.IsNullOrEmpty(asset.Transcript))
                    {
                        propertyTasks.Add(CustomerAssetOperation.SetAssetPropertyAsync(
                            _log,
                            AssetProperties.Transcript,
                            asset.Transcript,
                            assetId));
                    }

                    await Task.WhenAll(propertyTasks);
                }

                // Step 5: Associate to Work Order Incidents
                // Associate asset ONLY to workflow booking
    
                /*Guid workflowBookingId =
                    await BookableResourceBookingOperation
                        .GetBookingIdByWorkflowJobIdAsync(xOiJobInfo.WorkflowJobId);

                if (workflowBookingId != Guid.Empty)
                {
                    await CustomerAssetOperation.AssociateAssetToWorkOrderIncidentAsync(
                        _log,
                        assetId,
                        new List<Guid> { workflowBookingId }
                    );
                }
                else
                {
                    _log.LogWarning("No booking found for workflowJobId — asset not associated.");
                }*/


            }

            _log.LogInformation("Finished processing all customer assets for job.");
        }
    }
}

/*commented 16th NOV using Microsoft.Crm.Sdk.Messages;
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
using XOI_Integration.DataverseRepository.Operations;
using XOI_Integration.DataverseRepository.Provider;
using XOI_Integration.XOiRepository.XOiDataModels;

namespace XOI_Integration.DataverseRepository
{
    public class CustomerAssetDataHandler
    {
        private ILogger _log;

        public CustomerAssetDataHandler(ILogger log)
        {
            _log = log;
        }


        public async Task HandleCustomerAssetDataAsync(List<XOiToCustomerAssetData> customerAssetData, XOiJobInfo xOiJobInfo, string jobId)
        {
            if (customerAssetData == null || !customerAssetData.Any())
            {
                _log.LogInformation("No customer assets to process.");
                return;
            }

            _log.LogInformation($"Start processing {customerAssetData.Count} customer assets for job {jobId}");

            var customerId = await CustomerAssetOperation.GetCustomerGuidAsync(_log, xOiJobInfo.CustomerName);
            string assetCategory = Environment.GetEnvironmentVariable("DefaultAssetCategory", EnvironmentVariableTarget.Process);

            foreach (var asset in customerAssetData)
            {
                string assetName = $"{asset.Make} | {asset.Model} | {asset.Serial}";
                _log.LogInformation($"Processing asset: {assetName}");

                // 🔹 Step 1: Check for existing asset by name
                var query = new QueryExpression("msdyn_customerasset")
                {
                    ColumnSet = new ColumnSet("msdyn_customerassetid", "msdyn_name"),
                    Criteria = new FilterExpression(LogicalOperator.And)
                    {
                        Conditions =
                {
                    new ConditionExpression("msdyn_account", ConditionOperator.Equal, customerId),
                    new ConditionExpression("msdyn_name", ConditionOperator.Equal, assetName)
                }
                    }
                };

                var existingAssets = await DataverseApi.Instance.RetrieveMultipleAsync(query);
                Guid assetId;

                if (existingAssets.Entities.Any())
                {
                    // ✅ Update existing asset (transcript if changed)
                    var existingAsset = existingAssets.Entities.First();
                    assetId = existingAsset.Id;

                    _log.LogInformation($"Existing asset found: {assetName} ({assetId})");
                    if (xOiJobInfo?.WorkSummary != null)
                    {
                        xOiJobInfo.WorkSummary.CustomerAssetId = assetId;
                        _log.LogInformation($"[BU] Assigned existing AssetId {assetId} to WorkSummary.");
                    }

                    if (!string.IsNullOrEmpty(asset.Transcript))
                    {
                        _log.LogInformation($"Updating transcript for existing asset {assetId}");
                        await CustomerAssetOperation.UpdateCustomerAssetAsync(
                            _log,
                            new List<CustomerAssetToUpdate>
                            {
                        new CustomerAssetToUpdate
                        {
                            AssetId = assetId,
                            Transcript = asset.Transcript
                        }
                            },
                            xOiJobInfo.CustomerName,
                            jobId
                        );
                    }
                }
                else
                {
                    // ✅ Step 2: Create asset safely for plugin
                    _log.LogInformation($"🆕 Creating new asset: {assetName}");

                    Entity newAsset = new Entity("msdyn_customerasset")
                    {
                        ["msdyn_name"] = assetName,
                        ["msdyn_account"] = new EntityReference("account", customerId),
                        ["msdyn_customerassetcategory"] = new EntityReference("msdyn_customerassetcategory", Guid.Parse(assetCategory))
                    };

                    if (asset.ManufactureDate != null)
                        newAsset["msdyn_manufacturingdate"] = asset.ManufactureDate;

                    try
                    {
                        assetId = await DataverseApi.Instance.CreateAsync(newAsset);
                        _log.LogInformation($"✅ Asset created successfully: {assetName} ({assetId})");
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("AssetNameDuplicateCheckOnPropertyLogPlugin"))
                        {
                            _log.LogWarning($"Duplicate detected by plugin for {assetName}. Skipping creation.");
                            continue;
                        }
                        throw;
                    }

                    // Step 3: Apply BU before creation
                    _log.LogInformation("[BU] Attempting to resolve owning team for new asset");

                    var bookingIds = await BookableResourceBookingOperation.GetBookableResourceBookingIdsAsync(jobId);

                    if (bookingIds != null && bookingIds.Any())
                    {
                        var owningTeamId = await CustomerAssetOperation.GetOwningTeamFromBookingAsync(
                            _log,
                            bookingIds.First());

                        if (owningTeamId.HasValue)
                        {
                            newAsset["ownerid"] = new EntityReference("team", owningTeamId.Value);
                            _log.LogInformation($"[BU] Owning team applied: {owningTeamId.Value}");
                        }
                        else
                        {
                            _log.LogInformation("[BU] No owning team resolved. Default owner will apply.");
                        }
                    }

                    try
                    {
                        // Create asset
                        assetId = await DataverseApi.Instance.CreateAsync(newAsset);
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("AssetNameDuplicateCheckOnPropertyLogPlugin"))
                        {
                            _log.LogWarning($"Duplicate detected. Skipping creation for {assetName}");
                            continue;
                        }

                        throw;
                    }

                    // Important: Pass asset ID to summary for later BU updates
                    if (xOiJobInfo.WorkSummary != null)
                        xOiJobInfo.WorkSummary.CustomerAssetId = assetId;

                    // 🔹 Step 4: Create Property Logs (Make, Model, Serial, Transcript)
                    var propertyTasks = new List<Task>
            {
                CustomerAssetOperation.SetAssetPropertyAsync(_log, AssetProperties.Make, asset.Make, assetId),
                CustomerAssetOperation.SetAssetPropertyAsync(_log, AssetProperties.ModelNumber, asset.Model, assetId),
                CustomerAssetOperation.SetAssetPropertyAsync(_log, AssetProperties.SerialNumber, asset.Serial, assetId)
            };

                    if (!string.IsNullOrEmpty(asset.Transcript))
                        propertyTasks.Add(CustomerAssetOperation.SetAssetPropertyAsync(_log, AssetProperties.Transcript, asset.Transcript, assetId));

                    await Task.WhenAll(propertyTasks);
                }

                // 🔹 Step 5: Associate asset to Work Order Incidents (Bookings)
                var bookingIds = await BookableResourceBookingOperation.GetBookableResourceBookingIdsAsync(jobId);
                await CustomerAssetOperation.AssociateAssetToWorkOrderIncidentAsync(_log, assetId, bookingIds);
            }

            _log.LogInformation("✅ Finished processing all customer assets for job.");
        }*/


/* commented items
public async Task HandleCustomerAssetDataAsync(List<XOiToCustomerAssetData> customerAssetData, XOiJobInfo xOiJobInfo, string jobId)
{
    if (customerAssetData == null || !customerAssetData.Any())
    {
        _log.LogInformation("No customer assets to process.");
        return;
    }

    _log.LogInformation($"Start processing {customerAssetData.Count} customer assets for job {jobId}");

    // 1. Get all current customer asset IDs and properties
    var currentAssetIds = await CustomerAssetOperation.GetCustomerAssetIdsAsync(_log, xOiJobInfo.CustomerName);
    var relatedAssetProperties = await CustomerAssetOperation.GetCustomerAssetPropertiesAsync(_log, currentAssetIds);

    foreach (var customerAsset in customerAssetData)
    {
        _log.LogInformation($"Processing asset: Make={customerAsset.Make}, Model={customerAsset.Model}, Serial={customerAsset.Serial}");

        // Match by Make + Model + Serial
        var matchingAsset = relatedAssetProperties.FirstOrDefault(o =>
            string.Equals((o.Make ?? "").Trim(), (customerAsset.Make ?? "").Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals((o.Model ?? "").Trim(), (customerAsset.Model ?? "").Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals((o.Serial ?? "").Trim(), (customerAsset.Serial ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
        );

        Guid assetId;

        if (matchingAsset != null)
        {
            _log.LogInformation($"Existing asset found with ID {matchingAsset.AssetId}");

            // Update transcript if it changed
            if (!string.Equals(matchingAsset.Transcript ?? "", customerAsset.Transcript ?? "", StringComparison.Ordinal))
            {
                _log.LogInformation($"Updating transcript for asset {matchingAsset.AssetId}");
                await CustomerAssetOperation.UpdateCustomerAssetAsync(_log, new List<CustomerAssetToUpdate>
        {
            new CustomerAssetToUpdate { AssetId = matchingAsset.AssetId, Transcript = customerAsset.Transcript }
        }, xOiJobInfo.CustomerName, jobId);
            }

            assetId = matchingAsset.AssetId;
        }
        else
        {
            _log.LogInformation($"Creating new asset {customerAsset.Make} | {customerAsset.Model} | {customerAsset.Serial}");

            var newAssets = new List<CustomerAssetToCreate>
    {
        new CustomerAssetToCreate
        {
            Make = customerAsset.Make,
            Model = customerAsset.Model,
            Serial = customerAsset.Serial,
            Transcript = customerAsset.Transcript,
            ManufactureDate = customerAsset.ManufactureDate
        }
    };

            await CustomerAssetOperation.CreateCustomerAssetAsync(_log, newAssets, xOiJobInfo.CustomerName, jobId);

            // Refresh properties to get the newly created asset ID
            var updatedIds = await CustomerAssetOperation.GetCustomerAssetIdsAsync(_log, xOiJobInfo.CustomerName);
            relatedAssetProperties = await CustomerAssetOperation.GetCustomerAssetPropertiesAsync(_log, updatedIds);

            var createdAsset = relatedAssetProperties.FirstOrDefault(o =>
                string.Equals((o.Make ?? "").Trim(), (customerAsset.Make ?? "").Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals((o.Model ?? "").Trim(), (customerAsset.Model ?? "").Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals((o.Serial ?? "").Trim(), (customerAsset.Serial ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
            );

            assetId = createdAsset.AssetId;
            _log.LogInformation($"New asset created with ID {assetId}");
        }

        /* //gg commented 4 AM
         * Always associate the asset to Work Order Incident
         _log.LogInformation($"Associating asset {assetId} to Work Order Incident for job {xOiJobInfo.OrderNumber}");
         await CustomerAssetOperation.AssociateAssetToWorkOrderIncidentAsync(_log, assetId, xOiJobInfo);*/
/* var bookingIds = await BookableResourceBookingOperation.GetBookableResourceBookingIdsAsync(jobId);
 await CustomerAssetOperation.AssociateAssetToWorkOrderIncidentAsync(_log, assetId, bookingIds);

}

_log.LogInformation("Finished processing all customer assets for job");
}

*/
//gg 2 41 AM
/*public async Task HandleCustomerAssetDataAsync(List<XOiToCustomerAssetData> customerAssetData, XOiJobInfo xOiJobInfo, string jobId)
{
    _log.LogInformation("Start processing customer assets for job");

    // 1. Get all customer asset IDs for the customer
    var customerAssetIds = await CustomerAssetOperation.GetCustomerAssetIdsAsync(_log, xOiJobInfo.CustomerName);

    // 2. Get properties for all assets
    var relatedAssetProperties = await CustomerAssetOperation.GetCustomerAssetPropertiesAsync(_log, customerAssetIds);

    foreach (var customerAsset in customerAssetData)
    {
        // Match existing asset by Make + Model + Serial
        var matchingAsset = relatedAssetProperties.FirstOrDefault(a =>
            string.Equals((a.Make ?? "").Trim(), (customerAsset.Make ?? "").Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals((a.Model ?? "").Trim(), (customerAsset.Model ?? "").Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals((a.Serial ?? "").Trim(), (customerAsset.Serial ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
        );

        Guid assetId;

        if (matchingAsset != null)
        {
            // Update transcript if it differs
            if (!string.Equals(matchingAsset.Transcript ?? "", customerAsset.Transcript ?? "", StringComparison.Ordinal))
            {
                _log.LogInformation($"Updating transcript for existing asset {matchingAsset.AssetId}");
                await CustomerAssetOperation.UpdateCustomerAssetAsync(_log, new List<CustomerAssetToUpdate>
        {
            new CustomerAssetToUpdate
            {
                AssetId = matchingAsset.AssetId,
                Transcript = customerAsset.Transcript
            }
        }, xOiJobInfo.CustomerName, jobId);
            }

            assetId = matchingAsset.AssetId;
        }
        else
        {
            // Create new asset
            _log.LogInformation($"Creating new asset {customerAsset.Make} | {customerAsset.Model}");

            // Create the Customer Asset record
            var customerId = await CustomerAssetOperation.GetCustomerGuidAsync(_log, xOiJobInfo.CustomerName);
            string assetCategory = Environment.GetEnvironmentVariable("DefaultAssetCategory", EnvironmentVariableTarget.Process);

            Entity newAsset = new Entity("msdyn_customerasset")
            {
                ["msdyn_name"] = $"{customerAsset.Make} | {customerAsset.Model}",
                ["msdyn_account"] = new EntityReference("account", customerId),
                ["msdyn_customerassetcategory"] = new EntityReference("msdyn_customerassetcategory", Guid.Parse(assetCategory)),
                ["msdyn_manufacturingdate"] = customerAsset.ManufactureDate
            };

            assetId = await DataverseApi.Instance.CreateAsync(newAsset);

            // Set all properties via msdyn_propertylog
            var propertyTasks = new List<Task>
    {
        CustomerAssetOperation.SetAssetPropertyAsync(_log, AssetProperties.Make, customerAsset.Make, assetId),
        CustomerAssetOperation.SetAssetPropertyAsync(_log, AssetProperties.ModelNumber, customerAsset.Model, assetId),
        CustomerAssetOperation.SetAssetPropertyAsync(_log, AssetProperties.SerialNumber, customerAsset.Serial, assetId),
        CustomerAssetOperation.SetAssetPropertyAsync(_log, AssetProperties.Transcript, customerAsset.Transcript, assetId)
    };

            await Task.WhenAll(propertyTasks);

            _log.LogInformation($"New asset created with ID {assetId}");
        }

        // Associate asset to Work Order Incident(s)
        await CustomerAssetOperation.AssociateAssetToWorkOrderIncidentAsync(_log, assetId, xOiJobInfo);

        // Update in-memory list to avoid duplicates in same batch
        relatedAssetProperties.Add(new RelatedAssetProperty
        {
            AssetId = assetId,
            Make = customerAsset.Make,
            Model = customerAsset.Model,
            Serial = customerAsset.Serial,
            Transcript = customerAsset.Transcript
        });
    }

    _log.LogInformation("Finished processing customer assets for job");
}*/

/* gg commented 16th 2 am
 * public async Task HandleCustomerAssetDataAsync(List<XOiToCustomerAssetData> customerAssetData, XOi       JobInfo xOiJobInfo, string jobId)
 {
     var customerAssetIds = await CustomerAssetOperation.GetCustomerAssetIdsAsync(_log, xOiJobInfo.CustomerName);

     var relatedAssetProperties = await CustomerAssetOperation.GetCustomerAssetPropertiesAsync(_log, customerAssetIds);
     _log.LogInformation("Start Preparing Asset Data for Create or Update");

     foreach (var customerAsset in customerAssetData)
     {
         // Match by Make + Model + Serial
         var matchingAsset = relatedAssetProperties.FirstOrDefault(o =>
             string.Equals((o.Make ?? "").Trim(), (customerAsset.Make ?? "").Trim(), StringComparison.OrdinalIgnoreCase) &&
             string.Equals((o.Model ?? "").Trim(), (customerAsset.Model ?? "").Trim(), StringComparison.OrdinalIgnoreCase) &&
             string.Equals((o.Serial ?? "").Trim(), (customerAsset.Serial ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
         );

         Guid assetId;

         if (matchingAsset != null)
         {
             // Update transcript if needed
             if (!string.Equals(matchingAsset.Transcript ?? "", customerAsset.Transcript??"",StringComparison.Ordinal))
             {   
                 _log.LogInformation($"Updating transcript for asset {matchingAsset.AssetId}");
                 await CustomerAssetOperation.UpdateCustomerAssetAsync(_log, new List<CustomerAssetToUpdate>
         {
             new CustomerAssetToUpdate { AssetId = matchingAsset.AssetId, Transcript = customerAsset.Transcript }
         }, xOiJobInfo.CustomerName, jobId);
             }

             assetId = matchingAsset.AssetId;
         }
         else
         {
             // Create new asset
             _log.LogInformation($"Creating new asset {customerAsset.Make} | {customerAsset.Model}");
             var newAssets = new List<CustomerAssetToCreate>
     {
         new CustomerAssetToCreate
         {
             Make = customerAsset.Make,
             Model = customerAsset.Model,
             Serial = customerAsset.Serial,
             Transcript = customerAsset.Transcript,
             ManufactureDate = customerAsset.ManufactureDate
         }
     };

             await CustomerAssetOperation.CreateCustomerAssetAsync(_log, newAssets, xOiJobInfo.CustomerName, jobId);

             *//*  gg// Refresh to get the newly created asset ID
               var updatedAssets = await CustomerAssetOperation.GetCustomerAssetPropertiesAsync(_log, await CustomerAssetOperation.GetCustomerAssetIdsAsync(_log, xOiJobInfo.CustomerName));
               var createdAsset = updatedAssets.FirstOrDefault(o =>
                   string.Equals((o.Make ?? "").Trim(), (customerAsset.Make ?? "").Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals((o.Model ?? "").Trim(), (customerAsset.Model ?? "").Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals((o.Serial ?? "").Trim(), (customerAsset.Serial ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
               );*//*

             //Refresh to include the newly created asset in our in-memory list
                 var updatedIds = await CustomerAssetOperation.GetCustomerAssetIdsAsync(_log, xOiJobInfo.CustomerName);
                 relatedAssetProperties = await CustomerAssetOperation.GetCustomerAssetPropertiesAsync(_log, updatedIds);

                 var createdAsset = relatedAssetProperties.FirstOrDefault(o =>
                     string.Equals((o.Make ?? "").Trim(), (customerAsset.Make ?? "").Trim(), StringComparison.OrdinalIgnoreCase) &&
                     string.Equals((o.Model ?? "").Trim(), (customerAsset.Model ?? "").Trim(), StringComparison.OrdinalIgnoreCase) &&
                     string.Equals((o.Serial ?? "").Trim(), (customerAsset.Serial ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
                 );


                 assetId = createdAsset.AssetId;
             }

         // Associate asset to Work Order Incident
         await CustomerAssetOperation.AssociateAssetToWorkOrderIncidentAsync(_log, assetId, xOiJobInfo);
     }

     _log.LogInformation("Finished processing assets for job");
     //gg
     *//*var recordsToUpdate = new List<CustomerAssetToUpdate>();
     var recordsToCreate = new List<CustomerAssetToCreate>();

     _log.LogInformation("Start preparing assets data for Create or Update");

     foreach (var customerAsset in customerAssetData)
     {
         _log.LogInformation($"Processing asset with Make='{customerAsset.Make ?? "NULL"}', Model='{customerAsset.Model ?? "NULL"}', Serial='{customerAsset.Serial ?? "NULL"}'");

         foreach (var o in relatedAssetProperties)
         {
             _log.LogInformation($"Comparing against existing asset with Make='{o.Make}', Model='{o.Model}', Serial='{o.Serial}'");
         }
         //var matchingObject = relatedAssetProperties.FirstOrDefault(o =>
         //    (o.Make.Trim() ?? "") == (customerAsset.Make.Trim() ?? "") &&
         //    (o.Serial.Trim() ?? "") == (customerAsset.Serial.Trim() ?? "") &&
         //    (o.Model.Trim() ?? "") == (customerAsset.Model.Trim() ?? "")
         //);

         var matchingObject = relatedAssetProperties.FirstOrDefault(o =>
string.Equals((o.Make ?? "").Trim(), (customerAsset.Make ?? "").Trim(), StringComparison.OrdinalIgnoreCase) &&
string.Equals((o.Serial ?? "").Trim(), (customerAsset.Serial ?? "").Trim(), StringComparison.OrdinalIgnoreCase) &&
string.Equals((o.Model ?? "").Trim(), (customerAsset.Model ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
);

         if (matchingObject != null)
         {
             if ((matchingObject.Transcript.Trim() ?? "") != (customerAsset.Transcript.Trim() ?? ""))
             {
                 recordsToUpdate.Add(new CustomerAssetToUpdate
                 {
                     AssetId = matchingObject.AssetId,
                     Transcript = customerAsset.Transcript
                 });
             }
         }
         else
         {
             recordsToCreate.Add(new CustomerAssetToCreate
             {
                 Make = customerAsset.Make,
                 Serial = customerAsset.Serial,
                 Model = customerAsset.Model,
                 Transcript = customerAsset.Transcript,
                 ManufactureDate = customerAsset.ManufactureDate,
             });
         }
     }

     _log.LogInformation("Finish preparing assets data for Create or Update");

     if (recordsToCreate.Any())
     {
         await CustomerAssetOperation.CreateCustomerAssetAsync(_log, recordsToCreate, xOiJobInfo.CustomerName, jobId);
         _log.LogInformation("Customer Assets created successfully");
     }

     if (recordsToUpdate.Any())
     {
         await CustomerAssetOperation.UpdateCustomerAssetAsync(_log, recordsToUpdate, xOiJobInfo.CustomerName, jobId);
         _log.LogInformation("Customer Assets updated successfully");
     }

     if (!recordsToCreate.Any() && !recordsToUpdate.Any())
     {
         _log.LogInformation("Nothing to create or update");
     }*//*
 }

*/
