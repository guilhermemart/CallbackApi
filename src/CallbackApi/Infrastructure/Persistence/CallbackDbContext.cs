using CallbackApi.Features.Events.Models;
using Microsoft.EntityFrameworkCore;

namespace CallbackApi.Infrastructure.Persistence;

public class CallbackDbContext(DbContextOptions<CallbackDbContext> options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Event>();

        entity.ToTable("events");
        entity.HasKey(eventItem => eventItem.Id);
        entity.Property(eventItem => eventItem.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(200)
            .IsRequired();
        entity.Property(eventItem => eventItem.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();
        entity.Property(eventItem => eventItem.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
        entity.Property(eventItem => eventItem.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
        entity.Property(eventItem => eventItem.DeletedAt)
            .HasColumnName("deleted_at");
    }
}
