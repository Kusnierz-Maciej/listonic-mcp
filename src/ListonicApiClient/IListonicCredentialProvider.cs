namespace ListonicApiClient;

/// <summary>Supplies the Listonic credentials for the current request scope.</summary>
public interface IListonicCredentialProvider
{
    ListonicClientOptions GetCredentials();
}
