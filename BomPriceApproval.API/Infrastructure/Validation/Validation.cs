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
    private readonly Dictionary<string, object?> _extensions = new();
    private int _status = StatusCodes.Status400BadRequest;

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
    /// Attach an RFC 7807 extension member that will be serialised as a top-level
    /// JSON property on the response body (e.g. "lockoutSecondsRemaining").
    /// </summary>
    public ValidationProblemBuilder Extension(string key, object? value)
    {
        _extensions[key] = value;
        return this;
    }

    /// <summary>
    /// Override the response status code. Default is 400. Use 409 for Conflict
    /// (e.g. business-rule violations: "already exists", "in-use", etc.).
    /// </summary>
    public ValidationProblemBuilder Status(int statusCode)
    {
        _status = statusCode;
        return this;
    }

    /// <summary>
    /// Build the ActionResult with Content-Type application/problem+json.
    /// </summary>
    public ActionResult Return()
    {
        var problem = new ValidationProblemDetails(_errors)
        {
            Detail = _detail,
            Status = _status,
        };
        foreach (var (k, v) in _extensions)
            problem.Extensions[k] = v;
        return new ObjectResult(problem)
        {
            StatusCode = _status,
            ContentTypes = { "application/problem+json" },
        };
    }
}
