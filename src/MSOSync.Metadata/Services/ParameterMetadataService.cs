using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MSOSync.Common;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.Descriptors;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Interfaces;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Services;

public sealed class ParameterMetadataService(
    AppDbContext db,
    IMemoryCache cache,
    IMediator mediator,
    ICurrentUserService currentUserService,
    IAuditService auditService) : IParameterMetadataService
{
    private const string SecretMask = "*****";
    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
    };

    public async Task<IReadOnlyList<ParameterDto>> GetParametersAsync(string? category = null, CancellationToken ct = default)
    {
        var query = db.Parameters.AsNoTracking()
            .Where(p => p.TenantId == null && (category == null || p.Category == category))
            .OrderBy(p => p.DisplayOrder == null ? int.MaxValue : p.DisplayOrder)
            .ThenBy(p => p.ParameterName);
        var parameters = await query.ToListAsync(ct);
        return parameters.Select(Map).ToList().AsReadOnly();
    }

    public async Task<ParameterDto?> GetParameterAsync(string name, CancellationToken ct = default)
    {
        var cacheKey = $"metadata:parameter:{name}";
        if (cache.TryGetValue<ParameterDto>(cacheKey, out var cached))
            return cached;

        var param = await db.Parameters.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ParameterName == name && p.TenantId == null, ct);
        if (param == null) return null;

        var dto = Map(param);
        cache.Set(cacheKey, dto, CacheOptions);
        return dto;
    }

    public async Task UpdateParameterAsync(string name, string value, CancellationToken ct = default)
    {
        var param = await db.Parameters
            .FirstOrDefaultAsync(p => p.ParameterName == name && p.TenantId == null, ct)
            ?? throw new NotFoundException($"Parameter '{name}' not found", "PARAMETER_NOT_FOUND");

        var descriptor = ParameterDescriptor.Catalog.GetValueOrDefault(name, ParameterDescriptor.Unknown(name));
        var oldValue = descriptor.IsSecret ? SecretMask : param.ParameterValue;
        var newValue = descriptor.IsSecret ? SecretMask : value;

        param.ParameterValue = value;
        db.ParameterHists.Add(new SyncParameterHist
        {
            ParameterName = name,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedBy = currentUserService.GetCurrentUsername(),
            ChangeTime = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);

        cache.Remove($"metadata:parameter:{name}");

        await auditService.WriteAsync(
            "PARAMETER_UPDATED",
            $"{name}: '{oldValue}' → '{newValue}'",
            currentUserService.GetCurrentUsername(),
            ct);

        await mediator.Publish(new ParameterChangedEvent(name, oldValue, newValue), ct);
    }

    public async Task<IReadOnlyList<ParameterHistoryDto>> GetParameterHistoryAsync(
        string name, CancellationToken ct = default)
    {
        var history = await db.ParameterHists.AsNoTracking()
            .Where(h => h.ParameterName == name)
            .OrderByDescending(h => h.ChangeTime)
            .ToListAsync(ct);
        return history.Select(MapHist).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<ParameterHistoryDto>> GetAllParameterHistoryAsync(
        CancellationToken ct = default)
    {
        var history = await db.ParameterHists.AsNoTracking()
            .OrderByDescending(h => h.ChangeTime)
            .ToListAsync(ct);
        return history.Select(MapHist).ToList().AsReadOnly();
    }

    private ParameterDto Map(SyncParameter p)
    {
        var descriptor = ParameterDescriptor.Catalog.GetValueOrDefault(
            p.ParameterName, ParameterDescriptor.Unknown(p.ParameterName));
        var value = descriptor.IsSecret ? SecretMask : p.ParameterValue;
        return new ParameterDto(
            ParameterName:  p.ParameterName,
            ParameterValue: value,
            Category:       p.Category,
            DisplayName:    p.DisplayName,
            Description:    p.Description ?? descriptor.Description,
            DisplayOrder:   p.DisplayOrder,
            ValueType:      p.ValueType,
            MinimumValue:   p.MinimumValue,
            MaximumValue:   p.MaximumValue,
            AllowedValues:  p.AllowedValues,
            IsSecret:       descriptor.IsSecret,
            IsDynamic:      descriptor.IsDynamic,
            RequiresRestart: descriptor.RequiresRestart);
    }

    private static ParameterHistoryDto MapHist(SyncParameterHist h) =>
        new(h.HistId, h.ParameterName, h.OldValue, h.NewValue, h.ChangedBy, h.ChangeTime);
}
