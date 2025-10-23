using Microsoft.Extensions.Logging;
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

     
         public static async Task CreateBookableResourceBookingNoteAsync(ILogger _log, XOiWorkSummaryToBookableResourceData xOiWorkSummary, string jobId)
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
 
        /*GG
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
    
}
