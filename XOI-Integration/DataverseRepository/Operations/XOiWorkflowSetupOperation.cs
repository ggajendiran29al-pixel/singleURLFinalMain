using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using XOI_Integration.DataverseRepository.Provider;

namespace XOI_Integration.DataverseRepository.Operations
{
    /// <summary>
    /// Reads the XOi Workflow Setup table, which replaced the ExcludedWorkflowNames app
    /// setting. Exclusions are now per legal entity: a row excludes one workflow name for
    /// one company, so the same workflow can sync for one company and be skipped for
    /// another. The app setting could only express a single global list.
    /// </summary>
    public static class XOiWorkflowSetupOperation
    {
        private const string SETUP_ENTITY = "acl_xoiworkflowsetup";
        private const string SETUP_NAME = "acl_name";
        private const string SETUP_COMPANY = "acl_company";

        private const string BOOKING_ENTITY = "bookableresourcebooking";
        private const string BOOKING_JOB_ID = "sisps_xoi_vision_jobid";
        private const string BOOKING_WORK_ORDER = "msdyn_workorder";

        private const string WORK_ORDER_ENTITY = "msdyn_workorder";
        private const string WORK_ORDER_ID = "msdyn_workorderid";
        private const string WORK_ORDER_COMPANY = "cdm_company";

        /// <summary>
        /// Workflow names excluded for the company that owns this XOi job, matched by the
        /// job id already carried on the summary response. Resolved once per sync and
        /// matched in memory afterwards — asset sync checks every document, so querying
        /// per document would multiply round trips for no benefit.
        ///
        /// Returns an empty set when anything in the chain is missing: no booking carries
        /// the job id, the booking has no work order, the work order has no company, or
        /// the company has no setup rows. An empty set means nothing is excluded and the
        /// workflow syncs, which keeps a transient lookup failure from silently
        /// discarding technician data. Failures are logged as warnings.
        /// </summary>
        public static async Task<HashSet<string>> GetExcludedWorkflowNamesAsync(ILogger log, string xOiJobId)
        {
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(xOiJobId))
            {
                log.LogWarning("XOi Workflow Setup — no job id supplied, nothing excluded.");
                return excluded;
            }

            try
            {
                var companyRef = await GetCompanyForXOiJobAsync(log, xOiJobId);
                if (companyRef == null)
                    return excluded;

                var query = new QueryExpression(SETUP_ENTITY)
                {
                    ColumnSet = new ColumnSet(SETUP_NAME),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression(SETUP_COMPANY, ConditionOperator.Equal, companyRef.Id),
                            // Deactivating a setup row should stop it excluding anything.
                            new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                        }
                    }
                };

                var result = await DataverseApi.Instance.RetrieveMultipleAsync(query);

                foreach (var row in result.Entities)
                {
                    // Rows are hand-entered, so trim before comparing — a trailing space
                    // would otherwise make a row that looks correct never match.
                    var name = row.GetAttributeValue<string>(SETUP_NAME)?.Trim();
                    if (!string.IsNullOrEmpty(name))
                        excluded.Add(name);
                }

                log.LogInformation(
                    $"XOi Workflow Setup — company {companyRef.Name ?? companyRef.Id.ToString()} excludes {excluded.Count} workflow(s): [{string.Join(", ", excluded)}]");

                return excluded;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex,
                    $"XOi Workflow Setup lookup failed for job {xOiJobId} — nothing excluded, workflows will sync.");
                return excluded;
            }
        }

        /// <summary>
        /// Walks XOi job id to legal entity in one round trip:
        /// booking (by sisps_xoi_vision_jobid) to msdyn_workorder to cdm_company.
        /// Oldest booking wins, so the answer does not change between runs when several
        /// bookings share the job.
        /// </summary>
        private static async Task<EntityReference> GetCompanyForXOiJobAsync(ILogger log, string xOiJobId)
        {
            var query = new QueryExpression(BOOKING_ENTITY)
            {
                ColumnSet = new ColumnSet("bookableresourcebookingid"),
                TopCount = 1,
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(BOOKING_JOB_ID, ConditionOperator.Equal, xOiJobId),
                        new ConditionExpression(BOOKING_WORK_ORDER, ConditionOperator.NotNull)
                    }
                },
                Orders =
                {
                    new OrderExpression("createdon", OrderType.Ascending)
                }
            };

            var workOrderLink = query.AddLink(
                WORK_ORDER_ENTITY,
                BOOKING_WORK_ORDER,
                WORK_ORDER_ID,
                JoinOperator.Inner);

            workOrderLink.EntityAlias = "wo";
            workOrderLink.Columns = new ColumnSet(WORK_ORDER_COMPANY);

            var result = await DataverseApi.Instance.RetrieveMultipleAsync(query);
            var booking = result.Entities.FirstOrDefault();

            if (booking == null)
            {
                log.LogWarning(
                    $"XOi Workflow Setup — no work order booking carries job {xOiJobId}, nothing excluded.");
                return null;
            }

            if (!booking.Attributes.TryGetValue($"wo.{WORK_ORDER_COMPANY}", out var raw) ||
                !(raw is AliasedValue aliased) ||
                !(aliased.Value is EntityReference companyRef))
            {
                log.LogWarning(
                    $"XOi Workflow Setup — work order for job {xOiJobId} has no {WORK_ORDER_COMPANY}, nothing excluded.");
                return null;
            }

            return companyRef;
        }
    }
}
