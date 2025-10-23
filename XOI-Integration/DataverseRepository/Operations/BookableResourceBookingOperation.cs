using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using XOI_Integration.DataModels;
using XOI_Integration.DataverseRepository.Provider;
using XOI_Integration.XOiRepository.XOiDataModels;

namespace XOI_Integration.DataverseRepository.Operations
{
    public static class BookableResourceBookingOperation
    {
        public static async Task UpdateBookableResourceBookingAsync(Guid bookableResourceBookingId, XOiToBookableResourceData xOiToBookableResourceData)
        {
            Entity bookableResource = new Entity("bookableresourcebooking")
            {
                Id = bookableResourceBookingId
            };

            bookableResource["sisps_xoi_vision_jobid"] = xOiToBookableResourceData.XOiVisionJobId;
            bookableResource["sisps_xoi_vision_joburl"] = xOiToBookableResourceData.XoiVisionJobURL;
            bookableResource["sisps_xoi_vision_jobshareurl"] = xOiToBookableResourceData.XoiVisionJobShareURL;
            bookableResource["sisps_xoi_vision_webjoburl"] = xOiToBookableResourceData.XoiVisionWebURL;

            await DataverseApi.Instance.UpdateAsync(bookableResource);
        }

        public static async Task UpdateBookableResourceBookingNoteAsync(ILogger _log, XOiWorkSummaryToBookableResourceData xOiWorkSummary, Guid noteId, string jobId)
        {
            _log.LogInformation("Start updating Bookable Resource Booking Timeline");

            var bookingIds = await GetBookableResourceBookingIdsAsync(jobId);

            foreach (var bookableResourceBookingId in bookingIds)
            {
                string customerJobshareLink = await GetBookableResourceBookingCustomerJobShareLinkAsync(bookableResourceBookingId);

                _log.LogInformation($"Updating Note ID {noteId} for Booking ID {bookableResourceBookingId} | Workflow: {xOiWorkSummary.WorkflowName} | Summary: {xOiWorkSummary.WorkSummary}");

                Entity note = new Entity("msdyn_bookableresourcebookingquicknote")
                {
                    Id = noteId
                };

                note["msdyn_quicknote_lookup_entity"] = new EntityReference("bookableresourcebooking", bookableResourceBookingId);
                note["msdyn_text"] = $"[{xOiWorkSummary.WorkflowName}] Summary from ({xOiWorkSummary.UserInitial}) {xOiWorkSummary.WorkSummary}" +
                                     $"{Environment.NewLine}{customerJobshareLink}";

                await DataverseApi.Instance.UpdateAsync(note);

                _log.LogInformation($"Finished updating note for booking ID {bookableResourceBookingId}");
            }

            _log.LogInformation("Finish updating Bookable Resource Booking Notes");
        }

