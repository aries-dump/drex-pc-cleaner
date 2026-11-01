using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Oauth2.v2;
using Google.Apis.Oauth2.v2.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace Drex;

public sealed class AuthResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = "";
    public UserProfile? Profile { get; set; }
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
}

public sealed class GoogleAuthConfig
{
    public static string ClientId => GetRequiredEnv("GOOGLE_CLIENT_ID");
    public static string ClientSecret => GetRequiredEnv("GOOGLE_CLIENT_SECRET");
    public static readonly string[] Scopes = { "openid", "email", "profile" };
    public static readonly string RedirectUri = "http://localhost:54321/callback/";

    private static string GetRequiredEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Required environment variable '{name}' is not set. " +
                $"Please set the {name} environment variable before running the application. " +
                $"For Google OAuth, obtain credentials from https://console.cloud.google.com/apis/credentials");
        }
        return value;
    }
}

public static class AuthService
{
    static readonly GoogleAuthorizationCodeFlow Flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = new ClientSecrets
        {
            ClientId = GoogleAuthConfig.ClientId,
            ClientSecret = GoogleAuthConfig.ClientSecret
        },
        Scopes = GoogleAuthConfig.Scopes,
        DataStore = new FileDataStore("DrexCleaner.Auth", true)
    });

    public static string GetGoogleAuthUrl()
    {
        var request = Flow.CreateAuthorizationCodeRequest(GoogleAuthConfig.RedirectUri);
        request.State = Guid.NewGuid().ToString("N");
        return request.Build().ToString();
    }

    public static async Task<AuthResult> HandleGoogleCallbackAsync(string authorizationCode)
    {
        try
        {
            var tokenResponse = await Flow.ExchangeCodeForTokenAsync("user", authorizationCode, GoogleAuthConfig.RedirectUri, CancellationToken.None);

            if (tokenResponse.Error != null)
                return new AuthResult { Success = false, ErrorMessage = tokenResponse.ErrorDescription ?? "OAuth token exchange failed" };

            // Get user info from Google
            var credential = new UserCredential(Flow, "user", tokenResponse);
            var oauth2Service = new Oauth2Service(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Drex Cleaner"
            });

            var userInfo = await oauth2Service.Userinfo.Get().ExecuteAsync();

            if (string.IsNullOrEmpty(userInfo.Email))
                return new AuthResult { Success = false, ErrorMessage = "Failed to retrieve email from Google" };

            // Find or create profile
            var existingProfile = UserProfileStore.Load();
            UserProfile profile;

            if (existingProfile != null && existingProfile.Provider == "google" && existingProfile.ProviderId == userInfo.Id)
            {
                // Existing Google user
                profile = existingProfile;
                profile.Email = userInfo.Email;
                profile.DisplayName = userInfo.Name ?? userInfo.Email;
                profile.PictureUrl = userInfo.Picture ?? "";
                profile.LastLoginAt = DateTime.UtcNow;
                profile.IsVerified = userInfo.VerifiedEmail ?? false;
            }
            else if (existingProfile != null && existingProfile.Provider == "manual" && existingProfile.Email.Equals(userInfo.Email, StringComparison.OrdinalIgnoreCase))
            {
                // Link Google to existing manual account
                profile = existingProfile;
                profile.Provider = "google";
                profile.ProviderId = userInfo.Id;
                profile.DisplayName = userInfo.Name ?? userInfo.Email;
                profile.PictureUrl = userInfo.Picture ?? "";
                profile.LastLoginAt = DateTime.UtcNow;
                profile.IsVerified = userInfo.VerifiedEmail ?? false;
            }
            else
            {
                // New Google user
                profile = new UserProfile
                {
                    Email = userInfo.Email,
                    DisplayName = userInfo.Name ?? userInfo.Email,
                    PictureUrl = userInfo.Picture ?? "",
                    Provider = "google",
                    ProviderId = userInfo.Id,
                    IsVerified = userInfo.VerifiedEmail ?? false,
                    LastLoginAt = DateTime.UtcNow
                };
            }

            UserProfileStore.Save(profile);
            UserProfileStore.AddLoginHistory(new LoginHistoryEntry
            {
                Success = true,
                UserAgent = "Google OAuth",
                Timestamp = DateTime.UtcNow
            });

            var session = new ActiveSession
            {
                DeviceInfo = Environment.MachineName,
                IpAddress = GetLocalIpAddress()
            };
            UserProfileStore.AddActiveSession(session);

            return new AuthResult
            {
                Success = true,
                Profile = profile,
                AccessToken = tokenResponse.AccessToken ?? "",
                RefreshToken = tokenResponse.RefreshToken ?? ""
            };
        }
        catch (Exception ex)
        {
            return new AuthResult { Success = false, ErrorMessage = $"Google authentication failed: {ex.Message}" };
        }
    }

    public static async Task<AuthResult> LoginWithEmailPasswordAsync(string email, string password)
    {
        try
        {
            var profile = UserProfileStore.Load();

            if (profile == null || profile.Provider != "manual")
                return new AuthResult { Success = false, ErrorMessage = "Account not found. Please register first." };

            if (!VerifyPassword(password, profile.PasswordHash, profile.PasswordSalt))
                return new AuthResult { Success = false, ErrorMessage = "Invalid email or password" };

            if (!profile.IsVerified)
                return new AuthResult { Success = false, ErrorMessage = "Please verify your email address first" };

            profile.LastLoginAt = DateTime.UtcNow;
            UserProfileStore.Save(profile);
            UserProfileStore.AddLoginHistory(new LoginHistoryEntry { Success = true, UserAgent = "Manual Login" });

            var session = new ActiveSession
            {
                DeviceInfo = Environment.MachineName,
                IpAddress = GetLocalIpAddress()
            };
            UserProfileStore.AddActiveSession(session);

            return new AuthResult { Success = true, Profile = profile };
        }
        catch (Exception ex)
        {
            return new AuthResult { Success = false, ErrorMessage = $"Login failed: {ex.Message}" };
        }
    }

    public static async Task<AuthResult> RegisterWithEmailPasswordAsync(string email, string password, string confirmPassword)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(email))
                return new AuthResult { Success = false, ErrorMessage = "Email is required" };

            if (!email.Contains("@") || !email.Contains("."))
                return new AuthResult { Success = false, ErrorMessage = "Invalid email format" };

            if (string.IsNullOrWhiteSpace(password))
                return new AuthResult { Success = false, ErrorMessage = "Password is required" };

            if (password.Length < 8)
                return new AuthResult { Success = false, ErrorMessage = "Password must be at least 8 characters" };

            if (password != confirmPassword)
                return new AuthResult { Success = false, ErrorMessage = "Passwords do not match" };

            var existingProfile = UserProfileStore.Load();

            if (existingProfile != null && existingProfile.Email.Equals(email, StringComparison.OrdinalIgnoreCase))
                return new AuthResult { Success = false, ErrorMessage = "An account with this email already exists" };

            var (hash, salt) = HashPassword(password);

            var profile = new UserProfile
            {
                Email = email.ToLowerInvariant(),
                DisplayName = email.Split('@')[0],
                Provider = "manual",
                ProviderId = email.ToLowerInvariant(),
                PasswordHash = hash,
                PasswordSalt = salt,
                IsVerified = false, // Would need email verification in production
                LastLoginAt = DateTime.UtcNow
            };

            UserProfileStore.Save(profile);
            UserProfileStore.AddLoginHistory(new LoginHistoryEntry { Success = true, UserAgent = "Manual Registration" });

            var session = new ActiveSession
            {
                DeviceInfo = Environment.MachineName,
                IpAddress = GetLocalIpAddress()
            };
            UserProfileStore.AddActiveSession(session);

            return new AuthResult { Success = true, Profile = profile };
        }
        catch (Exception ex)
        {
            return new AuthResult { Success = false, ErrorMessage = $"Registration failed: {ex.Message}" };
        }
    }

    public static async Task<bool> ChangePasswordAsync(string currentPassword, string newPassword, string confirmPassword)
    {
        try
        {
            var profile = UserProfileStore.Load();
            if (profile == null || profile.Provider != "manual")
                return false;

            if (!VerifyPassword(currentPassword, profile.PasswordHash, profile.PasswordSalt))
                return false;

            if (newPassword != confirmPassword)
                return false;

            if (newPassword.Length < 8)
                return false;

            var (hash, salt) = HashPassword(newPassword);
            profile.PasswordHash = hash;
            profile.PasswordSalt = salt;
            UserProfileStore.Save(profile);

            return true;
        }
        catch { return false; }
    }

    public static async Task<AuthResult> RequestPasswordResetAsync(string email)
    {
        // In production, this would send an email with a reset token
        // For now, we'll just return success
        await Task.Delay(100);
        return new AuthResult { Success = true, ErrorMessage = "If an account exists, a password reset link has been sent." };
    }

    static (string hash, string salt) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    static bool VerifyPassword(string password, string hash, string salt)
    {
        try
        {
            var saltBytes = Convert.FromBase64String(salt);
            var hashBytes = Convert.FromBase64String(hash);
            var computedHash = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, 100_000, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(hashBytes, computedHash);
        }
        catch { return false; }
    }

    static string GetLocalIpAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return ip.ToString();
        }
        catch { }
        return "127.0.0.1";
    }

    // ===== Remember Me / Auto-Login =====

    /// <summary>
    /// Generates a secure remember-me token for auto-login. Valid for 30 days.
    /// </summary>
    public static string GenerateRememberMeToken(UserProfile profile)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        profile.RememberMeToken = token;
        profile.RememberMeTokenExpiry = DateTime.UtcNow.AddDays(30);
        UserProfileStore.Save(profile);
        return token;
    }

    /// <summary>
    /// Validates a remember-me token and returns the profile if valid.
    /// </summary>
    public static UserProfile? ValidateRememberMeToken(string token)
    {
        var profile = UserProfileStore.Load();
        if (profile == null || string.IsNullOrEmpty(profile.RememberMeToken))
            return null;

        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(profile.RememberMeToken),
                System.Text.Encoding.UTF8.GetBytes(token)))
            return null;

        if (profile.RememberMeTokenExpiry < DateTime.UtcNow)
            return null;

        // Extend token expiry on successful validation (sliding window)
        profile.RememberMeTokenExpiry = DateTime.UtcNow.AddDays(30);
        UserProfileStore.Save(profile);
        return profile;
    }

    /// <summary>
    /// Checks if there's a valid remember-me token stored (for auto-login).
    /// Does not require a token parameter - checks the stored token directly.
    /// </summary>
    public static UserProfile? CheckRememberMeToken()
    {
        var profile = UserProfileStore.Load();
        if (profile == null || string.IsNullOrEmpty(profile.RememberMeToken))
            return null;

        if (profile.RememberMeTokenExpiry < DateTime.UtcNow)
            return null;

        // Extend token expiry on successful validation (sliding window)
        profile.RememberMeTokenExpiry = DateTime.UtcNow.AddDays(30);
        UserProfileStore.Save(profile);
        return profile;
    }

    /// <summary>
    /// Clears the remember-me token (on sign out).
    /// </summary>
    public static void ClearRememberMeToken()
    {
        var profile = UserProfileStore.Load();
        if (profile != null)
        {
            profile.RememberMeToken = "";
            profile.RememberMeTokenExpiry = DateTime.MinValue;
            UserProfileStore.Save(profile);
        }
    }

    // ===== Email Verification =====

    /// <summary>
    /// Generates an email verification token. Valid for 24 hours.
    /// </summary>
    public static string GenerateEmailVerificationToken(UserProfile profile)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        profile.EmailVerificationToken = token;
        profile.EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24);
        profile.EmailVerificationSent = true;
        UserProfileStore.Save(profile);
        return token;
    }

    /// <summary>
    /// Validates an email verification token.
    /// </summary>
    public static bool VerifyEmailToken(string token)
    {
        var profile = UserProfileStore.Load();
        if (profile == null || string.IsNullOrEmpty(profile.EmailVerificationToken))
            return false;

        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(profile.EmailVerificationToken),
                System.Text.Encoding.UTF8.GetBytes(token)))
            return false;

        if (profile.EmailVerificationTokenExpiry < DateTime.UtcNow)
            return false;

        // Mark as verified and clear token
        profile.IsVerified = true;
        profile.EmailVerificationToken = "";
        profile.EmailVerificationTokenExpiry = DateTime.MinValue;
        UserProfileStore.Save(profile);
        return true;
    }

    /// <summary>
    /// Sends a verification email (placeholder - in production would send actual email).
    /// </summary>
    public static async Task<AuthResult> SendVerificationEmailAsync(string email)
    {
        var profile = UserProfileStore.Load();
        if (profile == null || !profile.Email.Equals(email, StringComparison.OrdinalIgnoreCase))
            return new AuthResult { Success = false, ErrorMessage = "Account not found" };

        if (profile.IsVerified)
            return new AuthResult { Success = false, ErrorMessage = "Email already verified" };

        var token = GenerateEmailVerificationToken(profile);

        // In production: send email with verification link
        // For now, we'll simulate by returning the token in the message
        await Task.Delay(100);

        // In a real app, you would send an email with a link like:
        // https://yourapp.com/verify?token={token}
        // For now, we return the token so it can be tested
        return new AuthResult
        {
            Success = true,
            ErrorMessage = $"Verification email sent. Token: {token} (In production, this would be sent via email)"
        };
    }

    /// <summary>
    /// Resends the verification email.
    /// </summary>
    public static async Task<AuthResult> ResendVerificationEmailAsync(string email)
    {
        var profile = UserProfileStore.Load();
        if (profile == null || !profile.Email.Equals(email, StringComparison.OrdinalIgnoreCase))
            return new AuthResult { Success = false, ErrorMessage = "Account not found" };

        if (profile.IsVerified)
            return new AuthResult { Success = false, ErrorMessage = "Email already verified" };

        var token = GenerateEmailVerificationToken(profile);

        // In production: send email
        await Task.Delay(100);

        return new AuthResult
        {
            Success = true,
            ErrorMessage = $"Verification email resent. Token: {token} (In production, this would be sent via email)"
        };
    }

    /// <summary>
    /// Checks if a profile has a valid verification token that hasn't expired.
    /// </summary>
    public static bool HasValidVerificationToken(UserProfile profile)
    {
        return !string.IsNullOrEmpty(profile.EmailVerificationToken)
            && profile.EmailVerificationTokenExpiry > DateTime.UtcNow;
    }