using Gurux.Data.Relay.Log;
using Gurux.Service.Orm.Common.Model;
using System.Diagnostics;
using Gurux.Data.Relay.Shared;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using Gurux.Data.Relay.Configuration;

namespace Gurux.Data.Relay.Web.Client.Services;

public sealed class GXAdminApiClient : IGXDataSourceAdminApi
{
    public async Task<GXDataSourceRequest> CreateDataSourceAsync(GXDataSourceRequest source, CancellationToken token)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/settings/data-sources", source, JsonOptions, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadApiErrorAsync(response, token));
        return await response.Content.ReadFromJsonAsync<GXDataSourceRequest>(JsonOptions, token) ?? throw new InvalidOperationException("Missing data source.");
    }

    public async Task SaveDataSourceAsync(GXDataSourceRequest source, CancellationToken token)
    {
        using var response = await _httpClient.PutAsJsonAsync($"api/settings/data-sources/{source.InstanceId}", source, JsonOptions, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadApiErrorAsync(response, token));
    }

    public async Task DeleteDataSourceAsync(Guid id, CancellationToken token)
    {
        using var response = await _httpClient.DeleteAsync($"api/settings/data-sources/{id}", token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadApiErrorAsync(response, token));
    }
    public async Task<GXDataSourceOverview> GetDataSourceOverviewAsync(CancellationToken token)
    {
        using var response = await _httpClient.GetAsync("api/settings/data-sources", token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadApiErrorAsync(response, token));
        return await response.Content.ReadFromJsonAsync<GXDataSourceOverview>(JsonOptions, token) ?? new GXDataSourceOverview();
    }

    public async Task<List<GXDatabase>> GetDatabaseCatalogAsync(CancellationToken token)
    {
        using var response = await _httpClient.GetAsync("api/databases", token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadApiErrorAsync(response, token));
        return await response.Content.ReadFromJsonAsync<List<GXDatabase>>(JsonOptions, token) ?? [];
    }

    public async Task TestCatalogDatabaseAsync(GXDatabase database, CancellationToken token)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/databases/test", database, JsonOptions, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadApiErrorAsync(response, token));
    }

    public async Task ImportCatalogSchemaAsync(Guid databaseId, Stream schema, CancellationToken token)
    {
        using var content = new StreamContent(schema);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        using var response = await _httpClient.PostAsync($"api/databases/{databaseId}/schema", content, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadApiErrorAsync(response, token));
    }

    public async Task SaveCatalogDatabaseAsync(GXDatabase database, CancellationToken token)
    {
        using var response = await _httpClient.PutAsJsonAsync($"api/databases/{database.Id}", database, JsonOptions, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadApiErrorAsync(response, token));
        var saved = await response.Content.ReadFromJsonAsync<GXDatabase>(JsonOptions, token)
            ?? throw new InvalidOperationException("Database save returned no data.");
        GXEntityMetadata.ApplySaved(saved, database);
    }

    public async Task DeleteCatalogDatabaseAsync(GXDatabase database, CancellationToken token)
    {
        using var response = await _httpClient.DeleteAsync($"api/databases/{database.Id}?concurrencyStamp={Uri.EscapeDataString(database.ConcurrencyStamp ?? "")}", token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadApiErrorAsync(response, token));
    }
    public async Task ImportAllSettingsAsync(string json, CancellationToken cancellationToken)
    {
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("api/settings/archive/import", content, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
    }

    public async Task<string> ExportTableDataAsync(string mode, Guid databaseId, string tableName,
        CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? filters = null)
    {
        using var output = new MemoryStream();
        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
        writer.WriteStartArray();
        int offset = 0;
        int? total = null;
        List<string>? columns = null;
        do
        {
            var page = await GetTableDataAsync(mode, databaseId, tableName, offset, 1000,
                cancellationToken, filters == null ? null : new Dictionary<string, string>(filters));
            total ??= page.TotalCount;
            columns ??= page.Columns;
            if (!columns.SequenceEqual(page.Columns) || (page.Rows.Count == 0 && offset < total))
                throw new InvalidOperationException("Table data changed during export. Please try again.");
            foreach (var row in page.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row.Values.Count != columns.Count) throw new InvalidOperationException("Table row does not match its columns.");
                writer.WriteStartObject();
                for (int i = 0; i < columns.Count; ++i)
                {
                    writer.WritePropertyName(columns[i]);
                    row.Values[i].WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            offset += page.Rows.Count;
        } while (offset < total);
        writer.WriteEndArray();
        await writer.FlushAsync(cancellationToken);
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }

    public async Task<string> ExportTableSchemaAsync(Guid databaseId, string tableName, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"api/databases/{databaseId}/schema?tableName={Uri.EscapeDataString(tableName)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task ImportTableSchemaAsync(Guid databaseId, Stream json, CancellationToken cancellationToken)
    {
        using var content = new StreamContent(json);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        using var response = await _httpClient.PostAsync($"api/database/client/{databaseId}/schema", content, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
    }

    /// <summary>Returns configuration tables that need a schema update.</summary>
    public async Task<List<GXConfigurationTableChange>> GetConfigurationTableChangesAsync(CancellationToken cancellationToken)
        => await _httpClient.GetFromJsonAsync<List<GXConfigurationTableChange>>("api/update", JsonOptions, cancellationToken) ?? [];

    public async Task<int> StartDataVaultMappingAsync(Guid mappingId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync($"api/datavault/mappings/{mappingId}/start", null, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
        return await response.Content.ReadFromJsonAsync<int>(cancellationToken);
    }
    public async Task<int> RefreshMartAsync(Guid databaseId, Guid mappingId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync($"api/datavault/databases/{databaseId}/marts/{mappingId}/refresh", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
        return await response.Content.ReadFromJsonAsync<int>(cancellationToken);
    }

    public async Task UpdateConfigurationTableAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync("api/update", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
    }

    public async Task ResetClientTableCheckpointAsync(GXResetCheckpointRequest request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/client/state/reset-checkpoint", request, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(await ReadApiErrorAsync(response, cancellationToken), null, response.StatusCode);
    }

    public async Task TestClientTransportConnectionAsync(Guid transportId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync($"api/client/transports/{transportId}/test-connection", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
    }

    public async Task<string> GetDatabaseDiagramAsync(string? mode, Guid databaseId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(mode is null ? $"api/databases/{databaseId}/diagram" : $"api/database/{mode}/{databaseId}/diagram", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task RunClientTransportAsync(Guid transportId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync($"api/client/transports/{transportId}/run", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
    }

    private static string DatabaseDataPath(string mode, Guid id) => mode == "databases" ? $"api/databases/{id}" : $"api/database/{mode}/{id}";

    public async Task<int> ImportTableJsonAsync(Guid databaseId, string tableName, Stream json,
        CancellationToken cancellationToken, string mode = "client")
    {
        using var content = new StreamContent(json);
        content.Headers.ContentType = new("application/json");
        using var response = await _httpClient.PostAsync(
            $"{DatabaseDataPath(mode, databaseId)}/import-json?tableName={Uri.EscapeDataString(tableName)}", content, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
        return await response.Content.ReadFromJsonAsync<int>(cancellationToken);
    }

    public async Task<int> ImportTableCsvAsync(Guid databaseId, string tableName, Stream csv,
        string delimiter, bool hasHeader, CancellationToken cancellationToken, string mode = "client")
    {
        using var content = new StreamContent(csv);
        content.Headers.ContentType = new("text/csv");
        using var response = await _httpClient.PostAsync(
            $"{DatabaseDataPath(mode, databaseId)}/import-csv?tableName={Uri.EscapeDataString(tableName)}&delimiter={Uri.EscapeDataString(delimiter)}&hasHeader={hasHeader}", content, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
        return await response.Content.ReadFromJsonAsync<int>(cancellationToken);
    }

    public async Task<GXTableData> GetTableDataAsync(string mode, Guid databaseId, string tableName,
        int startIndex, int count, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? filters = null)
    {
        string filterQuery = filters == null ? "" : string.Concat(filters.Where(f => !string.IsNullOrWhiteSpace(f.Value))
            .Select(f => $"&{Uri.EscapeDataString($"filters[{f.Key}]")}={Uri.EscapeDataString(f.Value)}"));
        using var response = await _httpClient.GetAsync(
            $"{DatabaseDataPath(mode, databaseId)}/data?tableName={Uri.EscapeDataString(tableName)}&startIndex={startIndex}&count={count}{filterQuery}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
        return await response.Content.ReadFromJsonAsync<GXTableData>(JsonOptions, cancellationToken)
            ?? new GXTableData();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions TypedJsonOptions = new(JsonOptions) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static readonly JsonSerializerOptions SchemaJsonOptions = new(JsonOptions)
    {
        PreferredObjectCreationHandling = System.Text.Json.Serialization.JsonObjectCreationHandling.Populate,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly HttpClient _httpClient;
    private readonly Gurux.UI.Components.IGXProgress? _progress;

    public GXAdminApiClient(HttpClient httpClient, Gurux.UI.Components.IGXProgress? progress = null)
    {
        _httpClient = httpClient;
        _progress = progress;
    }

    public async Task<GXSettings> GetSettingsAsync(string mode, CancellationToken cancellationToken,
        GXDatabase? filter = null)
    {
        using var progress = _progress?.ProgressStart("Get Settings...");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        var query = new List<string>();
        if (filter?.Type is { } type) query.Add($"Type={Uri.EscapeDataString(type.ToString())}");
        if (!string.IsNullOrWhiteSpace(filter?.Description))
            query.Add($"Description={Uri.EscapeDataString(filter.Description)}");
        string url = $"api/{mode}/settings" + (query.Count == 0 ? "" : "?" + string.Join("&", query));
        GXSettings? settings = await _httpClient.GetFromJsonAsync<GXSettings>(url, JsonOptions, cancellationToken);
        return settings ?? new GXSettings();
    }

    public async Task<IReadOnlyList<GXDatabase>> GetDatabasesAsync(string mode, CancellationToken cancellationToken)
    {
        using var progress = _progress?.ProgressStart("Get Databases...");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        List<GXDatabase?>? databases = await _httpClient.GetFromJsonAsync<List<GXDatabase?>>($"api/{mode}/databases", JsonOptions, cancellationToken);
        return databases?.OfType<GXDatabase>().ToList() ?? [];
    }

    public async Task<string?> SaveDatabasesAsync(string mode, IReadOnlyList<GXDatabase> databases, CancellationToken cancellationToken, string? concurrencyStamp = null)
    {
        using var progress = _progress?.ProgressStart("Save Databases...");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/{mode}/databases")
        {
            Content = JsonContent.Create(databases, options: JsonOptions)
        };
        if (concurrencyStamp is not null) request.Headers.IfMatch.Add(new($"\"{concurrencyStamp}\""));
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
        var saved = await response.Content.ReadFromJsonAsync<List<GXDatabase>>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The save response did not contain database versions.");
        for (int index = 0; index < databases.Count; ++index)
            GXEntityMetadata.ApplySaved(saved[index], databases[index]);
        return response.Headers.ETag?.Tag.Trim('"');
    }

    public async Task<GXStoreSettings> GetStoreSettingsAsync(CancellationToken cancellationToken)
    {
        using var progress = _progress?.ProgressStart("Get Store Settings...");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        using var response = await _httpClient.GetAsync("api/settings", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
        GXStoreSettings? settings = await response.Content.ReadFromJsonAsync<GXStoreSettings>(JsonOptions, cancellationToken);
        return settings ?? new GXStoreSettings();
    }

    public async Task SaveStoreSettingsAsync(GXStoreSettings settings, CancellationToken cancellationToken)
    {
        using var progress = _progress?.ProgressStart("Save Store Settings...");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync("api/settings", settings, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
        }
    }

    public async Task TestStoreSettingsAsync(GXStoreSettings settings, CancellationToken cancellationToken)
    {
        using var progress = _progress?.ProgressStart("Test Store Settings...");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        var payload = new { Settings = settings };

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/settings/test", payload, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
        }
    }

    private static async Task<string> ReadApiErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"Request failed with status code {(int)response.StatusCode}.";
        }

        try
        {
            var problem = JsonSerializer.Deserialize<GXApiProblemDetails>(body, JsonOptions);
            if (!string.IsNullOrWhiteSpace(problem?.Detail)) return problem.Detail;
            if (!string.IsNullOrWhiteSpace(problem?.Title))
            {
                var details = problem.Errors?.SelectMany(pair => (pair.Value ?? []).Where(message => !string.IsNullOrWhiteSpace(message)).Select(message => $"{pair.Key}: {message}")).ToArray() ?? [];
                return details.Length == 0 ? problem.Title : problem.Title + " " + string.Join(" | ", details);
            }
        }
        catch
        {
        }

        return body;
    }

    public async Task SaveSettingsAsync(string mode, GXSettings settings, CancellationToken cancellationToken)
    {
        using var progress = _progress?.ProgressStart("Save Settings...");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync($"api/{mode}/settings", settings, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string error = await ReadApiErrorAsync(response, cancellationToken);
            GXSettings? committed = null;
            try
            {
                using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                if (problem.RootElement.TryGetProperty("savedSettings", out var snapshot))
                    committed = snapshot.Deserialize<GXSettings>(JsonOptions);
            }
            catch (JsonException) { /* Other API failures can have a plain-text body. */ }
            if (committed is not null)
            {
                GXEntityMetadata.ApplySaved(committed, settings);
                throw new GXSettingsActivationException(error);
            }
            throw new InvalidOperationException(error);
        }
        var saved = await response.Content.ReadFromJsonAsync<GXSettings>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The save response did not contain settings versions.");
        GXEntityMetadata.ApplySaved(saved, settings);
    }

    public async Task<string> ExportSettingsAsync(string mode, CancellationToken cancellationToken)
    {
        using var progress = _progress?.ProgressStart("Export Settings...");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        using HttpResponseMessage response = await _httpClient.GetAsync($"api/{mode}/settings/export", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task ImportSettingsAsync(string mode, GXSettings settings, CancellationToken cancellationToken)
    {
        using var progress = _progress?.ProgressStart("Import Settings...");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"api/{mode}/settings/import", settings, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<GXEventLog>> GetEventsAsync(string mode, int top, LogLevel? minimumLevel, int? databaseIndex, CancellationToken cancellationToken)
    {
        using var progress = _progress?.ProgressStart("Get Log...");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"api/{mode}/events", new
        {
            Top = top,
            LogLevel = minimumLevel,
            DatabaseIndex = databaseIndex,
        }, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<GXEventLog>>(TypedJsonOptions, cancellationToken) ?? [];
    }

    public async Task<int> ClearEventsAsync(string mode, int? databaseIndex, CancellationToken cancellationToken)
    {
        using var progress = _progress?.ProgressStart("Clear Log...");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        string url = databaseIndex is null
            ? $"api/{mode}/events"
            : $"api/{mode}/events?databaseIndex={databaseIndex.Value}";

        using HttpResponseMessage response = await _httpClient.DeleteAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
        }

        var body = await response.Content.ReadFromJsonAsync<GXDeleteEventsResult>(cancellationToken);
        return body?.DeletedRows ?? 0;
    }

    public async Task<GXState?> GetStateAsync(string mode, CancellationToken cancellationToken)
    {
        using var progress = _progress?.ProgressStart("Get State...");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        GXState? state = await _httpClient.GetFromJsonAsync<GXState?>($"api/{mode}/state", cancellationToken);
        return state;
    }

    public async Task<int> SendClientSchemaAsync(CancellationToken cancellationToken)
    {
        using var progress = _progress?.ProgressStart("Send Client Schema...");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        using HttpResponseMessage response = await _httpClient.PostAsync("api/client/send-schema", null, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<GXSendSchemasResult>(cancellationToken);
        return body?.SentSchemas ?? 0;
    }

    public async Task ResetClientStateAsync(CancellationToken cancellationToken)
    {
        using var progress = _progress?.ProgressStart("Reset Client State...");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        using HttpResponseMessage response = await _httpClient.PostAsync("api/client/reset", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public Task SendClientTableSchemaAsync(GXClientTableRequest request, CancellationToken cancellationToken)
        => SendTableActionAsync("send-schema", string.Format("Send '{0}' table Schema to servers.", request.TableName), request, cancellationToken);

    public Task PingClientTableAsync(GXClientTableRequest request, CancellationToken cancellationToken)
        => SendTableActionAsync("ping", string.Format("Ping {0} servers.", request.TableName), request, cancellationToken);

    public Task RunClientTableAsync(GXClientTableRequest request, CancellationToken cancellationToken)
        => SendTableActionAsync("run", string.Format("Run {0} schedules.", request.TableName), request, cancellationToken);

    private async Task SendTableActionAsync(string action, string description, GXClientTableRequest request, CancellationToken cancellationToken)
    {
        using var progress = _progress?.ProgressStart(description);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"api/client/tables/{action}", request, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
    }

    public async Task<IReadOnlyList<string>> GetTablesAsync(GXDatabase databaseConfiguration, CancellationToken cancellationToken)
    {
        using var progress = _progress?.ProgressStart("Get Tables...");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/database/tables", databaseConfiguration, cancellationToken);
        response.EnsureSuccessStatusCode();
        IReadOnlyList<string>? tables = await response.Content.ReadFromJsonAsync<IReadOnlyList<string>>(cancellationToken);
        return tables ?? [];
    }

    public async Task<GXMartPreview> GetMartPreviewAsync(Guid databaseId, string sourceTable, Guid? hubMappingId, CancellationToken cancellationToken)
    {
        string url = $"api/datavault/databases/{databaseId}/marts/preview?sourceTable={Uri.EscapeDataString(sourceTable)}";
        if (hubMappingId.HasValue) url += $"&hubMappingId={hubMappingId.Value}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
        return await response.Content.ReadFromJsonAsync<GXMartPreview>(JsonOptions, cancellationToken) ?? throw new InvalidOperationException("Missing mart preview.");
    }

    public async Task<GXEventPage<GXEventLog>> GetEventPageAsync(string mode, GXEventPageRequest request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/{mode}/events/page", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GXEventPage<GXEventLog>>(TypedJsonOptions, cancellationToken) ?? new();
    }

    public async Task<GXCreateVaultTableResult> CreateMartAsync(Guid databaseId, GXCreateMartRequest request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/datavault/databases/{databaseId}/marts", request, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
        return await response.Content.ReadFromJsonAsync<GXCreateVaultTableResult>(JsonOptions, cancellationToken) ?? throw new InvalidOperationException("Missing mart creation result.");
    }

    public async Task<GXCreateVaultTableResult> CreateVaultTableAsync(Guid databaseId, GXCreateVaultTableRequest request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/datavault/databases/{databaseId}/tables", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
        return await response.Content.ReadFromJsonAsync<GXCreateVaultTableResult>(JsonOptions, cancellationToken) ?? throw new InvalidOperationException("Missing table creation result.");
    }

    public async Task<GXTableSchema> GetColumnsAsync(GXDatabase databaseConfiguration, string tableName, CancellationToken cancellationToken)
    {
        using var progress = _progress?.ProgressStart("Get Columns...");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"api/database/columns/{Uri.EscapeDataString(tableName)}", databaseConfiguration, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
        return await response.Content.ReadFromJsonAsync<GXTableSchema>(SchemaJsonOptions, cancellationToken) ?? new GXTableSchema();
    }

    public async Task TestConnectionAsync(GXDatabase databaseConfiguration, CancellationToken cancellationToken)
    {
        using var progress = _progress?.ProgressStart(string.Format("Testing connection for database '{0}'.", databaseConfiguration.Description));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/database/test", databaseConfiguration, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
        }
    }

    public async Task<IReadOnlyList<GXDataVaultTableMapping>> GetDataVaultMappingsAsync(CancellationToken cancellationToken)
    {
        using var progress = _progress?.ProgressStart("Get Data Vault Mappings...");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        List<GXDataVaultTableMapping?>? mappings = await _httpClient.GetFromJsonAsync<List<GXDataVaultTableMapping?>>(
            "api/datavault/mappings", JsonOptions, cancellationToken);
        return mappings?.OfType<GXDataVaultTableMapping>().ToList() ?? [];
    }

    public async Task DeleteDataVaultMappingAsync(Guid id, CancellationToken cancellationToken)
    {
        using var progress = _progress?.ProgressStart("Delete Data Vault Mapping...");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        using HttpResponseMessage response = await _httpClient.DeleteAsync($"api/datavault/mappings/{id}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<GXDataVaultMappingDetails> GetDataVaultMappingAsync(Guid id, CancellationToken cancellationToken)
    {
        using var progress = _progress?.ProgressStart("Get Data Vault Mapping...");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        var mapping = await _httpClient.GetFromJsonAsync<GXDataVaultMappingDetails>($"api/datavault/mappings/{id}", TypedJsonOptions, cancellationToken);
        return mapping ?? throw new InvalidOperationException("Mapping details were not returned.");
    }

    public async Task SaveDataVaultMappingAsync(Guid? id, Guid database, GXDataVaultTableMapping mapping, CancellationToken cancellationToken)
    {
        using var progress = _progress?.ProgressStart("Save Data Vault Mapping...");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progress?.CancellationToken ?? CancellationToken.None);
        cancellationToken = cancellation.Token;
        GXDataVaultMappingRequest payload = new()
        {
            Database = database,
            Mapping = mapping,
        };

        using HttpResponseMessage response = id is null
            ? await _httpClient.PostAsJsonAsync("api/datavault/mappings", payload, TypedJsonOptions, cancellationToken)
            : await _httpClient.PutAsJsonAsync($"api/datavault/mappings/{id.Value}", payload, TypedJsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadApiErrorAsync(response, cancellationToken));
    }

}
