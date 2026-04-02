using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XOI_Integration.DataverseRepository.Operations;
using XOI_Integration.DataverseRepository.Provider;
using XOI_Integration.XOiRepository.XOiDataModels;

namespace XOI_Integration.DataverseRepository
{
    public class BookableResourceWorkSummaryDataHandler
    {
        ILogger _log;

        public BookableResourceWorkSummaryDataHandler(ILogger log)
        {
            _log = log;
        }
        private static string ComputeHash(string input)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(input ?? "");
            return Convert.ToHexString(sha.ComputeHash(bytes));
        }

        // ---------------------------------------------------------------------
        // Helper - Check if a note already exists on this booking with this text
        // ---------------------------------------------------------------------
        public static async Task<bool> NoteAlreadyExistsAsync(Guid bookingId, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            QueryExpression query = new QueryExpression("annotation")
            {
                ColumnSet = new ColumnSet("annotationid", "notetext"),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression("objectid", ConditionOperator.Equal, bookingId),
                        new ConditionExpression("notetext", ConditionOperator.Equal, text)
                    }
                }
            };

            var result = await DataverseApi.Instance.RetrieveMultipleAsync(query);
            return result.Entities.Any();
        }

        // ---------------------------------------------------------------------
        // Main note creator
        // ---------------------------------------------------------------------
        public static async Task CreateBookableResourceBookingNoteAsync(
            ILogger _log,
            XOiWorkSummaryToBookableResourceData xOiSummary,
            string jobId)
        {
            _log.LogInformation("Start creating Bookable Resource Booking Timeline");

            var bookingIds = await BookableResourceBookingOperation.GetBookableResourceBookingIdsAsync(jobId);

            if (bookingIds == null || !bookingIds.Any())
            {
                _log.LogWarning("No bookings found for job - cannot create notes.");
                return;
            }


            // ---------------------------------------------------
            // 1. Resolve BU for each booking + update asset owner
            // ---------------------------------------------------
            foreach (var bookingId in bookingIds)
            {
                _log.LogInformation($"[BU] Resolving owning team for booking {bookingId}");

                var owningTeamId = await CustomerAssetOperation.GetOwningTeamFromBookingAsync(_log, bookingId);

                if (owningTeamId.HasValue && xOiSummary.CustomerAssetId != Guid.Empty)
                {
                    Entity updateOwner = new Entity("msdyn_customerasset", xOiSummary.CustomerAssetId)
                    {
                        ["ownerid"] = new EntityReference("team", owningTeamId.Value)
                    };

                    await DataverseApi.Instance.UpdateAsync(updateOwner);

                    _log.LogInformation($"[BU] Asset {xOiSummary.CustomerAssetId} owner set to team {owningTeamId.Value}");
                }
            }

            // ---------------------------------------------------
            // 2. Create Booking Notes (corrected logic)
            // ---------------------------------------------------
            foreach (var bookingId in bookingIds)
            {
                _log.LogInformation($"[NOTE] Processing booking {bookingId}");

                string jobShareLink =
                    await BookableResourceBookingOperation.GetBookableResourceBookingCustomerJobShareLinkAsync(bookingId);

                string newNote =
                    $"[{xOiSummary.WorkflowName}] Summary from ({xOiSummary.UserInitial}): {xOiSummary.WorkSummary}"
                    + Environment.NewLine +
                    jobShareLink;

                // Dedup check
                // ✅ Compute hash of summary only (without job link)
                string summaryText = $"[{xOiSummary.WorkflowName}] Summary from ({xOiSummary.UserInitial}): {xOiSummary.WorkSummary}";
                string hash = ComputeHash(summaryText);

                // Fetch all existing notes for this booking (with hash)
                var existingNotes = await BookableResourceBookingOperation.GetBookableResourceBookingNotesForSingleBooking(bookingId);

                // Skip if hash already exists
                bool alreadyExists = existingNotes.Any(n =>
                    !string.IsNullOrEmpty(n.Hash) &&
                    string.Equals(n.Hash, hash, StringComparison.OrdinalIgnoreCase));

                // Optional fallback for old notes created without hash
                if (!alreadyExists)
                {
                    alreadyExists = existingNotes.Any(n =>
                        !string.IsNullOrEmpty(n.Note) &&
                        n.Note.Contains(summaryText, StringComparison.OrdinalIgnoreCase));
                }

                if (alreadyExists)
                {
                    _log.LogInformation($"Skipping note — same summary already exists for booking {bookingId}");
                    continue;
                }

                Entity note = new Entity("msdyn_bookableresourcebookingquicknote")
                {
                    ["msdyn_quicknote_lookup_entity"] = new EntityReference("bookableresourcebooking", bookingId),
                    ["msdyn_text"] = newNote,
                    ["acl_xoisummaryhash"] = hash
                };

                await DataverseApi.Instance.CreateAsync(note);
                _log.LogInformation($"Created new note (summary hash: {hash}) for booking {bookingId}");
            }

                _log.LogInformation("Finish creating Bookable Resource Booking Notes");
        }
    }
}


