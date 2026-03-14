using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace ETD.Api.Security
{
    public class ActivationTokenService
    {
        private readonly IDataProtector _protector;

        private sealed class ActivationPayload
        {
            public DateTimeOffset ExpiresAtUtc { get; set; }
        }

        public ActivationTokenService(IDataProtectionProvider dataProtectionProvider)
        {
            _protector = dataProtectionProvider.CreateProtector("ETDP.AppActivationToken.v1");
        }

        public string CreateToken(DateTimeOffset expiresAtUtc)
        {
            var payload = new ActivationPayload { ExpiresAtUtc = expiresAtUtc };
            var json = JsonSerializer.Serialize(payload);
            return _protector.Protect(json);
        }

        public bool TryValidate(string? token, out DateTimeOffset expiresAtUtc)
        {
            expiresAtUtc = DateTimeOffset.MinValue;
            if (string.IsNullOrWhiteSpace(token)) return false;
            try
            {
                var json = _protector.Unprotect(token);
                var payload = JsonSerializer.Deserialize<ActivationPayload>(json);
                if (payload == null) return false;
                expiresAtUtc = payload.ExpiresAtUtc;
                return expiresAtUtc > DateTimeOffset.UtcNow;
            }
            catch
            {
                return false;
            }
        }
    }
}
