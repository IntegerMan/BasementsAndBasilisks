namespace MattEland.DigitalDungeonMaster.WebAPI.Settings;

public record JwtSettings
{
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required string Secret { get; init; }
    public double TimeOutInHours { get; set; } = 6.0;
}
