namespace StayFlow.BuildingBlocks.Deployment;

public enum DeploymentMode
{
    SaaS,
    OnPremise
}

public class DeploymentOptions
{
    public const string SectionName = "Deployment";

    public DeploymentMode Mode { get; set; } = DeploymentMode.SaaS;
    public bool LicenseValidationEnabled { get; set; } = false;
    public string? LicenseFilePath { get; set; }
    public string? InstanceName { get; set; }
}
