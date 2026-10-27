using System.Net;
using System.Text;

namespace AnalysisITC.Web;

public sealed partial class DailyStatusEmail
{
    static readonly string[] ReportSections = ["Service", "Health (web service)", "Local readiness (web service)", "External access (Cloudflare to MIST)", "Server storage", "Registration pipeline",
        "Registration account overview", "Previous-day usage", "User activity", "Interpretation diagnostic"];

    internal static string RenderHtml(string report) => RenderHtml(new DailyStatusReport(report,
        report.Split('\n').Any(line => line.Trim() == "Overall status: ATTENTION")));

    internal static string RenderHtml(DailyStatusReport report)
    {
        var lines = report.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var sections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        List<string>? current = null;
        foreach (var line in lines.Skip(1))
        {
            if (ReportSections.Contains(line, StringComparer.Ordinal))
                sections[line] = current = [];
            else if (!string.IsNullOrWhiteSpace(line) && current is not null)
                current.Add(line.Trim());
        }

        var html = new StringBuilder("""
            <!DOCTYPE html>
            <html lang="en"><head><meta charset="utf-8"/><meta name="viewport" content="width=device-width, initial-scale=1"/>
            <title>FT-ITC daily status</title>
            <style>@media only screen and (max-width:560px){.outer{padding:8px!important}.content{padding:20px 12px!important}.metric-value{font-size:22px!important}.account{padding:16px 12px!important}}</style>
            </head><body style="margin:0;padding:0;background:#edf1f5;color:#172b3a;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:1.5">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;background:#edf1f5"><tr><td class="outer" align="center" style="padding:28px 16px">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:760px;border-collapse:collapse;background:#ffffff;border:1px solid #d8e1e8">
            <tr><td class="content" style="padding:28px 32px;background:#142f43;color:#ffffff">
            <p style="margin:0 0 10px;font-size:12px;font-weight:bold;letter-spacing:2px;color:#a8c8dd">FT-ITC / MIST</p>
            """);
        html.Append("<h1 style=\"margin:0;font-size:25px;line-height:1.3\">").Append(E(lines[0])).Append("</h1>");
        var period = lines.FirstOrDefault(line => line.StartsWith("Period: ", StringComparison.Ordinal));
        if (period is not null)
            html.Append("<p style=\"margin:12px 0 0;font-size:12px;color:#d3e2ed\">").Append(E(period))
                .Append("<br/>Europe/Copenhagen</p>");
        html.Append("</td></tr><tr><td class=\"content\" style=\"padding:28px 32px\">");
        html.Append("<p style=\"margin:0 0 24px;padding:12px 16px;border-left:4px solid ")
            .Append(report.HasAttention ? "#b42318;background:#fff1f0" : "#217346;background:#edf7f0")
            .Append("\"><strong>Overall status: ").Append(report.HasAttention ? "ATTENTION" : "OK")
            .Append("</strong><br/><span style=\"font-size:13px\">")
            .Append(report.HasAttention ? "One or more checks need attention. See the operational details below." : "No checks reported a failure or warning. See check times and scope below.")
            .Append("</span></p>");

        if (sections.TryGetValue("Previous-day usage", out var usageLines))
        {
            Heading(html, "Daily overview");
            string? Value(string label) => usageLines.FirstOrDefault(line => line.StartsWith(label + ": ", StringComparison.Ordinal))?[(label.Length + 2)..];
            html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;table-layout:fixed;margin:0 0 16px\"><tr>");
            Metric(html, "Requests", Value("Requests") ?? "Unavailable");
            Metric(html, "Provider attempts", Value("Provider attempts") ?? "Unavailable");
            Metric(html, "Estimated cost", Value("Estimated cost") ?? (Value("Cost") is null ? "Unavailable" : "Unknown"));
            html.Append("</tr></table>");
            DetailTable(html, usageLines.Where(line => !line.StartsWith("Requests: ", StringComparison.Ordinal)
                && !line.StartsWith("Provider attempts: ", StringComparison.Ordinal) && !line.StartsWith("Estimated cost: ", StringComparison.Ordinal)));
        }

        Heading(html, "User activity");
        html.Append("<p style=\"margin:0 0 16px;color:#526779;font-size:13px\">Activity for the reported day, grouped by account or public installation. Automated diagnostics are excluded.</p>");
        if (report.Users is null)
            html.Append("<p style=\"padding:16px;background:#fff4e5\">User activity unavailable.</p>");
        else if (report.Users.Count == 0)
            html.Append("<p style=\"padding:16px;background:#f3f6f9\">No user activity.</p>");
        else
            foreach (var user in report.Users) UserCard(html, user);

