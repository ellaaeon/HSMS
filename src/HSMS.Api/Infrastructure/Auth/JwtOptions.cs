namespace HSMS.Api.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "HSMS";
    public string Audience { get; set; } = "HSMS.Clients";
    public string SigningKey { get; set; } = "CHANGE_THIS_TO_LONG_RANDOM_SECRET";
    public int ExpiryMinutes { get; set; } = 480;
}
