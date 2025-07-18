using System.ComponentModel.DataAnnotations;

namespace mvctest.Models
{
    public class UserAccessLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string UserName { get; set; }

        [Required]
        [StringLength(100)]
        public string AppUniqueID { get; set; }

        [Required]
        [StringLength(45)]
        public string IPAddress { get; set; }

        [Required]
        [StringLength(100)]
        public string DataSetId { get; set; }

        [Required]
        [StringLength(255)]
        public string WorkGroupServer { get; set; }

        [Required]
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
}