        Heading(html, "Operational details");
        foreach (var section in sections.Where(section => section.Key is not ("Previous-day usage" or "User activity") && section.Value.Count > 0))
        {
            html.Append("<h3 style=\"margin:22px 0 8px;font-size:15px;color:#173e59\">").Append(E(section.Key)).Append("</h3>");
            DetailTable(html, section.Value);
        }
        html.Append("""
            </td></tr><tr><td class="content" style="padding:18px 32px;border-top:1px solid #d8e1e8;background:#f8fafc;font-size:12px;color:#526779">
            FT-ITC MIST · Daily operational report<br/>Costs are in USD. Request content and scientific data are not included.
            </td></tr></table></td></tr></table></body></html>
            """);
        return html.ToString();
    }

    static void Heading(StringBuilder html, string text) => html.Append("<h2 style=\"margin:26px 0 12px;font-size:19px;color:#142f43\">").Append(E(text)).Append("</h2>");

    static void Metric(StringBuilder html, string label, string value) => html
        .Append("<td valign=\"top\" style=\"padding:14px 10px;background:#f0f5f9;border:2px solid #ffffff;word-wrap:break-word\">")
        .Append("<div style=\"font-size:12px;color:#526779\">").Append(E(label)).Append(": </div>")
        .Append("<div class=\"metric-value\" style=\"font-size:26px;font-weight:bold;line-height:1.3;color:#173e59\">")
        .Append(E(value)).Append("</div></td>");

    static void UserCard(StringBuilder html, DailyUserActivity user)
    {
        html.Append("<table class=\"user-card\" role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;margin:0 0 20px;border:1px solid #cddbe6;table-layout:fixed\"><tr><td class=\"account\" style=\"padding:18px 20px;background:#f0f5f9;border-top:3px solid #356984;word-wrap:break-word;overflow-wrap:anywhere\">")
            .Append("<h3 style=\"margin:0 0 5px;font-size:19px;color:#142f43\">").Append(E(user.Name)).Append("</h3>")
            .Append("<p style=\"margin:0;color:#526779;font-size:12px\">Access tier: ").Append(E(user.Tier))
            .Append("<br/>Account ID: ").Append(E(user.Id ?? "Not recorded")).Append("</p>")
            .Append("</td></tr><tr><td class=\"account\" style=\"padding:16px 20px\">")
            .Append("<p style=\"margin:0 0 12px\"><strong>").Append(user.Requests).Append(user.Requests == 1 ? " request" : " requests")
            .Append("</strong> · ").Append(E(user.Cost.Description)).Append("</p>")
            .Append("<table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;table-layout:fixed;font-size:13px\"><thead><tr>")
            .Append("<th scope=\"col\" align=\"left\" style=\"padding:8px 4px;border-bottom:2px solid #d8e1e8\">Activity / model</th>")
            .Append("<th scope=\"col\" align=\"left\" width=\"90\" style=\"padding:8px 4px;border-bottom:2px solid #d8e1e8\">Outcome</th>")
            .Append("<th scope=\"col\" align=\"right\" width=\"46\" style=\"padding:8px 4px;border-bottom:2px solid #d8e1e8\">Count</th></tr></thead><tbody>");
        foreach (var action in user.Actions)
        {
            html.Append("<tr><td valign=\"top\" style=\"padding:10px 4px;border-bottom:1px solid #e5ebf0;word-wrap:break-word;overflow-wrap:anywhere\"><strong>")
                .Append(E(TaskLabel(action.Task))).Append("</strong><br/><span style=\"color:#526779;font-size:12px\">")
                .Append(E(action.Provider)).Append(" / ").Append(E(action.Model)).Append("</span></td>")
                .Append("<td valign=\"top\" style=\"padding:10px 4px;border-bottom:1px solid #e5ebf0\"><strong style=\"color:")
                .Append(action.Outcome == "success" ? "#217346" : action.Outcome is "provider_error" or "server_error" or "timeout" ? "#b42318" : "#9a6700")
                .Append("\">").Append(E(OutcomeLabel(action.Outcome))).Append("</strong></td>")
                .Append("<td valign=\"top\" align=\"right\" style=\"padding:10px 4px;border-bottom:1px solid #e5ebf0;font-weight:bold\">")
                .Append(action.Requests).Append("</td></tr>");
        }
        html.Append("</tbody></table><p style=\"margin:12px 0 6px;font-size:12px;color:#526779\">First request: ")
            .Append(E(user.FirstRequest)).Append("<br/>Last request: ").Append(E(user.LastRequest))
            .Append("</p><p style=\"margin:0;font-size:12px;color:#526779\">All time: ").Append(user.TotalRequests)
            .Append(" prompt(s); ").Append(E(user.TotalCost.Description)).Append("</p></td></tr></table>");
    }

    static void DetailTable(StringBuilder html, IEnumerable<string> lines)
    {
        html.Append("<table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;table-layout:fixed;font-size:13px\"><tbody>");
        foreach (var line in lines)
        {
            var separator = line.IndexOf(':');
            if (separator < 0)
                html.Append("<tr><td colspan=\"2\" style=\"padding:9px 10px;border-bottom:1px solid #e5ebf0;word-wrap:break-word\">").Append(StatusHtml(line)).Append("</td></tr>");
            else
                html.Append("<tr><th scope=\"row\" align=\"left\" width=\"36%\" valign=\"top\" style=\"padding:9px 10px;border-bottom:1px solid #e5ebf0;font-weight:normal;color:#526779;word-wrap:break-word\">")
                    .Append(E(line[..(separator + 1)])).Append(" </th><td valign=\"top\" style=\"padding:9px 10px;border-bottom:1px solid #e5ebf0;word-wrap:break-word;overflow-wrap:anywhere\">")
                    .Append(StatusHtml(line[(separator + 1)..].Trim())).Append("</td></tr>");
        }
        html.Append("</tbody></table>");
    }

    static string StatusHtml(string value)
    {
        // Only style explicit leading statuses, never words inside names or diagnostic explanations.
        foreach (var (token, color) in new[] { ("PASS", "#217346"), ("FAIL", "#b42318"), ("ATTENTION", "#b42318"),
            ("WARNING", "#9a6700"), ("SKIPPED", "#9a6700"), ("check unavailable", "#b42318"), ("ok", "#217346"), ("active", "#217346") })
            if (value == token || value.StartsWith(token + " —", StringComparison.Ordinal))
                return "<strong style=\"color:" + color + "\">" + E(token) + "</strong>" + E(value[token.Length..]);
        return E(value);
    }

    static string E(string value) => WebUtility.HtmlEncode(value);
}