/*COMMENTED ON 16TH JUNE
 * using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XOI_Integration.DataverseRepository.Operations;
using XOI_Integration.DataverseRepository.Provider;
using XOI_Integration.XOiRepository.XOiDataModels;

namespace XOI_Integration.DataverseRepository
{
    public class BookableResourceWorkSummaryDataHandler
    {
        ILogger _log;

        public BookableResourceWorkSummaryDataHandler(ILogger log)
        {
            _log = log;
        }

        public static async Task CreateBookableResourceBookingNoteAsync(
       ILogger _log,
       XOiWorkSummaryToBookableResourceData xOiSummary,
       string jobId)
        {
            _log.LogInformation("Start creating Bookable Resource Booking Timeline");

            var bookingIds = await BookableResourceBookingOperation.GetBookableResourceBookingIdsAsync(jobId);

            // -------------------------
            // 1. Resolve BU + update owner
            // -------------------------
            foreach (var bookingId in bookingIds)
            {
                _log.LogInformation($"[BU] Resolving owning team for booking {bookingId}");

                var owningTeamId = await CustomerAssetOperation.GetOwningTeamFromBookingAsync(_log, bookingId);

                if (owningTeamId.HasValue)
                {
                    _log.LogInformation($"[BU] Team resolved: {owningTeamId.Value}");

                    if (xOiSummary.CustomerAssetId != Guid.Empty)
                    {
                        Entity updateOwner = new Entity("msdyn_customerasset", xOiSummary.CustomerAssetId)
                        {
                            ["ownerid"] = new EntityReference("team", owningTeamId.Value)
                        };

                        await DataverseApi.Instance.UpdateAsync(updateOwner);
                        _log.LogInformation($"[BU] Asset {xOiSummary.CustomerAssetId} owner set to team {owningTeamId.Value}");
                    }
                }
                else
                {
                    _log.LogWarning($"[BU] Owning team not found for booking {bookingId}");
                }
            }

            // -------------------------
            // 2. Create Booking Notes
            // -------------------------
            foreach (var bookingId in bookingIds)
            {
                var existingNotes = (await BookableResourceBookingOperation.GetBookableResourceBookingNotes(jobId))
                                        .Where(n => n.NoteId == bookingId)
                                        .ToList();

                string jobShareLink = await BookableResourceBookingOperation
                                            .GetBookableResourceBookingCustomerJobShareLinkAsync(bookingId);

                string newNote =
                    $"[{xOiSummary.WorkflowName}] Summary from ({xOiSummary.UserInitial}): {xOiSummary.WorkSummary}"
                    + Environment.NewLine
                    + jobShareLink;

                if (existingNotes.Any(n => BookableResourceBookingOperation.NoteEquals(n.Note, newNote)))
                {
                    _log.LogInformation($"Skipping duplicate note for booking {bookingId}");
                    continue;
                }

                Entity note = new Entity("msdyn_bookableresourcebookingquicknote")
                {
                    ["msdyn_quicknote_lookup_entity"] = new EntityReference("bookableresourcebooking", bookingId),
                    ["msdyn_text"] = newNote
                };

                await DataverseApi.Instance.CreateAsync(note);
                _log.LogInformation($"Created note for booking {bookingId}");
            }

            _log.LogInformation("Finish creating Bookable Resource Booking Notes");
        }
    }
}*/



