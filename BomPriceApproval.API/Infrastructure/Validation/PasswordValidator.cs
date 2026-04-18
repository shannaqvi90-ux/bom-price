namespace BomPriceApproval.API.Infrastructure.Validation;

public static class PasswordValidator
{
    public const int MinLength = 8;

    public static string? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return "Password is required.";
        if (password.Length < MinLength)
            return $"Password must be at least {MinLength} characters.";
        if (!password.Any(char.IsUpper))
            return "Password must contain at least one uppercase letter.";
        if (!password.Any(char.IsLower))
            return "Password must contain at least one lowercase letter.";
        if (!password.Any(char.IsDigit))
            return "Password must contain at least one digit.";
        if (password.All(char.IsLetterOrDigit))
            return "Password must contain at least one special character.";
        return null;
    }
}
