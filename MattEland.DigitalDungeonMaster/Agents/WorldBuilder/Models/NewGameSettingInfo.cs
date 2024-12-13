namespace MattEland.DigitalDungeonMaster.Agents.WorldBuilder.Models;

public class NewGameSettingInfo
{
    public string PlayerCharacterName { get; set; } = string.Empty;
    public string PlayerDescription { get; set; } = string.Empty;
    public string PlayerCharacterClass { get; set; } = string.Empty;
    public string GameSettingDescription { get; set; } = string.Empty;
    public string CampaignName { get; set; } = string.Empty;
    public string CampaignObjective { get; set; } = string.Empty;
    public string FirstSessionObjective { get; set; } = string.Empty;
    public string DesiredGameplayStyle { get; set; } = string.Empty;
}