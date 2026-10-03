using System.Globalization;
using HusqaCockpit.Core.Api;
using Windows.Security.Credentials;

namespace HusqaCockpit.App.Services;

/// <summary>
/// Stores the API key/secret and the current access token in the Windows Credential Manager
/// (Control Panel › Credential Manager › Web Credentials), never in plain files.
/// </summary>
public sealed class CredentialStore : ITokenCache
{
    private const string CredentialsResource = "HusqA Cockpit – API";
    private const string TokenResource = "HusqA Cockpit – Access token";

    private readonly PasswordVault _vault = new();

    public ApiCredentials? LoadCredentials()
    {
        var credential = Find(CredentialsResource);
        if (credential is null)
        {
            return null;
        }
        credential.RetrievePassword();
        return new ApiCredentials(credential.UserName, credential.Password);
    }

    public void SaveCredentials(ApiCredentials credentials)
    {
        RemoveAll(CredentialsResource);
        RemoveAll(TokenResource);
        _vault.Add(new PasswordCredential(CredentialsResource, credentials.ApplicationKey, credentials.ApplicationSecret));
    }

    public void DeleteCredentials()
    {
        RemoveAll(CredentialsResource);
        RemoveAll(TokenResource);
    }

    AccessToken? ITokenCache.Load()
    {
        var credential = Find(TokenResource);
        if (credential is null)
        {
            return null;
        }
        credential.RetrievePassword();

        // Stored as "<expiry unix seconds>|<token>".
        var separator = credential.Password.IndexOf('|', StringComparison.Ordinal);
        if (separator <= 0 || !long.TryParse(credential.Password.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var expiry))
        {
            return null;
        }
        return new AccessToken(credential.UserName, credential.Password[(separator + 1)..], DateTimeOffset.FromUnixTimeSeconds(expiry));
    }

    void ITokenCache.Save(AccessToken token)
    {
        RemoveAll(TokenResource);
        var value = string.Create(CultureInfo.InvariantCulture, $"{token.ExpiresAt.ToUnixTimeSeconds()}|{token.Value}");
        _vault.Add(new PasswordCredential(TokenResource, token.ApplicationKey, value));
    }

    void ITokenCache.Clear() => RemoveAll(TokenResource);

    private PasswordCredential? Find(string resource)
    {
        try
        {
            return _vault.FindAllByResource(resource).FirstOrDefault();
        }
        catch (Exception)
        {
            // FindAllByResource throws when nothing is stored.
            return null;
        }
    }

    private void RemoveAll(string resource)
    {
        try
        {
            foreach (var credential in _vault.FindAllByResource(resource))
            {
                _vault.Remove(credential);
            }
        }
        catch (Exception)
        {
            // Nothing stored.
        }
    }
}
