using System;
using System.Text.RegularExpressions;
using UnityEngine;

[CreateAssetMenu(menuName = "Cat Quest/Account Connection", fileName = "AccountConnectionConfig")]
public sealed class AccountConnectionConfig : ScriptableObject
{
    public string firebaseProjectId;
    public string firebaseApiKey;
    public string googleDesktopClientId;
    [Tooltip("Optional desktop OAuth client secret only. Never a service-account or web-server secret.")]
    public string googleDesktopClientSecret;

    public bool IsConfigured => ConfigurationError == null;
    public string ConfigurationError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(firebaseProjectId) || string.IsNullOrWhiteSpace(firebaseApiKey) ||
                string.IsNullOrWhiteSpace(googleDesktopClientId))
                return "Google sign-in is not available yet. Local tasks and progress remain available.";
            if (!Regex.IsMatch(firebaseProjectId, "^[a-z][a-z0-9-]{4,28}[a-z0-9]$") ||
                !Regex.IsMatch(firebaseApiKey, "^[A-Za-z0-9_-]+$") ||
                !googleDesktopClientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal))
                return "Google sign-in is unavailable in this build. Local tasks and progress remain available.";
            return null;
        }
    }
}
