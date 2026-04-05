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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace XOI_Integration.DataverseRepository.Operations
{
    public static class BookableResourceBookingOperation
    {
        // =========================================================
        // CORE UPDATE (From full XOi object)
        // =========================================================
        public static async Task UpdateBookableResourceBookingAsync(Guid id, XOiToBookableResourceData x)
        {
            Entity entity = new Entity("bookableresourcebooking") { Id = id };

            entity["sisps_xoi_vision_jobid"] = x.XOiVisionJobId;
            entity["sisps_xoi_vision_joburl"] = x.XoiVisionJobURL;
            entity["sisps_xoi_vision_jobshareurl"] = x.XoiVisionJobShareURL;

            // 03042026 Always use VisionWeb.ViewJob URL for webjoburl — ContributeToJob URL belongs to a different field and must not go here
            entity["sisps_xoi_vision_webjoburl"] = x.XoiVisionWebURL;

            await DataverseApi.Instance.UpdateAsync(entity);
        }
        //Hash
        private static string ComputeHash(string input)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(input ?? "");
            return Convert.ToHexString(sha.ComputeHash(bytes));
        }

        // =========================================================
        // UPDATE JOB ID ON WORK ORDER 
        // =========================================================
        public static async Task UpdateXOiJobIdOnWorkOrderAsync(Guid workOrderId, string xOiJobId)
        {
            if (workOrderId == Guid.Empty || string.IsNullOrEmpty(xOiJobId))
                return;

            Entity wo = new Entity("msdyn_workorder") { Id = workOrderId };
            wo["acl_xoi_vision_jobid"] = xOiJobId;

            await Task.Run(() => DataverseApi.Instance.Update(wo));
        }
        // =========================================================
        // UPDATE WORKFLOW JOB ID ON BOOKING
        // =========================================================
        public static async Task UpdateWorkflowJobIdOnBookingAsync(Guid bookingId, string workflowJobId)
        {
            if (bookingId == Guid.Empty || string.IsNullOrEmpty(workflowJobId))
                return;

            Entity entity = new Entity("bookableresourcebooking")
            {
                Id = bookingId
            };

            // Save workflowJobId to custom field
            entity["acl_xoi_workflowjobid"] = workflowJobId;

            await DataverseApi.Instance.UpdateAsync(entity);
        }


        // =========================================================
        // UPDATE JOB ID ON BOOKING — FULL OBJECT 
        // =========================================================
        public static async Task UpdateXOiJobIdOnBookingAsync(Guid bookingId, XOiToBookableResourceData x)
        {
            if (bookingId == Guid.Empty || x == null)
                return;

            string finalUrl =
                !string.IsNullOrEmpty(x.ContributeToJobUrl)
                    ? x.ContributeToJobUrl
                    : x.XoiVisionWebURL;

            Entity entity = new Entity("bookableresourcebooking")
            {
                Id = bookingId,
                ["sisps_xoi_vision_jobid"] = x.XOiVisionJobId,
                ["sisps_xoi_vision_webjoburl"] = finalUrl,
                ["sisps_xoi_vision_joburl"] = x.XoiVisionJobURL,
                ["sisps_xoi_vision_jobshareurl"] = x.XoiVisionJobShareURL
            };

            await DataverseApi.Instance.UpdateAsync(entity);
        }

        // =========================================================
        // UPDATE JOB ID ON BOOKING — STRING VERSION
        // =========================================================
        public static async Task UpdateXOiJobIdOnBookingAsync(Guid bookingId, string jobId)
        {
            if (bookingId == Guid.Empty || string.IsNullOrEmpty(jobId))
                return;

            Entity entity = new Entity("bookableresourcebooking") { Id = bookingId };
            entity["sisps_xoi_vision_jobid"] = jobId;

            await DataverseApi.Instance.UpdateAsync(entity);
        }


        // =========================================================
        // GET WEB URL
        // =========================================================
        public static async Task<string> GetWebJobUrlAsync(Guid bookingId)
        {
            var entity = await Task.Run(() =>
                DataverseApi.Instance.Retrieve(
                    "bookableresourcebooking",
                    bookingId,
                    new ColumnSet("sisps_xoi_vision_webjoburl")
                ));

            return entity?.GetAttributeValue<string>("sisps_xoi_vision_webjoburl");
        }

        // =========================================================
        // UPDATE WEB URL
        // =========================================================
        public static async Task UpdateWebJobUrlOnBookingAsync(Guid bookingId, string jobId)
        {
            if (bookingId == Guid.Empty || string.IsNullOrEmpty(jobId))
                return;

            string finalUrl =
                jobId.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? jobId
                    : $"https://visionweb.xoi.io/jobactivity/{jobId}";

            Entity entity = new Entity("bookableresourcebooking") { Id = bookingId };
            entity["sisps_xoi_vision_webjoburl"] = finalUrl;

            await DataverseApi.Instance.UpdateAsync(entity);
        }

        // =========================================================
        // UPDATE SHARE URL
        // =========================================================
        public static async Task UpdateJobShareUrlOnBookingAsync(Guid bookingId, string shareUrl)
        {
            if (bookingId == Guid.Empty || string.IsNullOrEmpty(shareUrl))
                return;

            Entity entity = new Entity("bookableresourcebooking") { Id = bookingId };
            entity["sisps_xoi_vision_jobshareurl"] = shareUrl;

            await DataverseApi.Instance.UpdateAsync(entity);
        }

        // =========================================================
        // PRIMARY JOB ID RESOLUTION
        // =========================================================
        public static async Task<string> GetXOiJobIdAsync(Guid bookingId)
        {
            var booking = await Task.Run(() =>
                DataverseApi.Instance.Retrieve(
                    "bookableresourcebooking",
                    bookingId,
                    new ColumnSet("sisps_xoi_vision_jobid", "msdyn_workorder")
                ));

            if (booking.Contains("sisps_xoi_vision_jobid"))
                return (string)booking["sisps_xoi_vision_jobid"];

            if (booking.Contains("msdyn_workorder"))
            {
                Guid woId = booking.GetAttributeValue<EntityReference>("msdyn_workorder").Id;

                var workOrder = await Task.Run(() =>
                    DataverseApi.Instance.Retrieve(
                        "msdyn_workorder",
                        woId,
                        new ColumnSet("acl_xoi_vision_jobid")
                    ));

                if (workOrder?.Contains("acl_xoi_vision_jobid") == true)
                    return (string)workOrder["acl_xoi_vision_jobid"];
            }

            return null;
        }

        // =========================================================
        // GET NOTES FOR THIS SPECIFIC BOOKING (NEW FIX)
        // =========================================================
        public static async Task<List<BookableResourceBookingNote>> GetNotesForBookingAsync(Guid bookingId)
        {
            QueryExpression query = new QueryExpression("msdyn_bookableresourcebookingquicknote")
            {
                ColumnSet = new ColumnSet("msdyn_text", "acl_xoisummaryhash"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(
                            "msdyn_quicknote_lookup_entity",
                            ConditionOperator.Equal,
                            bookingId
                        )
                    }
                }
            };

            var result = await DataverseApi.Instance.RetrieveMultipleAsync(query);
            return result.Entities.Select(e => new BookableResourceBookingNote
            {
                Note = e.GetAttributeValue<string>("msdyn_text"),
                Hash = e.GetAttributeValue<string>("acl_xoisummaryhash"),
                NoteId = e.Id
            }).ToList();
        }

        // =========================================================
        // NOTE CREATION
        // =========================================================
        // 03042026 Restored original signature — workflowJobId lookup is reliable because
        // XoiToCeUpdateBooking step 4 always writes workflowJobId to the correct booking
        // (resolved via email match) BEFORE this method is called
        public static async Task CreateBookableResourceBookingNoteAsync(
    ILogger log,
    XOiWorkSummaryToBookableResourceData summary,
    string jobId,
    Guid bookingId)
        {
            log.LogInformation("Start creating notes (workflow-specific)");

            Guid currentBookingId = bookingId;

            if (currentBookingId == Guid.Empty)
            {
                log.LogWarning("No booking resolved — skipping note.");
                return;
            }

            string shareLink = await GetBookableResourceBookingCustomerJobShareLinkAsync(currentBookingId);

            var tech = GetTechnicianInfoFromBooking(currentBookingId);
            var techName = tech.FullName ?? summary.UserInitial;
            log.LogInformation($"Technician for booking {currentBookingId}: {techName} (UserId: {tech.UserId})");

            string noteText = $"[{summary.WorkflowName}] Summary from ({techName}): {summary.WorkSummary}"
                              + Environment.NewLine
                              + shareLink;

            // Hash-based duplicate check using acl_xoisummaryhash field
            string hash = ComputeHash(noteText);
            var existingNotes = await GetNotesForBookingAsync(currentBookingId);
            if (existingNotes.Any(n => n.Hash == hash))
            {
                log.LogInformation($"Duplicate note detected via hash for booking {currentBookingId} — skipped.");
                return;
            }

            Entity note = new Entity("msdyn_bookableresourcebookingquicknote")
            {
                ["msdyn_quicknote_lookup_entity"] = new EntityReference("bookableresourcebooking", currentBookingId),
                ["msdyn_text"] = noteText,
                ["acl_xoisummaryhash"] = hash
            };

            if (tech.UserId.HasValue && tech.UserId.Value != Guid.Empty)
            {
                note["ownerid"] = new EntityReference("systemuser", tech.UserId.Value);
            }

            await DataverseApi.Instance.CreateAsync(note);

            log.LogInformation($"Note created for booking {currentBookingId}");
        }

        /*  public static async Task CreateBookableResourceBookingNoteAsync(
        ILogger log,
        XOiWorkSummaryToBookableResourceData summary,
        string jobId)
          {
              log.LogInformation("Start creating notes");

              //  Determine the CURRENT booking based on workflow event
              // The webhook ALWAYS contains exactly ONE booking associated with the job summary.
              Guid currentBookingId = await GetBookableResourceBookingIdAsync(jobId);


              if (currentBookingId == Guid.Empty)
              {
                  log.LogWarning("No matching booking found for workflowJobId — note skipped.");
                  return;
              }

              if (currentBookingId == Guid.Empty)
              {
                  log.LogWarning("No BRB found for JobId, note not created.");
                  return;
              }

              //  Only load existing notes for THIS ONE booking
              var existingNotes = await GetBookableResourceBookingNotesForSingleBooking(currentBookingId);

              string shareLink = await GetBookableResourceBookingCustomerJobShareLinkAsync(currentBookingId);

              string noteText =
                  $"[{summary.WorkflowName}] Summary from ({summary.UserInitial}): {summary.WorkSummary}"
                  + Environment.NewLine
                  + shareLink;

              // Check duplicate for this booking only
              if (existingNotes.Any(n => NoteEquals(n.Note, noteText)))
              {
                  log.LogInformation($"Skipping duplicate note for booking {currentBookingId}");
                  return;
              }

              //  Create note ONLY for current booking
              Entity note = new Entity("msdyn_bookableresourcebookingquicknote")
              {
                  ["msdyn_quicknote_lookup_entity"] =
                      new EntityReference("bookableresourcebooking", currentBookingId),

                  ["msdyn_text"] = noteText
              };

              await DataverseApi.Instance.CreateAsync(note);
              log.LogInformation($"Note created for booking {currentBookingId}");

              log.LogInformation("Finish creating Bookable Resource Booking Notes");
          }*/
        public static async Task<List<BookableResourceBookingNote>> GetBookableResourceBookingNotesForSingleBooking(Guid bookingId)
        {
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

            var result = await DataverseApi.Instance.RetrieveMultipleAsync(query);

            return result.Entities.Select(e => new BookableResourceBookingNote
            {
                Note = e.GetAttributeValue<string>("msdyn_text"),
                NoteId = e.Id
            }).ToList();
        }


        // =========================================================
        // GET ALL NOTES FOR JOB (unchanged)
        // =========================================================
        public static async Task<List<BookableResourceBookingNote>> GetBookableResourceBookingNotes(string jobId)
        {
            var bookingIds = await GetBookableResourceBookingIdsAsync(jobId);
            List<BookableResourceBookingNote> notes = new();

            foreach (var bookingId in bookingIds)
            {
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

                var result = await DataverseApi.Instance.RetrieveMultipleAsync(query);

                notes.AddRange(
                    result.Entities.Select(e => new BookableResourceBookingNote
                    {
                        Note = e.GetAttributeValue<string>("msdyn_text"),
                        NoteId = e.Id
                    }));
            }

            return notes;
        }

        // =========================================================
        // COPY JOB DETAILS ACROSS BOOKINGS
        // =========================================================
        public static async Task CopyJobDetailsToCurrentAsync(
    Guid currentBookingId,
    Guid sourceBookingId)
        {
            QueryExpression query = new QueryExpression("bookableresourcebooking")
            {
                ColumnSet = new ColumnSet(
                    "sisps_xoi_vision_jobid",
                    "sisps_xoi_vision_jobshareurl",
                    "sisps_xoi_vision_webjoburl",
                    "sisps_xoi_vision_joburl"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
            {
                new ConditionExpression(
                    "bookableresourcebookingid",
                    ConditionOperator.Equal,
                    sourceBookingId)
            }
                }
            };

            var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);
            var source = response.Entities.FirstOrDefault();
            if (source == null) return;

            Entity update = new Entity("bookableresourcebooking", currentBookingId)
            {
                ["sisps_xoi_vision_jobid"] =
                    source.GetAttributeValue<string>("sisps_xoi_vision_jobid"),

                ["sisps_xoi_vision_jobshareurl"] =
                    source.GetAttributeValue<string>("sisps_xoi_vision_jobshareurl"),

                ["sisps_xoi_vision_webjoburl"] =
                    source.GetAttributeValue<string>("sisps_xoi_vision_webjoburl")
            };

            //  Replace ONLY owner email in job URL
            string sourceJobUrl =
                source.GetAttributeValue<string>("sisps_xoi_vision_joburl");

            if (!string.IsNullOrWhiteSpace(sourceJobUrl))
            {
                string updatedJobUrl =
                     ReplaceOwnerInVisionJobUrl(
                        sourceJobUrl,
                        currentBookingId);

                update["sisps_xoi_vision_joburl"] = updatedJobUrl;
            }

            await DataverseApi.Instance.UpdateAsync(update);
        }

        //Replace ONLY the owner in payload
        private static string ReplaceOwnerInVisionJobUrl(
     string jobUrl,
     Guid bookingId)
        {
            const string payloadKey = "?payload=";

            int payloadIndex =
                jobUrl.IndexOf(payloadKey, StringComparison.OrdinalIgnoreCase);

            if (payloadIndex < 0)
                return jobUrl;

            string baseUrl = jobUrl.Substring(0, payloadIndex);
            string encodedPayload =
                jobUrl.Substring(payloadIndex + payloadKey.Length);

            string jsonPayload =
                Uri.UnescapeDataString(encodedPayload);

            // ✅ SAFE JSON PARSE (Newtonsoft)
            JObject payload = JObject.Parse(jsonPayload);

            //to be removed Commented 22012026//string email = GetTechnicianEmailFromBooking(bookingId);
            string email = GetTechnicianInfoFromBooking(bookingId).Email;

            if (string.IsNullOrWhiteSpace(email))
                return jobUrl;

            string existingOwner = payload["owner"]?.ToString();

            // ✅ LOOP BREAK / IDEMPOTENCY
            if (string.Equals(existingOwner, email, StringComparison.OrdinalIgnoreCase))
                return jobUrl;

            // Replace ONLY owner
            payload["owner"] = email;

            string newJson = payload.ToString(Formatting.None);
            string newEncodedPayload =
                Uri.EscapeDataString(newJson);

            return $"{baseUrl}?payload={newEncodedPayload}";
        }


        //to be removed Commented 22012026//
        /*    //Resolve technician email
            private static string GetTechnicianEmailFromBooking(Guid bookingId)
            {
                // 1️⃣ Booking → Resource (correct logical name: resource)
                var booking = DataverseApi.Instance.Retrieve(
                    "bookableresourcebooking",
                    bookingId,
                    new ColumnSet("resource")
                );

                var resourceRef =
                    booking.GetAttributeValue<EntityReference>("resource");
                if (resourceRef == null)
                    return null;

                // 2️⃣ Resource → User
                var resource = DataverseApi.Instance.Retrieve(
                    "bookableresource",
                    resourceRef.Id,
                    new ColumnSet("userid")
                );

                var userRef =
                    resource.GetAttributeValue<EntityReference>("userid");
                if (userRef == null)
                    return null;

                // 3️⃣ User → Email
                var user = DataverseApi.Instance.Retrieve(
                    "systemuser",
                    userRef.Id,
                    new ColumnSet("internalemailaddress")
                );

                return user.GetAttributeValue<string>("internalemailaddress");
            }

    */


        // =========================================================
        // CUSTOMER JOB SHARE LINK
        // =========================================================
        public static async Task<string> GetBookableResourceBookingCustomerJobShareLinkAsync(Guid bookingId)
        {
            QueryExpression query = new QueryExpression("bookableresourcebooking")
            {
                ColumnSet = new ColumnSet("sisps_xoi_vision_jobshareurl"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("bookableresourcebookingid", ConditionOperator.Equal, bookingId)
                    }
                }
            };

            var response = await DataverseApi.Instance.RetrieveMultipleAsync(query);
            return response.Entities.FirstOrDefault()?.GetAttributeValue<string>("sisps_xoi_vision_jobshareurl");
        }

        //====Helper====

        //22012026
        private class TechnicianInfo
        {
            public Guid? UserId { get; set; }
            public string FullName { get; set; }
            public string Email { get; set; }
        }
        private static TechnicianInfo GetTechnicianInfoFromBooking(Guid bookingId)
        {
            // Booking -> Resource
            var booking = DataverseApi.Instance.Retrieve(
                "bookableresourcebooking",
                bookingId,
                new ColumnSet("resource")
            );

            var resourceRef = booking.GetAttributeValue<EntityReference>("resource");
            if (resourceRef == null) return new TechnicianInfo();

            // Resource -> name + userid
            var resource = DataverseApi.Instance.Retrieve(
                "bookableresource",
                resourceRef.Id,
                new ColumnSet("name", "userid")
            );

            var resourceName = resource.GetAttributeValue<string>("name");
            var userRef = resource.GetAttributeValue<EntityReference>("userid");

            if (userRef == null)
            {
                // no user mapped, still can return resource name
                return new TechnicianInfo { FullName = resourceName };
            }

            // User -> fullname + email
            var user = DataverseApi.Instance.Retrieve(
                "systemuser",
                userRef.Id,
                new ColumnSet("fullname", "internalemailaddress")
            );

            return new TechnicianInfo
            {
                UserId = userRef.Id,
                FullName = !string.IsNullOrWhiteSpace(resourceName)
                            ? resourceName
                            : user.GetAttributeValue<string>("fullname"),
                Email = user.GetAttributeValue<string>("internalemailaddress")
            };
        }


        // =========================================================
        // GET ALL BOOKING IDs FOR ONE JOB
        // =========================================================
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

                  return response.Entities
                      .Select(e => e.GetAttributeValue<Guid>("bookableresourcebookingid"))
                      .ToList();
              }
       

        public static async Task<Guid> GetBookableResourceBookingIdAsync(string jobId)
        {
            var list = await GetBookableResourceBookingIdsAsync(jobId);
            return list.FirstOrDefault();
        }

        // =========================================================
        // NOTE TEXT CLEAN COMPARISON
        // =========================================================
        public static bool NoteEquals(string a, string b)
        {
            a = a?.Trim().Replace("\r", "").Replace("\n", "");
            b = b?.Trim().Replace("\r", "").Replace("\n", "");
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
        public static async Task<Guid> GetBookingIdByWorkflowJobIdAsync(string workflowJobId)
        {
            if (string.IsNullOrWhiteSpace(workflowJobId))
                return Guid.Empty;

            QueryExpression query = new QueryExpression("bookableresourcebooking")
            {
                ColumnSet = new ColumnSet("bookableresourcebookingid"),
                Criteria = new FilterExpression
                {
                    Conditions =
            {
                new ConditionExpression("acl_xoi_workflowjobid", ConditionOperator.Equal, workflowJobId)
            }
                }
            };

            var result = await DataverseApi.Instance.RetrieveMultipleAsync(query);
            return result.Entities.FirstOrDefault()?.Id ?? Guid.Empty;
        }

        // =========================================================
        // RESOLVE BOOKING BY TECHNICIAN + CLOSEST SCHEDULED DATE
        // Uses webhook FiredAt (actual completion time) vs booking starttime
        // to correctly identify which booking a technician completed
        // when same technician has multiple bookings on same job
        // =========================================================
        public static async Task<Guid> ResolveBookingByTechnicianAndDateAsync(
            ILogger log,
            List<Guid> bookingIds,
            string assigneeEmail,
            DateTime firedAt)
        {
            var candidates = new List<(Guid Id, DateTime? Start, string Email, string WorkflowId)>();

            foreach (var id in bookingIds)
            {
                var brb = DataverseApi.Instance.Retrieve(
                    "bookableresourcebooking",
                    id,
                    new ColumnSet("starttime", "acl_xoi_workflowjobid")
                );

                var start = brb.Contains("starttime")
                    ? brb.GetAttributeValue<DateTime>("starttime")
                    : (DateTime?)null;

                var wfId = brb.GetAttributeValue<string>("acl_xoi_workflowjobid");
                var tech = GetTechnicianInfoFromBooking(id);

                candidates.Add((id, start, tech.Email, wfId));
            }

            // 1. Filter by technician email (now reliable — cache key includes workflowJobId)
            var techMatches = !string.IsNullOrEmpty(assigneeEmail)
                ? candidates
                    .Where(c => string.Equals(c.Email, assigneeEmail, StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : candidates;

            if (!techMatches.Any())
            {
                log.LogWarning($"No bookings matched email '{assigneeEmail}' — using all bookings");
                techMatches = candidates;
            }

            log.LogInformation($"Email filter '{assigneeEmail}' matched {techMatches.Count} booking(s)");

            // 2. Prefer unmapped bookings — fallback to all tech bookings if all mapped (second workflow scenario)
            var pool = techMatches.Where(c => string.IsNullOrWhiteSpace(c.WorkflowId)).ToList();
            if (!pool.Any())
            {
                log.LogWarning("All technician bookings already mapped — second workflow scenario, using all tech bookings");
                pool = techMatches;
            }

            // 3. Pick closest scheduled starttime to webhook FiredAt
            var selected = pool
                .OrderBy(c =>
                    c.Start.HasValue
                        ? Math.Abs((c.Start.Value - firedAt).TotalMinutes)
                        : double.MaxValue)
                .First();

            log.LogInformation($"ResolveBookingByTechnicianAndDate → booking {selected.Id} (start: {selected.Start}, email: {selected.Email}, firedAt: {firedAt})");
            return selected.Id;
        }

    }
}