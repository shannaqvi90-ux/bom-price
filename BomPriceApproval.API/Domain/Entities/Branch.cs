namespace BomPriceApproval.API.Domain.Entities;

public class Branch
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<User> Users { get; set; } = [];
    public ICollection<Customer> Customers { get; set; } = [];
    public ICollection<Item> Items { get; set; } = [];
    public ICollection<QuotationRequest> QuotationRequests { get; set; } = [];
}
