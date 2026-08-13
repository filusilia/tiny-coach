using Windows.Security.Credentials;

namespace AegisCoach.Native.Services;

public static class AppCredentialStore
{
    private const string ResourceName = "AegisCoach";

    public static string Load(string key)
    {
        try
        {
            var credential = new PasswordVault().Retrieve(ResourceName, key);
            credential.RetrievePassword();
            return credential.Password ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static bool Save(string key, string value, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            try
            {
                var emptyVault = new PasswordVault();
                emptyVault.Remove(emptyVault.Retrieve(ResourceName, key));
            }
            catch
            {
                // No secret needs to be stored; an unavailable or empty vault must not block basic setup.
            }

            error = null;
            return true;
        }

        try
        {
            var vault = new PasswordVault();
            try
            {
                vault.Remove(vault.Retrieve(ResourceName, key));
            }
            catch
            {
                // The credential does not exist yet.
            }

            vault.Add(new PasswordCredential(ResourceName, key, value));

            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }
}
