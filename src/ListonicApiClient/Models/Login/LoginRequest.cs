using Refit;

namespace ListonicApiClient.Models.Login;

public class LoginRequest
{
    [AliasAs("username")]
    public string Username { get; init; } = string.Empty;

    [AliasAs("password")]
    public string Password { get; init; } = string.Empty;

    [AliasAs("client_id")]
    public string ClientId { get; init; } = "listonicv2";

    [AliasAs("client_secret")]
    public string ClientSecret { get; init; } = "fjdfsoj9874jdfhjkh34jkhffdfff";

    [AliasAs("redirect_uri")]
    public string RedirectUri { get; init; } = "https://listonicv2api.jestemkucharzem.pl";
}
