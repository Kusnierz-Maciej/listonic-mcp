namespace ListonicApiClient;

public class ListonicClientOptions
{
    public const string BaseUrl = "https://api.listonic.com/api";

    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