/*GG
 /* public static async Task CreateBookableResourceBookingNoteAsync(ILogger _log, XOiWorkSummaryToBookableResourceData xOiWorkSummary, string jobId)
          {
              _log.LogInformation("Start creating Bookable Resource Booking Timeline");

              var bookingIds = await BookableResourceBookingOperation.GetBookableResourceBookingIdsAsync(jobId);

              foreach (var bookableResourceBookingId in bookingIds)
              {
                  var existingNotes = (await BookableResourceBookingOperation.GetBookableResourceBookingNotes(jobId))
                                          .Where(n => n.NoteId == bookableResourceBookingId)
                                          .ToList();

                  string customerJobshareLink = await BookableResourceBookingOperation.GetBookableResourceBookingCustomerJobShareLinkAsync(bookableResourceBookingId);

                  var newNote = $"[{xOiWorkSummary.WorkflowName}] Summary from ({xOiWorkSummary.UserInitial}): {xOiWorkSummary.WorkSummary}" +
                                $"{Environment.NewLine}{customerJobshareLink}";

                  if (existingNotes.Any(n => BookableResourceBookingOperation.NoteEquals(n.Note, newNote)))
                  {
                      _log.LogInformation($"✅ Skipping duplicate note for Booking ID {bookableResourceBookingId}");
                      continue;
                  }

                  _log.LogInformation($"Creating Note for Booking ID {bookableResourceBookingId} | Workflow: {xOiWorkSummary.WorkflowName} | Summary: {xOiWorkSummary.WorkSummary}");

                  Entity note = new Entity("msdyn_bookableresourcebookingquicknote")
                  {
                      ["msdyn_quicknote_lookup_entity"] = new EntityReference("bookableresourcebooking", bookableResourceBookingId),
                      ["msdyn_text"] = newNote
                  };

                  await DataverseApi.Instance.CreateAsync(note);

                  _log.LogInformation($"Finished creating note for booking ID {bookableResourceBookingId}");
              }

              _log.LogInformation("Finish creating Bookable Resource Booking Notes");
          }
      }
}
       var dataverseNotes = await BookableResourceBookingOperation.GetBookableResourceBookingNotes(jobId);

       _log.LogInformation("Finish receiving bookable resource booking notes from Dataverse");

       XOiWorkSummaryToBookableResourceData noteToUpdate = new XOiWorkSummaryToBookableResourceData();
       Guid dataverseNoteId = default;
       bool isDuplicate = false;

       _log.LogInformation("Start preparing note data for Create or Update");

       foreach (var dataverseNote in dataverseNotes)
       {
           if (dataverseNote.Note.Contains(xOiNotes.WorkflowName) && !dataverseNote.Note.Contains(xOiNotes.WorkSummary))
           {
               noteToUpdate = xOiNotes;
               dataverseNoteId = dataverseNote.NoteId;
           }
           else if (dataverseNote.Note.Contains(xOiNotes.WorkflowName))
           {
               isDuplicate = true;
           }
       }

       _log.LogInformation("Finish preparing notes data for Create or Update");

       if (noteToUpdate.IsFilled())
       {
           await BookableResourceBookingOperation.UpdateBookableResourceBookingNoteAsync(_log, xOiNotes, dataverseNoteId, jobId);
       }
       else if (isDuplicate == false)
       {
           await BookableResourceBookingOperation.CreateBookableResourceBookingNoteAsync(_log, xOiNotes, jobId);
       }
       else if (isDuplicate)
       {
           _log.LogInformation("Nothing to Create or Update");
       }
   }*/


