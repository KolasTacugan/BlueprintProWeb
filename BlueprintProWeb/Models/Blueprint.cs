using System.ComponentModel.DataAnnotations;

namespace BlueprintProWeb.Models
{
    public class Blueprint
    {
        public int blueprintId { get; set; }
        public string blueprintImage { get; set; }
        [MaxLength(60)]
        public string blueprintName { get; set; } = "";
        public int blueprintPrice { get; set; }
        public string blueprintDescription { get; set; } = "";
        public string blueprintStyle { get; set; } = "";
        public Boolean blueprintIsForSale { get; set; } 
    }
}
