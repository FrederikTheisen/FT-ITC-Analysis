using System.Net.Mail;

namespace AnalysisITC.Web;

/// <summary>Shared, side-effect-free validation for public registration requests.</summary>
public static class RegistrationRequestValidator
{
    public static bool TryValidate(RegistrationRequest value, RegistrationOptions options, out string reason)
    {
        var name = value.Name?.Trim();
        var email = value.Email?.Trim();
        var organization = value.Organisation?.Trim();

        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || TerminalText.ContainsUnsafe(name))
        {
            reason = "name is missing or invalid";
            return false;
        }

        if (string.IsNullOrWhiteSpace(email) || email.Length > 254)
        {
            reason = "email is missing or invalid";
            return false;
        }

        if (organization?.Length > 200 || organization is not null && TerminalText.ContainsUnsafe(organization))
        {
            reason = "organisation is invalid";
            return false;
        }

        if (!value.AcceptedTerms || !value.AcknowledgedPrivacy)
        {
            reason = "required consent was not supplied";
            return false;
        }

        if (!string.Equals(value.TermsVersion, options.TermsVersion, StringComparison.Ordinal)
            || !string.Equals(value.PrivacyVersion, options.PrivacyVersion, StringComparison.Ordinal))
        {
            reason = "terms or privacy version is not current";
            return false;
        }

        try { _ = new MailAddress(email); }
        catch (FormatException)
        {
            reason = "email is invalid";
            return false;
        }

        reason = "request fields and consent are valid";
        return true;
    }
}
