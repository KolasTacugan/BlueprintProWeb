namespace BlueprintProWeb.ViewModels
{
    public class MobileEditBlueprintViewModel
    {
        public int blueprintId { get; set; }
        public string? blueprintName { get; set; }
        public int? blueprintPrice { get; set; }
        public string? blueprintStyle { get; set; }
        public string? blueprintDescription { get; set; }
        public IFormFile? BlueprintImage { get; set; }
    }
}
