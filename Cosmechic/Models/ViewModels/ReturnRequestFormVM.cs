namespace Cosmechic.Models.ViewModels
{
    public class ReturnRequestFormVM
    {
        public int OrderId { get; set; }
        public List<ReturnableLineVM> Lines { get; set; } = new();
    }

    public class ReturnableLineVM
    {
        public int OrderDetailId { get; set; }
        public string ProduitNom { get; set; } = string.Empty;
        public int Purchased { get; set; }
        public int MaxReturnable { get; set; }
    }
}
