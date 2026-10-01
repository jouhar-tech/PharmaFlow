using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Models;

namespace PharmaFlow.Services;

public interface IPharmaFlowSavingsService
{
    Task<bool> RecordAsync(
        long profileId,
        string category,
        decimal amount,
        string? description,
        string? sourceType,
        string? sourceId,
        CancellationToken cancellationToken);
}

public sealed class PharmaFlowSavingsService : IPharmaFlowSavingsService
{
    private readonly ApplicationDbContext _db;

    public PharmaFlowSavingsService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<bool> RecordAsync(
        long profileId,
        string category,
        decimal amount,
        string? description,
        string? sourceType,
        string? sourceId,
        CancellationToken cancellationToken)
    {
        if (profileId <= 0 ||
            string.IsNullOrWhiteSpace(category) ||
            amount < 0m)
        {
            return false;
        }

        category = category.Trim();
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        sourceType = string.IsNullOrWhiteSpace(sourceType) ? null : sourceType.Trim();
        sourceId = string.IsNullOrWhiteSpace(sourceId) ? null : sourceId.Trim();

        if (sourceType is not null && sourceId is not null)
        {
            var alreadyRecorded = await _db.PharmaFlowSavingsEvents
                .AsNoTracking()
                .AnyAsync(
                    e => e.ProfileId == profileId &&
                         e.SourceType == sourceType &&
                         e.SourceId == sourceId,
                    cancellationToken);

            if (alreadyRecorded)
                return false;
        }

        _db.PharmaFlowSavingsEvents.Add(new PharmaFlowSavingsEvent
        {
            ProfileId = profileId,
            Category = category,
            Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
            Description = description,
            SourceType = sourceType,
            SourceId = sourceId,
            OccurredAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
