/*
 * XOi Job Owner — Work Order to Booking propagation
 * =================================================
 *
 * Dispatchers nominate the XOi job owner on the Work Order. XOi holds one owner
 * per job and the integration reads it from the booking, so this pushes the Work
 * Order value down onto a booking, whose Update step then drives the Azure
 * Function and the XOi updateJob call.
 *
 * Registration (model-driven Work Order form):
 *   Event    : Form OnLoad
 *   Function : ACL.XoiJobOwner.onLoad
 *   Options  : "Pass execution context as first parameter" — ticked
 *
 * Nothing is registered against the field directly. onLoad attaches an OnChange
 * flag and a post-save handler, so the booking is only written after the Work
 * Order value is actually committed — a nomination the dispatcher abandons by
 * cancelling the save never reaches XOi.
 */

"use strict";

var ACL = ACL || {};

ACL.XoiJobOwner = (function () {

    var FIELD = "acl_xoijobowner";
    var BOOKING_ENTITY = "bookableresourcebooking";
    var RESOURCE_ENTITY_SET = "bookableresources";

    // Booking statecode values treated as open. VERIFY AGAINST YOUR ORG before
    // relying on this: the option set on bookableresourcebooking is solution
    // specific, and picking the wrong value silently targets the wrong booking.
    var OPEN_STATECODES = [0];

    // Set false to leave the booking's owner in place when the Work Order field
    // is cleared. As written, clearing on the Work Order clears the booking too,
    // which reverts XOi to its default first-technician behaviour.
    var CLEAR_PROPAGATES = true;

    var NOTIFICATION_ID = "acl_xoijobowner_notice";

    var pendingPropagate = false;

    function stripBraces(guid) {
        return (guid || "").replace(/[{}]/g, "");
    }

    function notify(formContext, message) {
        formContext.ui.setFormNotification(message, "WARNING", NOTIFICATION_ID);
    }

    function clearNotice(formContext) {
        formContext.ui.clearFormNotification(NOTIFICATION_ID);
    }

    /*
     * Ranks open bookings newest first. starttime is the scheduled slot and is
     * the dispatcher's mental model of "latest", but it is nullable — unscheduled
     * bookings fall to the back and are ordered by createdon instead of being
     * dropped, so a work order whose bookings are all unscheduled still resolves.
     */
    function pickLatestOpenBooking(bookings) {
        var open = bookings.filter(function (b) {
            return OPEN_STATECODES.indexOf(b.statecode) !== -1;
        });

        if (open.length === 0) {
            return null;
        }

        open.sort(function (a, b) {
            var aStart = a.starttime ? Date.parse(a.starttime) : null;
            var bStart = b.starttime ? Date.parse(b.starttime) : null;

            if (aStart !== null && bStart !== null && aStart !== bStart) {
                return bStart - aStart;
            }
            if (aStart !== null && bStart === null) {
                return -1;
            }
            if (aStart === null && bStart !== null) {
                return 1;
            }

            return Date.parse(b.createdon) - Date.parse(a.createdon);
        });

        return open[0];
    }

    function propagate(formContext) {
        var workOrderId = stripBraces(formContext.data.entity.getId());
        if (!workOrderId) {
            return;
        }

        var attribute = formContext.getAttribute(FIELD);
        if (!attribute) {
            return;
        }

        var selected = attribute.getValue();
        var ownerId = selected && selected.length ? stripBraces(selected[0].id) : null;
        var ownerName = selected && selected.length ? selected[0].name : null;

        if (!ownerId && !CLEAR_PROPAGATES) {
            return;
        }

        var query =
            "?$select=bookableresourcebookingid,starttime,createdon,statecode,_" + FIELD + "_value" +
            "&$filter=_msdyn_workorder_value eq " + workOrderId;

        Xrm.WebApi.retrieveMultipleRecords(BOOKING_ENTITY, query).then(
            function (result) {
                if (!result.entities.length) {
                    notify(formContext, "XOi Job Owner was not applied: this work order has no bookings yet.");
                    return;
                }

                var target = pickLatestOpenBooking(result.entities);
                if (!target) {
                    notify(formContext, "XOi Job Owner was not applied: no open booking on this work order.");
                    return;
                }

                // The booking's Update step drives the Azure Function and an XOi
                // API call, so skip a write that would not change anything.
                var current = stripBraces(target["_" + FIELD + "_value"] || "");
                if (current === (ownerId || "")) {
                    clearNotice(formContext);
                    return;
                }

                var payload = {};
                payload[FIELD + "@odata.bind"] =
                    ownerId ? "/" + RESOURCE_ENTITY_SET + "(" + ownerId + ")" : null;

                Xrm.WebApi.updateRecord(BOOKING_ENTITY, target.bookableresourcebookingid, payload).then(
                    function () {
                        clearNotice(formContext);
                        formContext.ui.setFormNotification(
                            ownerId
                                ? "XOi Job Owner set to " + ownerName + " on the latest open booking."
                                : "XOi Job Owner cleared on the latest open booking.",
                            "INFO",
                            NOTIFICATION_ID);
                    },
                    function (error) {
                        notify(formContext, "XOi Job Owner could not be applied to the booking: " + error.message);
                    });
            },
            function (error) {
                notify(formContext, "XOi Job Owner could not read this work order's bookings: " + error.message);
            });
    }

    function onFieldChange() {
        pendingPropagate = true;
    }

    function onPostSave(executionContext) {
        if (!pendingPropagate) {
            return;
        }
        pendingPropagate = false;

        propagate(executionContext.getFormContext());
    }

    function onLoad(executionContext) {
        var formContext = executionContext.getFormContext();
        var attribute = formContext.getAttribute(FIELD);

        if (!attribute) {
            return;
        }

        attribute.addOnChange(onFieldChange);

        // Post-save keeps the booking write behind a committed Work Order value.
        // Older clients without addOnPostSave fall back to OnSave, which fires
        // before the commit completes but is still better than writing on change.
        if (formContext.data.entity.addOnPostSave) {
            formContext.data.entity.addOnPostSave(onPostSave);
        } else {
            formContext.data.entity.addOnSave(onPostSave);
        }
    }

    return {
        onLoad: onLoad
    };

})();
