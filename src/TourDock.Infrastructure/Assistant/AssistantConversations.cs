using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TourDock.Application.Abstractions;
using TourDock.Infrastructure.Persistence;

namespace TourDock.Infrastructure.Assistant;

/// <summary>An assistant conversation, kept for a day after its last message (no account, no name, just the chat).</summary>
public sealed class AssistantConversation
{
    public Guid Id { get; set; }

    /// <summary>The Messages API conversation as JSON (see ClaudeHistory).</summary>
    public string History { get; set; } = "[]";

    public int Turns { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>Assistant messages per UTC day, for the global daily cap.</summary>
public sealed class AssistantDailyUsage
{
    public DateOnly Day { get; set; }

    public int Messages { get; set; }
}

internal sealed class AssistantConversationConfiguration : IEntityTypeConfiguration<AssistantConversation>
{
    public void Configure(EntityTypeBuilder<AssistantConversation> builder)
    {
        builder.ToTable("assistant_conversations");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.History).HasColumnType("jsonb");
        builder.HasIndex(c => c.ExpiresAt); // the retention job deletes by it
    }
}

internal sealed class AssistantDailyUsageConfiguration : IEntityTypeConfiguration<AssistantDailyUsage>
{
    public void Configure(EntityTypeBuilder<AssistantDailyUsage> builder)
    {
        builder.ToTable("assistant_daily_usage");
        builder.HasKey(u => u.Day);
    }
}

internal sealed class AssistantConversations(TourDockDbContext db, TimeProvider time, AssistantSettings settings) : IAssistantConversations
{
    public async Task<AssistantConversationState?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return await db.AssistantConversations.AsNoTracking()
            .Where(c => c.Id == id && c.ExpiresAt > now)
            .Select(c => new AssistantConversationState(c.Id, c.History, c.Turns))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SaveAsync(AssistantConversationState conversation, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var row = await db.AssistantConversations.FindAsync([conversation.Id], cancellationToken);
        if (row is null)
        {
            row = new AssistantConversation { Id = conversation.Id, CreatedAt = now };
            db.AssistantConversations.Add(row);
        }

        row.History = conversation.History;
        row.Turns = conversation.Turns;
        row.UpdatedAt = now;
        row.ExpiresAt = now + settings.Retention;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>One statement, so concurrent requests can't both take the last message of the day.</summary>
    public async Task<bool> TryCountMessageAsync(int dailyLimit, CancellationToken cancellationToken)
    {
        var day = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var counted = await db.Database.SqlQuery<int>($"""
            INSERT INTO assistant_daily_usage (day, messages) VALUES ({day}, 1)
            ON CONFLICT (day) DO UPDATE SET messages = assistant_daily_usage.messages + 1
            WHERE assistant_daily_usage.messages < {dailyLimit}
            RETURNING messages AS "Value"
            """).ToListAsync(cancellationToken);
        return counted.Count == 1;
    }
}
