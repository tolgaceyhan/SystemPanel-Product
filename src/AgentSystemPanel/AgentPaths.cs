namespace AgentSystemPanel;

/// <summary>Agent'ın makineye özel dosya konumları (program klasörü dışında; güncellemelerde korunur).</summary>
public static class AgentPaths
{
    public static string ConfigDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SystemPanel", "Agent");

    public static string MachineSettingsFile => Path.Combine(ConfigDirectory, "agentsettings.json");
}