        public static async Task CreateBookableResourceBookingNoteAsync(ILogger _log, XOiWorkSummaryToBookableResourceData xOiWorkSummary, string jobId)
        {
            _log.LogInformation("Start creating Bookable Resource Booking Timeline");

            var bookingIds = await GetBookableResourceBookingIdsAsync(jobId);

            foreach (var bookableResourceBookingId in bookingIds)
            {
                //gg update Fetch existing notes
                var existingNotes = await GetBookableResourceBookingNotes(jobId);
                string customerJobshareLink = await GetBookableResourceBookingCustomerJobShareLinkAsync(bookableResourceBookingId);

                // Build the note text exactly how it will be created
                var newNote = $"[{xOiWorkSummary.WorkflowName}] Summary from ({xOiWorkSummary.UserInitial}): {xOiWorkSummary.WorkSummary}" +
                              $"{Environment.NewLine}{customerJobshareLink}";

                // Check if note already exists (avoid duplicates)
                if (existingNotes.Any(n => NoteEquals(n.Note, newNote)))
                {
                    _log.LogInformation($"✅ Skipping duplicate note for Booking ID {bookableResourceBookingId}");
                    continue;
                }

                // Create the note if it does not exist
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
        /*public static async Task CreateBookableResourceBookingNoteAsync(
    ILogger _log,
    XOiWorkSummaryToBookableResourceData xOiWorkSummary,
    string jobId)
        {
            _log.LogInformation($"[XOI] Start creating Bookable Resource Booking Timeline for JobID: {jobId}");

            Guid bookableResourceBookingId = await GetBookableResourceBookingIdAsync(jobId);
            _log.LogInformation($"[XOI] Resolved Booking ID: {bookableResourceBookingId}");

            string customerJobshareLink = await GetBookableResourceBookingCustomerJobShareLinkAsync(bookableResourceBookingId);
            _log.LogInformation($"[XOI] Customer Share Link: {customerJobshareLink ?? "null"}");

            // Diagnostic — Log hash of note content for deduplication analysis
            string noteContent = $"[{xOiWorkSummary.WorkflowName}] Summary from ({xOiWorkSummary.UserInitial}): {xOiWorkSummary.WorkSummary}{Environment.NewLine}{customerJobshareLink}";
            string noteHash = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(noteContent.Substring(0, Math.Min(50, noteContent.Length))));
            _log.LogInformation($"[XOI] Note Hash (first 50 chars): {noteHash}");

            // Optional: Add deduplication check before creating
            var existingNotes = await GetBookableResourceBookingNotes(jobId);
            if (existingNotes.Any(n => n.Note == noteContent))
            {
                _log.LogWarning($"[XOI] Duplicate note detected for JobID: {jobId}, skipping creation.");
                return;
            }

            Entity note = new Entity("msdyn_bookableresourcebookingquicknote")
            {
                ["msdyn_quicknote_lookup_entity"] = new EntityReference("bookableresourcebooking", bookableResourceBookingId),
                ["msdyn_text"] = noteContent
            };

            _log.LogInformation($"[XOI] Sending note create request to Dataverse...");

            try
            {
                await DataverseApi.Instance.CreateAsync(note);
                _log.LogInformation($"[XOI] Note successfully created in Dataverse for BookingID: {bookableResourceBookingId}");
            }
            catch (Exception ex)
            {
                _log.LogError($"[XOI] ERROR creating note: {ex.Message}\n{ex.StackTrace}");
                throw;
            }

            _log.LogInformation($"[XOI] Finish creating Bookable Resource Booking Note for JobID: {jobId}");
        }
*/
        /* public static async Task CreateBookableResourceBookingNoteAsync(
     ILogger _log,
     XOiWorkSummaryToBookableResourceData xOiWorkSummary,
     string jobId)
         {
             _log.LogInformation($"[XOI] Start creating Bookable Resource Booking Timeline for JobID: {jobId}");

             var bookingIds = await GetBookableResourceBookingIdsAsync(jobId);
             if (bookingIds == null || !bookingIds.Any())
             {
                 _log.LogWarning($"[XOI] No bookings found for job {jobId}");
                 return;
             }

             foreach (var bookingId in bookingIds)
             {
                 string customerJobshareLink = await GetBookableResourceBookingCustomerJobShareLinkAsync(bookingId);
                 var newNoteText = $"[{xOiWorkSummary.WorkflowName}] Summary from ({xOiWorkSummary.UserInitial}): {xOiWorkSummary.WorkSummary}{Environment.NewLine}{customerJobshareLink}";

                 // 🔹 Fetch notes only for this booking (not all)
                 QueryExpression query = new QueryExpression("msdyn_bookableresourcebookingquicknote")
                 {
                     ColumnSet = new ColumnSet("msdyn_text"),
                     Criteria = new FilterExpression
                     {
                         Conditions =
                 {
                     new ConditionExpression("msdyn_quicknote_lookup_entity", ConditionOperator.Equal, bookingId)
                 }
                     }
                 };
                 var existingNotes = await DataverseApi.Instance.RetrieveMultipleAsync(query);
                 bool noteExists = existingNotes.Entities.Any(e => NoteEquals(e.GetAttributeValue<string>("msdyn_text"), newNoteText));

                 if (noteExists)
                 {
                     _log.LogInformation($"✅ Skipping duplicate note for booking {bookingId} (already exists).");
                     continue;
                 }

                 // Create new note
                 _log.LogInformation($"🆕 Creating note for Booking {bookingId}");
                 Entity note = new Entity("msdyn_bookableresourcebookingquicknote")
                 {
                     ["msdyn_quicknote_lookup_entity"] = new EntityReference("bookableresourcebooking", bookingId),
                     ["msdyn_text"] = newNoteText
                 };

                 await DataverseApi.Instance.CreateAsync(note);
                 _log.LogInformation($"✅ Note created for Booking {bookingId}");
             }

             _log.LogInformation($"[XOI] Finish creating Bookable Resource Booking Note for JobID: {jobId}");
         }*/

        public static async Task<string> GetXOiJobIdAsync(Guid bookableResourceBookingId)
        {
            QueryExpression query = new QueryExpression("bookableresourcebooking")
            {
                ColumnSet = new ColumnSet("sisps_xoi_vision_jobid"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("bookableresourcebookingid", ConditionOperator.Equal, bookableResourceBookingId)
                    }
                }
            };

            var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);
            return response.Entities.FirstOrDefault()?.GetAttributeValue<string>("sisps_xoi_vision_jobid");
        }

