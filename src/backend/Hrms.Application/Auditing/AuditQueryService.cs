using System.Text.Json;
using Hrms.Application.Abstractions;
using Hrms.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Hrms.Shared.Auditing;

namespace Hrms.Application.Auditing;

public sealed record AuditQueryParams(
    string? ModuleCode = null, string? EntityType = null, string? EntityId = null,
    string? Action = null, Guid? ActorId = null,
    DateTimeOffset? From = null, DateTimeOffset? To = null, int Skip = 0, int Take = 50);

public sealed record AuditFieldChangeDto(string Field, string? OldValue, string? NewValue,
    string? OldLabel, string? NewLabel, bool Masked);

internal sealed class FieldChange
{
    public required string Field { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string? OldLabel { get; set; }
    public string? NewLabel { get; set; }
    public bool Masked { get; init; }
}

public sealed record AuditEntryDto(Guid Id, DateTimeOffset OccurredAt, string ModuleCode, string EntityType,
    string EntityId, string? EntityLabel, string Action, string ActorType, Guid? ActorId, string? ActorDisplay,
    string Source, string? Reason, string? IpAddress, string? CorrelationId,
    IReadOnlyList<AuditFieldChangeDto> Fields);

/// <summary>
/// Read-side audit queries shared by tenant and platform realms. Expands the stored before/after JSON into
/// per-field changes and resolves foreign-key guids to display names using the EF model — any [Audited]
/// entity's FK gets a meaningful label with no per-module wiring.
/// </summary>
public class AuditQueryService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Masked = "***";
    private static readonly string[] DisplayCandidates =
        ["DisplayName", "Name", "Code", "Title", "Email", "OriginalFileName", "LegalName", "Subject", "EmployeeCode"];

    private readonly DbContext _db;
    protected AuditQueryService(DbContext db) => _db = db;

    public async Task<IReadOnlyList<AuditEntryDto>> SearchAsync(AuditQueryParams q, CancellationToken ct)
    {
        var take = Math.Clamp(q.Take, 1, 200);
        var query = _db.Set<AuditEntry>().AsNoTracking().OrderByDescending(e => e.OccurredAt).AsQueryable();
        if (q.ModuleCode is { Length: > 0 } m) query = query.Where(e => e.ModuleCode == m);
        if (q.EntityType is { Length: > 0 } ety) query = query.Where(e => e.EntityType == ety);
        if (q.EntityId is { Length: > 0 } eid) query = query.Where(e => e.EntityId == eid);
        if (q.Action is { Length: > 0 } a) query = query.Where(e => e.Action == a);
        if (q.ActorId is { } actor) query = query.Where(e => e.ActorId == actor);
        if (q.From is { } from) query = query.Where(e => e.OccurredAt >= from);
        if (q.To is { } to) query = query.Where(e => e.OccurredAt <= to);

        var entries = await query.Skip(Math.Max(0, q.Skip)).Take(take).ToListAsync(ct);

        // Collect every guid field + entity id, resolve them in one batch per target table.
        var parsed = entries.Select(e => (Entry: e, Fields: Expand(e))).ToList();
        var lookups = new Dictionary<LookupKey, List<Guid>>();
        var requests = new List<(FieldChange F, LookupKey Key, Guid Id, bool Old)>();
        var entityLabelTargets = new List<(int Index, LookupKey Key, Guid Id)>();

        for (var i = 0; i < parsed.Count; i++)
        {
            var (entry, fields) = parsed[i];
            var et = FindEntityType(entry.EntityType);
            foreach (var f in fields)
            {
                foreach (var (value, old) in new[] { (f.OldValue, true), (f.NewValue, false) })
                {
                    if (!f.Masked && Guid.TryParse(value, out var id) &&
                        ResolveTarget(et, f.Field) is { } key)
                    {
                        lookups.GetOrAdd(key, []).Add(id);
                        requests.Add((f, key, id, old));
                    }
                }
            }

            if (et is not null && Guid.TryParse(entry.EntityId, out var eidGuid) &&
                TableKey(et) is { } tk)
            {
                entityLabelTargets.Add((i, tk, eidGuid));
                lookups.GetOrAdd(tk, []).Add(eidGuid);
            }
        }

        var labels = await LoadLabelsAsync(lookups, ct);
        foreach (var (f, key, id, old) in requests)
        {
            if (labels.TryGetValue((key, id), out var label))
            {
                if (old) f.OldLabel = label; else f.NewLabel = label;
            }
        }

        var entityLabels = entityLabelTargets
            .Where(t => labels.ContainsKey((t.Key, t.Id)))
            .ToDictionary(t => t.Index, t => labels[(t.Key, t.Id)]);

        return parsed.Select((p, i) => new AuditEntryDto(p.Entry.Id, p.Entry.OccurredAt, p.Entry.ModuleCode,
            p.Entry.EntityType, p.Entry.EntityId, entityLabels.GetValueOrDefault(i), p.Entry.Action,
            p.Entry.ActorType, p.Entry.ActorId, p.Entry.ActorDisplay, p.Entry.Source, p.Entry.Reason,
            p.Entry.IpAddress, p.Entry.CorrelationId,
            p.Fields.Select(f => new AuditFieldChangeDto(f.Field, f.OldValue, f.NewValue, f.OldLabel, f.NewLabel, f.Masked)).ToList())).ToList();
    }

