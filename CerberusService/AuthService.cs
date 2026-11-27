using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RoomAgent
{
    public sealed class AuthService
    {
        private const int SaltSizeBytes = 16;
        private const int HashSizeBytes = 32;
        private const int SecretSizeBytes = 32;
        private const int Pbkdf2Iterations = 100000;
        private const string TokenVersion = "v1";
        private const int MaxFailedAttempts = 5;
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromSeconds(30);

        private sealed class FailureState
        {
            public int Count;
            public DateTime LockedUntilUtc;
        }

        private readonly ConcurrentDictionary<string, FailureState> _failures =
            new ConcurrentDictionary<string, FailureState>(StringComparer.OrdinalIgnoreCase);

        public static bool IsAuthEnabled(AgentConfig config)
        {
            return config != null &&
                   !string.IsNullOrWhiteSpace(config.AuthPasswordHash) &&
                   !string.IsNullOrWhiteSpace(config.AuthPasswordSalt);
        }

        public static void SetPassword(AgentConfig config, string password)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password cannot be empty.", "password");

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                Pbkdf2Iterations,
                HashAlgorithmName.SHA256,
                HashSizeBytes);

            config.AuthPasswordSalt = Convert.ToBase64String(salt);
            config.AuthPasswordHash = Convert.ToBase64String(hash);
            config.AuthTokenSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretSizeBytes));
        }

        public static void ClearPassword(AgentConfig config)
        {
            if (config == null) throw new ArgumentNullException("config");
            config.AuthPasswordSalt = "";
            config.AuthPasswordHash = "";
            config.AuthTokenSecret = "";
        }

        public static void RotateTokenSecret(AgentConfig config)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (!IsAuthEnabled(config))
            {
                config.AuthTokenSecret = "";
                return;
            }
            config.AuthTokenSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretSizeBytes));
        }

        public static bool VerifyPassword(AgentConfig config, string password)
        {
            if (!IsAuthEnabled(config)) return true;
            if (string.IsNullOrEmpty(password)) return false;

            try
            {
                byte[] salt = Convert.FromBase64String(config.AuthPasswordSalt);
                byte[] expectedHash = Convert.FromBase64String(config.AuthPasswordHash);
                byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(password),
                    salt,
                    Pbkdf2Iterations,
                    HashAlgorithmName.SHA256,
                    expectedHash.Length);
                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static string IssueToken(AgentConfig config)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (string.IsNullOrWhiteSpace(config.AuthTokenSecret))
            {
                config.AuthTokenSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretSizeBytes));
            }

            byte[] secret = Convert.FromBase64String(config.AuthTokenSecret);
            long issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string jti = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            string payloadJson = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                { "iat", issuedAt },
                { "jti", jti }
            });
            string payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
            string signingInput = TokenVersion + "." + payloadB64;

            using (HMACSHA256 hmac = new HMACSHA256(secret))
            {
                byte[] sig = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
                return signingInput + "." + Base64UrlEncode(sig);
            }
        }

        public static bool VerifyToken(AgentConfig config, string token)
        {
            if (!IsAuthEnabled(config)) return true;
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(config.AuthTokenSecret))
            {
                return false;
            }

            string[] parts = token.Trim().Split('.');
            if (parts.Length != 3 || !string.Equals(parts[0], TokenVersion, StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                byte[] secret = Convert.FromBase64String(config.AuthTokenSecret);
                byte[] actualSig = Base64UrlDecode(parts[2]);
                string signingInput = parts[0] + "." + parts[1];

                using (HMACSHA256 hmac = new HMACSHA256(secret))
                {
                    byte[] expectedSig = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
                    return actualSig.Length == expectedSig.Length &&
                           CryptographicOperations.FixedTimeEquals(actualSig, expectedSig);
                }
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public bool IsLoginAllowed(string clientKey, out int retryAfterSeconds)
        {
            retryAfterSeconds = 0;
            if (string.IsNullOrEmpty(clientKey)) return true;

            FailureState state;
            if (!_failures.TryGetValue(clientKey, out state)) return true;

            lock (state)
            {
                if (state.LockedUntilUtc > DateTime.UtcNow)
                {
                    retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((state.LockedUntilUtc - DateTime.UtcNow).TotalSeconds));
                    return false;
                }
                if (state.LockedUntilUtc != DateTime.MinValue)
                {
                    state.Count = 0;
                    state.LockedUntilUtc = DateTime.MinValue;
                }
                return true;
            }
        }

        public void RecordLoginFailure(string clientKey)
        {
            if (string.IsNullOrEmpty(clientKey)) return;
            FailureState state = _failures.GetOrAdd(clientKey, _ => new FailureState());
            lock (state)
            {
                state.Count++;
                if (state.Count >= MaxFailedAttempts)
                {
                    state.LockedUntilUtc = DateTime.UtcNow.Add(LockoutDuration);
                }
            }
        }

        public void ResetLoginFailures(string clientKey)
        {
            if (string.IsNullOrEmpty(clientKey)) return;
            FailureState ignored;
            _failures.TryRemove(clientKey, out ignored);
        }

        private static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static byte[] Base64UrlDecode(string input)
        {
            string padded = input.Replace('-', '+').Replace('_', '/');
            int mod = padded.Length % 4;
            if (mod == 2) padded += "==";
            else if (mod == 3) padded += "=";
            else if (mod == 1) throw new FormatException("Invalid base64url length.");
            return Convert.FromBase64String(padded);
        }
    }
}
