namespace AS24Net.Server;

public static class AuthClaims
{
    /// <summary>Present while the signed in user has to change the password (e.g. the default admin account).</summary>
    public const string MustChangePassword = "as24net:must_change_password";

    /// <summary>Present when the user signed in with Microsoft Entra ID instead of a password.</summary>
    public const string SignedInWithEntra = "as24net:entra";
}
