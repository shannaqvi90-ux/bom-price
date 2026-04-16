namespace BomPriceApproval.Tests.Shared;

public record LoginResponse(string AccessToken, string RefreshToken);
public record ItemDto(int Id, string Code, string Description, string Type);
public record CustomerDto(int Id, string Name);
public record ErrorResponse(string Message);
public record CreatedRequisition(int Id, string RefNo);
