namespace MSOSync.Metadata.Configuration;

public static class ConfigurationAuditConstants
{
    public const string TemplateCreated      = "CONFIG_TEMPLATE_CREATED";
    public const string TemplateDraftUpdated = "CONFIG_TEMPLATE_DRAFT_UPDATED";
    public const string TemplatePublished    = "CONFIG_TEMPLATE_PUBLISHED";
    public const string TemplateArchived     = "CONFIG_TEMPLATE_ARCHIVED";
    public const string TemplateCloned       = "CONFIG_TEMPLATE_CLONED";

    public const string Assigned        = "CONFIG_ASSIGNED";
    public const string Unassigned      = "CONFIG_UNASSIGNED";
    public const string RolledBack      = "CONFIG_ROLLED_BACK";
    public const string OverrideSet     = "CONFIG_OVERRIDE_SET";
    public const string OverrideRemoved = "CONFIG_OVERRIDE_REMOVED";
    public const string RolloutStarted  = "CONFIG_ROLLOUT_STARTED";
}
