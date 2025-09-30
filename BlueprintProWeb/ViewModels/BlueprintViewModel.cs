using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace BlueprintProWeb.ViewModels
{
    public class BlueprintViewModel
    {
        public int blueprintId { get; set; }
        public string architectId { get; set; }
        public IFormFile? BlueprintImage { get; set; }

        [MaxLength(60)]
        public string blueprintName { get; set; } = "";
        public int blueprintPrice { get; set; }
        public string blueprintDescription { get; set; } = "";
        public string blueprintStyle { get; set; } = "";
        public Boolean blueprintIsForSale { get; set; }
        public string clentId { get; set; }
        public List<SelectListItem> Clients { get; set; } = new();
    }
}
