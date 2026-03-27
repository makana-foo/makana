using System.Text.Json;

namespace Makana.StackUpdates.Services;

public static class PulumiDisplayValueFormatter
{
    private const string PulumiSignatureKey = "4dabf18193072939515e22adb298388d";
    private const string SecretSignatureValue = "1b47061264138c4ac30d75fd1eb44270";
    public const string UnknownSignatureValue = "04da6b54-80e4-46f7-96ec-b56ff0331ba9";

    public static string NormalizeUnknownDisplayText(string value)
        => value.Replace(UnknownSignatureValue, "[unknown]", StringComparison.Ordinal);

    public static string? NormalizeUnknownDisplayValue(string? value)
        => string.Equals(value, UnknownSignatureValue, StringComparison.Ordinal)
            ? "[unknown]"
            : value;

    public static bool TryFormatSpecialValue(JsonElement value, out string formatted)
    {
        formatted = string.Empty;

        if (value.ValueKind == JsonValueKind.String &&
            string.Equals(value.GetString(), UnknownSignatureValue, StringComparison.Ordinal))
        {
            formatted = "[unknown]";
            return true;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!value.TryGetProperty(PulumiSignatureKey, out var signature))
        {
            return false;
        }

        var signatureValue = signature.GetString();
        if (string.Equals(signatureValue, SecretSignatureValue, StringComparison.Ordinal))
        {
            formatted = "[secret]";
            return true;
        }

        if (string.Equals(signatureValue, UnknownSignatureValue, StringComparison.Ordinal))
        {
            formatted = "[unknown]";
            return true;
        }

        return false;
    }
}
