using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace ETD.Api.Security
{
    public class ActivationTokenService
    {
        private readonly IDataProtector _protector;

        public sealed class ActivationTokenInfo
        {
            public DateTimeOffset ExpiresAtUtc { get; set; }
            public string LecturerEmail { get; set; } = string.Empty;
        }

        private sealed class ActivationPayload
        {
            public DateTimeOffset ExpiresAtUtc { get; set; }
            public string LecturerEmail { get; set; } = string.Empty;
        }

        public ActivationTokenService(IDataProtectionProvider dataProtectionProvider)
        {
            _protector = dataProtectionProvider.CreateProtector("ETDP.AppActivationToken.v1");
        }

        public string CreateToken(DateTimeOffset expiresAtUtc, string? lecturerEmail = null)
        {
            var payload = new ActivationPayload
            {
                ExpiresAtUtc = expiresAtUtc,
                LecturerEmail = string.IsNullOrWhiteSpace(lecturerEmail) ? string.Empty : lecturerEmail.Trim()
            };
            var json = JsonSerializer.Serialize(payload);
            return _protector.Protect(json);
        }

        public bool TryValidateInfo(string? token, out ActivationTokenInfo info)
        {
            info = new ActivationTokenInfo();
            if (string.IsNullOrWhiteSpace(token)) return false;
            try
            {
                var json = _protector.Unprotect(token);
                var payload = JsonSerializer.Deserialize<ActivationPayload>(json);
                if (payload == null) return false;

                info = new ActivationTokenInfo
                {
                    ExpiresAtUtc = payload.ExpiresAtUtc,
                    LecturerEmail = payload.LecturerEmail ?? string.Empty
                };
                return info.ExpiresAtUtc > DateTimeOffset.UtcNow;
            }
            catch
            {
                return false;
            }
        }

        public bool TryValidate(string? token, out DateTimeOffset expiresAtUtc)
        {
            expiresAtUtc = DateTimeOffset.MinValue;
            var valid = TryValidateInfo(token, out var info);
            if (valid)
            {
                expiresAtUtc = info.ExpiresAtUtc;
            }
            return valid;
        }
    }
}
