using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using XOI_Integration.DataModels.Enums;

namespace XOI_Integration.Helper
{
    public static class DeserializeJSON
    {
        // -------------------------------------------------------
        // EXISTING — Extract BookableResourceBookingId
        // -------------------------------------------------------
        public static Guid GetBookableResourceBookingId(string jsonString)
        {
            var jsonObject = JObject.Parse(jsonString);

            Guid id = (Guid)jsonObject["InputParameters"][0]["value"]["Attributes"]
                .FirstOrDefault(a => (string)a["key"] == "bookableresourcebookingid")?["value"];

            return id;
        }

        // -------------------------------------------------------
        // EXISTING — Determine Create vs Update
        // -------------------------------------------------------
        public static OperationType GetDataverseOperationType(string jsonString)
        {
            var jsonObject = JObject.Parse(jsonString);

            string messageName = (string)jsonObject["MessageName"];

            return messageName switch
            {
                "Create" => OperationType.Create,
                "Update" => OperationType.Update,
                _ => OperationType.Update
            };
        }

        // -------------------------------------------------------
        // NEW — Extract WorkOrderId 
        // -------------------------------------------------------
        public static Guid GetWorkOrderId(string jsonString)
        {
            try
            {
                var json = JObject.Parse(jsonString);

                // ⚡ First place: InputParameters → Target → Attributes
                var attrs = json["InputParameters"]?[0]?["value"]?["Attributes"];
                if (attrs != null)
                {
                    var wo = attrs.FirstOrDefault(a => (string)a["key"] == "msdyn_workorder")?["value"];
                    if (wo != null)
                        return (Guid)wo;
                }

                // ⚡ Second place: Pipeline secure/unsecure (for plugin-format messages)
                var target = json["Target"]?["Attributes"];
                if (target != null)
                {
                    var wo = target.FirstOrDefault(a => (string)a["key"] == "msdyn_workorder")?["value"];
                    if (wo != null)
                        return (Guid)wo;
                }

                // ⚡ No work order in JSON
                return Guid.Empty;
            }
            catch
            {
                return Guid.Empty;
            }
        }
    }
}
