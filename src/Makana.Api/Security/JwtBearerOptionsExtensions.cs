using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Makana.Api.Security;

internal static class JwtBearerOptionsExtensions
{
    public static void UseSignalRQueryStringAccessToken(this JwtBearerOptions options, PathString hubPath)
    {
        var existingOnMessageReceived = options.Events.OnMessageReceived;
        options.Events.OnMessageReceived = async context =>
        {
            if (existingOnMessageReceived is not null)
            {
                await existingOnMessageReceived(context);
            }

            if (!string.IsNullOrWhiteSpace(context.Token) ||
                !context.Request.Path.StartsWithSegments(hubPath))
            {
                return;
            }

            var accessToken = context.Request.Query["access_token"].ToString();
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                context.Token = accessToken;
            }
        };
    }

    public static void UseFormSubjectToken(this JwtBearerOptions options, PathString tokenPath)
    {
        var existingOnMessageReceived = options.Events.OnMessageReceived;
        options.Events.OnMessageReceived = async context =>
        {
            if (existingOnMessageReceived is not null)
            {
                await existingOnMessageReceived(context);
            }

            if (!string.IsNullOrWhiteSpace(context.Token) ||
                !context.Request.Path.Equals(tokenPath, StringComparison.OrdinalIgnoreCase) ||
                !context.Request.HasFormContentType)
            {
                return;
            }

            var form = await context.Request.ReadFormAsync(context.HttpContext.RequestAborted);
            var subjectToken = form["subject_token"].ToString();
            if (!string.IsNullOrWhiteSpace(subjectToken))
            {
                context.Token = subjectToken;
            }
        };
    }
}
