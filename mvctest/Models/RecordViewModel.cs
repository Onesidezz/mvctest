using System.ComponentModel;
using TRIM.SDK;

namespace mvctest.Models
{
    public class RecordViewModel
    {
        public long? URI { get; set; }
        public string? Title { get; set; }
        public string? Container { get; set; }
        public string? AllParts { get; set; }
        public string? Assignee { get; set; }
        public string? DateCreated { get; set; }
        public string? IsContainer { get; set; } 
        public Dictionary<string,long>? ContainerCount { get; set; }
        public long? Totalrecords { get; set; }

        // New search filter properties
        public string? Region { get; set; }
        public string? Country { get; set; }
        public string? BillTo { get; set; }
        public string? ShipTo { get; set; }
        public string? ClientId { get; set; }

        public dynamic ACL { get; set; }
        public string? DownloadLink { get; set; }    // Esource
        public List<ContainerRecordsInfo> containerRecordsInfo { get; set; }

    }
    public class CreateRecord
    {
        public string? Title { get; set; }
        public string? Container { get; set; }
        public string? DateCreated { get; set; }
        public string? AttachDocumentpath { get; set; }
    }
    public class FileHandaler
    {
        public string FileName { get; set; }
        public byte[] File { get; set; }
        public string? LocalDownloadPath { get; set; }
    }
    public class ContainerRecordsInfo
    {
        public string ContainerName { get; set; }
        public List<string> ChildTitles { get; set; } = new List<string>();
        public int Count => ChildTitles.Count;
    }
}