    private List<FieldChange> Expand(AuditEntry e)
    {
        var before = Parse(e.Before);
        var after = Parse(e.After);
        var names = e.Action == AuditActions.Create ? after.Keys
            : e.Action == AuditActions.Delete ? before.Keys
            : ParseList(e.Changes).Concat(before.Keys).Distinct();
        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Select(n =>
        {
            var o = before.GetValueOrDefault(n);
            var a = after.GetValueOrDefault(n);
            var masked = Format(o) == Masked || Format(a) == Masked;
            return new FieldChange { Field = n, OldValue = Format(o), NewValue = Format(a), Masked = masked };
        }).ToList();
    }

    private static string? Format(JsonElement? el) => el switch
    {
        null => null,
        { ValueKind: JsonValueKind.Null } => null,
        { ValueKind: JsonValueKind.String } s => s.GetString(),
        { ValueKind: JsonValueKind.True } => "true",
        { ValueKind: JsonValueKind.False } => "false",
        var x => x.Value.ToString(),
    };

    private static Dictionary<string, JsonElement?> Parse(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<Dictionary<string, JsonElement?>>(json, Json) ?? [];

    private static List<string> ParseList(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<List<string>>(json, Json) ?? [];

    private IEntityType? FindEntityType(string clrName) =>
        _db.Model.GetEntityTypes().FirstOrDefault(t => t.ClrType.Name == clrName);

    /// <summary>FK property → target table/label column via the EF model; null when the property isn't an FK.</summary>
    private static LookupKey? ResolveTarget(IEntityType? et, string field)
    {
        var prop = et?.FindProperty(field);
        var principal = prop?.GetContainingForeignKeys().FirstOrDefault()?.PrincipalEntityType;
        return principal is null ? null : TableKey(principal);
    }

    private static LookupKey? TableKey(IEntityType et)
    {
        var table = et.GetTableName();
        if (table is null) return null;
        var col = DisplayCandidates.Select(c => et.FindProperty(c)).FirstOrDefault(p => p is not null);
        return new LookupKey(et.GetSchema() ?? "public", table, col?.GetColumnName() ?? "id");
    }

    private sealed record LookupKey(string Schema, string Table, string Column);

    private async Task<Dictionary<(LookupKey, Guid), string>> LoadLabelsAsync(
        Dictionary<LookupKey, List<Guid>> lookups, CancellationToken ct)
    {
        var result = new Dictionary<(LookupKey, Guid), string>();
        if (lookups.Count == 0) return result;
        var conn = _db.Database.GetDbConnection();
        var openedHere = conn.State == System.Data.ConnectionState.Closed;
        if (openedHere) await conn.OpenAsync(ct);
        try
        {
            foreach (var (key, ids) in lookups)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"select id, \"{key.Column}\" as label from \"{key.Schema}\".\"{key.Table}\" where id = any(@ids)";
                var p = cmd.CreateParameter();
                p.ParameterName = "ids";
                p.Value = ids.Distinct().ToArray();
                cmd.Parameters.Add(p);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var label = reader.IsDBNull(1) ? null : reader.GetValue(1)?.ToString();
                    if (label is not null) result[(key, reader.GetGuid(0))] = label;
                }
            }
        }
        finally
        {
            if (openedHere) await conn.CloseAsync();
        }

        return result;
    }
}

public sealed class TenantAuditQueryService(ITenantDbContext db) : AuditQueryService((DbContext)db);

public sealed class PlatformAuditQueryService(IPlatformDbContext db) : AuditQueryService((DbContext)db);

file static class DictExt
{
    public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> d, TKey key, TValue v)
        where TKey : notnull where TValue : class, new() =>
        d.TryGetValue(key, out var existing) ? existing : d[key] = v;
}
