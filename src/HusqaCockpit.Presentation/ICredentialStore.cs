using HusqaCockpit.Core.Api;

namespace HusqaCockpit.Presentation;

/// <summary>Where the API key and secret are kept: the Windows Credential Manager in the app.</summary>
public interface ICredentialStore
{
    ApiCredentials? LoadCredentials();

    void SaveCredentials(ApiCredentials credentials);

    void DeleteCredentials();
}
