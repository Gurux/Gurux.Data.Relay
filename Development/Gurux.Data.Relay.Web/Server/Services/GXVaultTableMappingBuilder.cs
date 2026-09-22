using Gurux.Service.Orm.Common.Model;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Shared;
using Gurux.Service.Orm.Model;
using Mapping = Gurux.Data.Relay.Configuration.GXDataVaultTableMapping;
using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Web.Server.Services;

public static class GXVaultTableMappingBuilder
{
    public static Mapping Build(GXCreateVaultTableRequest request, GXTableSchema target, Mapping? staging)
    {
        var mapping = new Mapping { SourceTable = staging?.SourceTable ?? new() { Id = Guid.Empty, Name = request.SourceTable }, TargetTable = new() { Id = Guid.Empty, Name = target.ToString() }, ObjectType = request.ObjectType };
        string Resolve(string source) => staging?.Columns.FirstOrDefault(c => string.Equals(c.TargetColumn, source, StringComparison.OrdinalIgnoreCase))?.SourceColumn ?? source;
        void Add(string source, string destination, DataVaultColumnRole role, bool resolved = false) => mapping.Columns.Add(new()
        { SourceColumn = Resolve(source), TargetColumn = resolved ? destination : request.ResolveTargetColumn(destination), Role = role });
        switch (request.ObjectType)
        {
            case DataVaultObjectType.Hub:
                Add(request.BusinessKeyColumn!, target.Columns.Single(c => c.IsPrimaryKey).Name, DataVaultColumnRole.HashKey, resolved: true);
                string businessKey = string.IsNullOrWhiteSpace(request.TargetBusinessKeyColumn)
                    ? request.BusinessKeyColumn + "_BK" : request.TargetBusinessKeyColumn;
                Add(request.BusinessKeyColumn!, businessKey, DataVaultColumnRole.BusinessKey);
                break;
            case DataVaultObjectType.Link:
                Add(request.Parents[0].SourceColumn, target.Columns.Single(c => c.IsPrimaryKey).Name, 
                    DataVaultColumnRole.LinkHashKey, resolved: true);
                goto case DataVaultObjectType.Satellite;
            case DataVaultObjectType.Satellite:
                foreach (var parent in request.Parents)
                {
                    Add(parent.SourceColumn, parent.Column, DataVaultColumnRole.ParentHashKey);
                }
                if (request.ObjectType == DataVaultObjectType.Satellite) Add(request.Columns[0], "HASH_DIFF", DataVaultColumnRole.HashDiff);
                break;
            case DataVaultObjectType.Reference:
                Add(request.BusinessKeyColumn!, request.BusinessKeyColumn!, DataVaultColumnRole.BusinessKey);
                break;
        }
        if (request.ObjectType is DataVaultObjectType.Satellite or DataVaultObjectType.Reference)
            foreach (var column in request.Columns) Add(column, column, DataVaultColumnRole.Attribute);
        foreach (var role in new[] { DataVaultColumnRole.LoadDate, DataVaultColumnRole.RecordSource })
        {
            string name = role == DataVaultColumnRole.LoadDate ? "LOAD_DATE" : "RECORD_SOURCE";
            var metadata = staging?.Columns.FirstOrDefault(c => c.Role == role);
            mapping.Columns.Add(new() { SourceColumn = metadata?.SourceColumn ?? name, TargetColumn = request.ResolveTargetColumn(name), Role = role });
        }
        return mapping;
    }
}
