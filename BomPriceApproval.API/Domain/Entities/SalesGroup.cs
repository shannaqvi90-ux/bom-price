namespace BomPriceApproval.API.Domain.Entities;

public class SalesGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public ICollection<User> Members { get; set; } = [];
}
