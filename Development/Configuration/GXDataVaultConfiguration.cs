using System.Text.Json.Serialization;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Shared;

namespace Gurux.Data.Relay.Configuration;

public sealed class GXDataVaultConfiguration : GXModeConfiguration
{
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)]
    public List<GXColumnAlias> ColumnAliases { get; set; } = GXColumnAlias.Defaults();

    public override IEnumerable<System.ComponentModel.DataAnnotations.ValidationResult> Validate(System.ComponentModel.DataAnnotations.ValidationContext validationContext)
    {
        foreach (var error in base.Validate(validationContext)) yield return error;
        if (ColumnAliases == null || ColumnAliases.Any(c => c == null || !GXColumnAlias.IsValid(c)))
            yield return new("Column mappings require two names with at most one wildcard each.", [nameof(ColumnAliases)]);
    }

    public List<GXDatabaseConfiguration> Databases { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<GXDataVaultTableMapping>? Mappings { get; set; }

    public GXDataVaultConfiguration()
    {
        Mode = ApplicationMode.DataVault;
    }

    public IReadOnlyList<GXDatabaseConfiguration> GetDatabases()
    {
        if (Databases.Count == 0)
        {
            throw new InvalidOperationException("Data Vault configuration must contain at least one database in Databases.");
        }

        return Databases;
    }

    public IReadOnlyList<GXDataVaultTableMapping> GetMappings()
    {
        List<GXDataVaultTableMapping> mappings = Databases
            .SelectMany(database => database.Mappings ?? [])
            .ToList();
        if (Mappings is { Count: > 0 })
        {
            mappings.AddRange(Mappings);
        }

        return mappings;
    }
}

