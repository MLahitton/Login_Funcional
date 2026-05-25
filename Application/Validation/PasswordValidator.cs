using System.Text.RegularExpressions;

namespace Application.Validation;

public static partial class PasswordValidator
{
    public static IReadOnlyList<string> GetErrors(string? password)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(password))
        {
            errors.Add("La contrasena es obligatoria.");
            return errors;
        }

        if (password.Length < 8)
        {
            errors.Add("La contrasena debe tener al menos 8 caracteres.");
        }

        if (!UpperRegex().IsMatch(password))
        {
            errors.Add("Debe incluir al menos una mayuscula.");
        }

        if (!LowerRegex().IsMatch(password))
        {
            errors.Add("Debe incluir al menos una minuscula.");
        }

        if (!DigitRegex().IsMatch(password))
        {
            errors.Add("Debe incluir al menos un numero.");
        }

        if (!SymbolRegex().IsMatch(password))
        {
            errors.Add("Debe incluir al menos un simbolo (ej: ! @ #).");
        }

        return errors;
    }

    public static bool IsValid(string? password) => GetErrors(password).Count == 0;

    [GeneratedRegex("[A-Z]")]
    private static partial Regex UpperRegex();

    [GeneratedRegex("[a-z]")]
    private static partial Regex LowerRegex();

    [GeneratedRegex("[0-9]")]
    private static partial Regex DigitRegex();

    [GeneratedRegex(@"[^a-zA-Z0-9]")]
    private static partial Regex SymbolRegex();
}
