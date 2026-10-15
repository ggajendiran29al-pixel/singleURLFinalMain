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

        // The field's schema name, as created in Dataverse.
        private const string SETUP_NAME = "acl_Name";

        // Its logical name — the lower-cased form the platform stores and the only one a
        // query is guaranteed to resolve. Reads go through ReadWorkflowName, which accepts
        // either casing, so the two constants cannot drift into a silent empty result.
        private const string SETUP_NAME_LOGICAL = "acl_name";

        private const string SETUP_COMPANY = "acl_company";

        private const string BOOKING_ENTITY = "bookableresourcebooking";
        private const string BOOKING_JOB_ID = "sisps_xoi_vision_jobid";
        private const string BOOKING_WORK_ORDER = "msdyn_workorder";

        private const string WORK_ORDER_ENTITY = "msdyn_workorder";
        private const string WORK_ORDER_ID = "msdyn_workorderid";

        // The work order's legal entity lookup is sis_company — the field name, not the
        // table it points at. It targets cdm_company, which is also what the setup table's
        // acl_company targets, so the two EntityReference ids compare directly.
        private const string WORK_ORDER_COMPANY = "sis_company";

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

            // Named so a failure says which query threw. The previous version logged the
            // exception only through LogWarning's structured parameter, which this log view
            // does not render — so a failure said nothing about its own cause.
            string step = "resolving company from job id";

            try
            {
                var companyRef = await GetCompanyForXOiJobAsync(log, xOiJobId);
                if (companyRef == null)
                    return excluded;

                step = $"reading {SETUP_ENTITY} rows for company {companyRef.Id}";

                var query = new QueryExpression(SETUP_ENTITY)
                {
                    // Logical names, always lower case, are what the platform resolves in a
                    // query regardless of how the field's schema name was typed. The read
                    // below stays casing-tolerant, so the value is picked up either way.
                    ColumnSet = new ColumnSet(SETUP_NAME_LOGICAL),
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
                    var name = ReadWorkflowName(row);
                    if (!string.IsNullOrEmpty(name))
                        excluded.Add(name);
                }

                log.LogInformation(
                    $"XOi Workflow Setup — company {companyRef.Name ?? companyRef.Id.ToString()} excludes {excluded.Count} workflow(s): [{string.Join(", ", excluded)}]");

                return excluded;
            }
            catch (Exception ex)
            {
                log.LogWarning(
                    $"XOi Workflow Setup lookup failed for job {xOiJobId} while {step} — " +
                    $"{ex.GetType().Name}: {ex.Message} — nothing excluded, workflows will sync.");
                return excluded;
            }
        }

        /// <summary>
        /// Reads the workflow name from a setup row without depending on the casing of
        /// SETUP_NAME. ColumnSet and ConditionExpression resolve attribute names server
        /// side and accept either casing, but the Attributes collection that comes back is
        /// keyed by the lower case logical name and looked up case sensitively — so asking
        /// for a schema name like "acl_Name" directly would return null, and every row
        /// would be skipped silently with nothing excluded.
        ///
        /// Rows are hand-entered, so the value is trimmed: a trailing space would
        /// otherwise stop a row that looks correct from ever matching.
        /// </summary>
        private static string ReadWorkflowName(Entity row)
        {
            if (row.Attributes.TryGetValue(SETUP_NAME, out var exact))
                return (exact as string)?.Trim();

            var match = row.Attributes
                .FirstOrDefault(a => string.Equals(a.Key, SETUP_NAME, StringComparison.OrdinalIgnoreCase));

            return (match.Value as string)?.Trim();
        }

        /// <summary>
        /// Walks XOi job id to legal entity in one round trip:
        /// booking (by sisps_xoi_vision_jobid) to msdyn_workorder to sis_company.
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
