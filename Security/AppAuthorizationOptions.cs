namespace ETD.Api.Security
{
    public class AppAuthorizationOptions
    {
        public bool RequireApiKey { get; set; } = true;
        public bool RequireActivation { get; set; } = true;
        public bool BypassInDevelopment { get; set; } = true;
        public int TokenLifetimeHours { get; set; } = 720;
        public List<string> ApiKeys { get; set; } = new();
        public List<string> ActivationKeys { get; set; } = new();
        public List<string> BypassMachineNames { get; set; } = new();
    }
}