        public static async Task<List<BookableResourceBookingTimeline>> GetBookableResourceBookingTimelines(string jobId)
        {
            var bookingIds = await GetBookableResourceBookingIdsAsync(jobId);
            List<BookableResourceBookingTimeline> timelines = new List<BookableResourceBookingTimeline>();

            foreach (var bookableResourceBookingId in bookingIds)
            {
                QueryExpression query = new QueryExpression("annotation")
                {
                    ColumnSet = new ColumnSet("subject", "notetext"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("objectid", ConditionOperator.Equal, bookableResourceBookingId)
                        }
                    }
                };

                var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);

                foreach (var entity in response.Entities)
                {
                    timelines.Add(new BookableResourceBookingTimeline
                    {
                        Title = entity.GetAttributeValue<string>("subject"),
                        Timelinetext = entity.GetAttributeValue<string>("notetext"),
                        TimelineId = entity.GetAttributeValue<Guid>("annotationid")
                    });
                }
            }

            return timelines;
        }

        public static async Task<List<BookableResourceBookingNote>> GetBookableResourceBookingNotes(string jobId)
        {
            var bookingIds = await GetBookableResourceBookingIdsAsync(jobId);
            List<BookableResourceBookingNote> notes = new List<BookableResourceBookingNote>();

            foreach (var bookableResourceBookingId in bookingIds)
            {
                QueryExpression query = new QueryExpression("msdyn_bookableresourcebookingquicknote")
                {
                    ColumnSet = new ColumnSet("msdyn_text"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("msdyn_quicknote_lookup_entity", ConditionOperator.Equal, bookableResourceBookingId)
                        }
                    }
                };

                var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);

                foreach (var entity in response.Entities)
                {
                    notes.Add(new BookableResourceBookingNote
                    {
                        Note = entity.GetAttributeValue<string>("msdyn_text"),
                        NoteId = entity.GetAttributeValue<Guid>("msdyn_bookableresourcebookingquicknoteid")
                    });
                }
            }

            return notes;
        }


        public static async Task CopyJobDetailsToCurrentAsync(Guid currentBookableResourceBookingId, Guid copyFromBookableResourceBookingId)
        {
            var query = new QueryExpression("bookableresourcebooking")
            {
                ColumnSet = new ColumnSet("sisps_xoi_vision_jobid", "sisps_xoi_vision_jobshareurl", "sisps_xoi_vision_webjoburl", "sisps_xoi_vision_joburl"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("bookableresourcebookingid", ConditionOperator.Equal, copyFromBookableResourceBookingId)
                    }
                }
            };

            var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);
            var resourceToCopyFrom = response.Entities.FirstOrDefault();

            if (resourceToCopyFrom == null) return;

            var updateEntity = new Entity("bookableresourcebooking", currentBookableResourceBookingId)
            {
                ["sisps_xoi_vision_jobid"] = resourceToCopyFrom.GetAttributeValue<string>("sisps_xoi_vision_jobid"),
                ["sisps_xoi_vision_jobshareurl"] = resourceToCopyFrom.GetAttributeValue<string>("sisps_xoi_vision_jobshareurl"),
                ["sisps_xoi_vision_webjoburl"] = resourceToCopyFrom.GetAttributeValue<string>("sisps_xoi_vision_webjoburl"),
                ["sisps_xoi_vision_joburl"] = resourceToCopyFrom.GetAttributeValue<string>("sisps_xoi_vision_joburl")
            };

            await DataverseApi.Instance.UpdateAsync(updateEntity);
        }
        //gg change to public
        public static async Task<string> GetBookableResourceBookingCustomerJobShareLinkAsync(Guid bookableResourceBookingId)
        {
            QueryExpression query = new QueryExpression("bookableresourcebooking")
            {
                ColumnSet = new ColumnSet("sisps_xoi_vision_jobshareurl"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("bookableresourcebookingid", ConditionOperator.Equal, bookableResourceBookingId)
                    }
                }
            };

            var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);
            return response.Entities.FirstOrDefault()?.GetAttributeValue<string>("sisps_xoi_vision_jobshareurl");
        }

        public static async Task<List<Guid>> GetBookableResourceBookingIdsAsync(string jobId)
        {
            QueryExpression query = new QueryExpression("bookableresourcebooking")
            {
                ColumnSet = new ColumnSet("bookableresourcebookingid"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("sisps_xoi_vision_jobid", ConditionOperator.Equal, jobId)
                    }
                }
            };

            var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);
            return response.Entities.Select(e => e.GetAttributeValue<Guid>("bookableresourcebookingid")).ToList();
        }

        public static async Task<Guid> GetBookableResourceBookingIdAsync(string jobId)
        {
            var bookingIds = await GetBookableResourceBookingIdsAsync(jobId);
            return bookingIds.FirstOrDefault(); // gg update Returns Guid.Empty if no bookings found
        }
        /*gg*/
        public static bool NoteEquals(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b)) return true;
            a = a?.Trim().Replace("\r", "").Replace("\n", "");
            b = b?.Trim().Replace("\r", "").Replace("\n", "");
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

    }
}
