using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BomPriceApproval.API.Infrastructure.Validation;

public static class Validation
{
    /// <summary>
    /// Start building a 400 ValidationProblemDetails with the given human-readable summary.
    /// </summary>
    public static ValidationProblemBuilder Detail(string detail) => new(detail);
}

public sealed class ValidationProblemBuilder
{
    private readonly string _detail;
    private readonly ModelStateDictionary _errors = new();

    internal ValidationProblemBuilder(string detail)
    {
        _detail = detail;
    }

    /// <summary>
    /// Add a field-level error. Field keys use bracket notation for arrays
    /// (e.g. "Items[0].ExpectedQty"). Call once per offending field.
    /// </summary>
    public ValidationProblemBuilder Field(string field, string message)
    {
        _errors.AddModelError(field, message);
        return this;
    }

    /// <summary>
    /// Build the 400 ActionResult with Content-Type application/problem+json.
    /// </summary>
    public ActionResult Return()
    {
        var problem = new ValidationProblemDetails(_errors)
        {
            Detail = _detail,
            Status = StatusCodes.Status400BadRequest,
        };
        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" },
        };
    }
}
