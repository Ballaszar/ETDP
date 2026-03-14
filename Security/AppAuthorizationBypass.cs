using Microsoft.Extensions.Hosting;

namespace ETD.Api.Security
{
    public static class AppAuthorizationBypass
    {
        public static bool IsBypassed(IHostEnvironment env, AppAuthorizationOptions options)
        {
            if (options.BypassInDevelopment && env.IsDevelopment())
            {
                return true;
            }

            var machine = Environment.MachineName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(machine)) return false;

            return options.BypassMachineNames.Any(x =>
                string.Equals(x?.Trim(), machine, StringComparison.OrdinalIgnoreCase));
        }
    }
}
