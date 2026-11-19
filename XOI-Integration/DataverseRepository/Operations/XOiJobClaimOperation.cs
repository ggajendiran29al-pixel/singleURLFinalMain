using System;
using System.ServiceModel;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using XOI_Integration.DataverseRepository.Provider;

namespace XOI_Integration.DataverseRepository.Operations
{
    // Stops two Service Bus messages for the same work order (or project) from each
    // creating an XOi job. Booking Create is followed within milliseconds by Booking
    // Update, and several bookings can be created on one work order at once; every one
    // of those messages used to read an empty acl_xoi_vision_jobid, call createJob, and
    // leave XOi with one job per message — only the last ID written stayed linked.
    //
    // Before creating, a message now writes a short-lived PENDING marker into the parent's
    // acl_xoi_vision_jobid, conditioned on the row version it read. Dataverse lets exactly
    // one conditional write win; every other message re-reads, sees the marker, and waits
    // for the real job ID to replace it, then reuses that job.
    public static class XOiJobClaimOperation
    {
        private const string JobIdField = "acl_xoi_vision_jobid";

        // Kept short so it fits any column that can hold an XOi job ID (a 36-char UUID):
        // "PENDING|<unix seconds>|<8 hex>" is 27 characters.
        private const string PendingPrefix = "PENDING|";

        // A claim older than this is treated as abandoned (the holder crashed or timed out
        // between claiming and writing the job ID) and may be taken over. createJob plus
        // the Dataverse writes normally take a few seconds.
        private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

        // How long a message waits for another message's claim to resolve before giving
        // up and throwing, which hands the message back to Service Bus for redelivery.
        private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

        // Dataverse ConcurrencyVersionMismatch (0x80060882)
        private const int ConcurrencyVersionMismatch = unchecked((int)0x80060882);

        public sealed class Claim
        {
            public string EntityName { get; init; }
            public Guid RecordId { get; init; }

            // Set when the parent already holds a real job ID — reuse it, create nothing.
            public string ExistingJobId { get; init; }

            // Set when this message won the claim and must create the job.
            public string Marker { get; init; }

            public bool Acquired => Marker != null;
        }

        public static bool IsPendingMarker(string value) =>
            value != null && value.StartsWith(PendingPrefix, StringComparison.Ordinal);

        // Readers of acl_xoi_vision_jobid go through this so an in-flight claim is never
        // mistaken for a job ID.
        public static string RealJobIdOrNull(string value) =>
            string.IsNullOrEmpty(value) || IsPendingMarker(value) ? null : value;

        // Returns either the job ID another message created (reuse it) or an acquired claim
        // (create the job, then write its ID over the marker). Throws if another message's
        // claim does not resolve within WaitTimeout, so Service Bus redelivers this one.
        public static async Task<Claim> AcquireAsync(
            ILogger log, string entityName, Guid recordId)
        {
            DateTime deadline = DateTime.UtcNow + WaitTimeout;

            while (true)
            {
                Entity record = await Task.Run(() =>
                    DataverseApi.Instance.Retrieve(entityName, recordId, new ColumnSet(JobIdField)));

                string value = record.GetAttributeValue<string>(JobIdField);

                if (RealJobIdOrNull(value) != null)
                {
                    log.LogInformation($"XOi job {value} already exists on {entityName} {recordId} — reusing it.");
                    return new Claim { EntityName = entityName, RecordId = recordId, ExistingJobId = value };
                }

                bool heldByAnother = IsPendingMarker(value) && !IsStale(value);

                if (!heldByAnother)
                {
                    if (IsPendingMarker(value))
                        log.LogWarning($"Taking over stale XOi job claim '{value}' on {entityName} {recordId}.");

                    string marker = NewMarker();

                    if (await TryWriteAsync(log, entityName, recordId, record.RowVersion, marker))
                    {
                        log.LogInformation($"Claimed XOi job creation for {entityName} {recordId} ({marker}).");
                        return new Claim { EntityName = entityName, RecordId = recordId, Marker = marker };
                    }

                    log.LogInformation($"Another message claimed {entityName} {recordId} first — re-reading.");
                }
                else
                {
                    log.LogInformation($"XOi job for {entityName} {recordId} is being created by another message ({value}) — waiting.");
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new InvalidOperationException(
                        $"XOi job creation for {entityName} {recordId} is still claimed by another message after " +
                        $"{WaitTimeout.TotalSeconds:0}s ({value}). Failing so Service Bus redelivers this message.");
                }

                await Task.Delay(heldByAnother ? PollInterval : TimeSpan.FromMilliseconds(250));
            }
        }

        // Clears this message's marker when no job ID was produced, so the next message can
        // try again straight away instead of waiting for the claim to go stale. Only clears
        // the marker if it is still ours; never throws.
        public static async Task ReleaseAsync(ILogger log, Claim claim)
        {
            if (claim?.Acquired != true)
                return;

            try
            {
                Entity record = await Task.Run(() =>
                    DataverseApi.Instance.Retrieve(claim.EntityName, claim.RecordId, new ColumnSet(JobIdField)));

                if (record.GetAttributeValue<string>(JobIdField) != claim.Marker)
                    return;

                if (await TryWriteAsync(log, claim.EntityName, claim.RecordId, record.RowVersion, null))
                    log.LogInformation($"Released XOi job claim on {claim.EntityName} {claim.RecordId}.");
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, $"Could not release XOi job claim on {claim.EntityName} {claim.RecordId} — it expires after {StaleAfter.TotalMinutes:0} minutes.");
            }
        }

        private static string NewMarker() =>
            $"{PendingPrefix}{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}|{Guid.NewGuid().ToString("N").Substring(0, 8)}";

        private static bool IsStale(string marker)
        {
            string[] parts = marker.Split('|');

            if (parts.Length < 2 || !long.TryParse(parts[1], out long seconds))
                return true; // unreadable marker — never let it block job creation for good

            return DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(seconds) > StaleAfter;
        }

        // Writes the field only if the row is unchanged since rowVersion was read. Returns
        // false when another write got there first.
        private static async Task<bool> TryWriteAsync(
            ILogger log, string entityName, Guid recordId, string rowVersion, string value)
        {
            var target = new Entity(entityName, recordId) { [JobIdField] = value };

            if (string.IsNullOrEmpty(rowVersion))
            {
                // Only happens if optimistic concurrency is disabled on the table. Fall back
                // to write-then-verify: narrows the race to the read-back window rather than
                // the whole createJob call.
                log.LogWarning($"{entityName} returned no row version — claiming without a concurrency check.");

                await DataverseApi.Instance.UpdateAsync(target);

                Entity check = await Task.Run(() =>
                    DataverseApi.Instance.Retrieve(entityName, recordId, new ColumnSet(JobIdField)));

                return check.GetAttributeValue<string>(JobIdField) == value;
            }

            target.RowVersion = rowVersion;

            try
            {
                await DataverseApi.Instance.ExecuteAsync(new UpdateRequest
                {
                    Target = target,
                    ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
                });
                return true;
            }
            catch (Exception ex) when (IsVersionMismatch(ex))
            {
                return false;
            }
        }

        private static bool IsVersionMismatch(Exception ex)
        {
            // ServiceClient may surface the fault directly or wrapped (DataverseOperationException
            // carries the Dataverse error code as its HResult), so check the whole chain.
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                if (e is FaultException<OrganizationServiceFault> fault &&
                    fault.Detail?.ErrorCode == ConcurrencyVersionMismatch)
                    return true;

                if (e.HResult == ConcurrencyVersionMismatch)
                    return true;
            }
            return false;
        }
    }
}
