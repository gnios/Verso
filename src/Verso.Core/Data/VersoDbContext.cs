using Microsoft.EntityFrameworkCore;
using Verso.Core.Data.Entities;

namespace Verso.Core.Data;

public class VersoDbContext(DbContextOptions<VersoDbContext> options) : DbContext(options)
{
    public DbSet<ResearchPage> ResearchPages => Set<ResearchPage>();
    public DbSet<Transcription> Transcriptions => Set<Transcription>();
    public DbSet<Segment> Segments => Set<Segment>();
    public DbSet<Speaker> Speakers => Set<Speaker>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ResearchPage>()
            .HasMany(r => r.Transcriptions)
            .WithOne(t => t.ResearchPage)
            .HasForeignKey(t => t.ResearchPageId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Transcription>()
            .HasMany(t => t.Segments)
            .WithOne()
            .HasForeignKey(s => s.TranscriptionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Locutor escopado por transcrição (AD-003) — sem tabela global de Speaker.
        modelBuilder.Entity<Transcription>()
            .HasMany(t => t.Speakers)
            .WithOne()
            .HasForeignKey(s => s.TranscriptionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Segment>()
            .HasOne(s => s.Speaker)
            .WithMany()
            .HasForeignKey(s => s.SpeakerId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Transcription>()
            .HasMany(t => t.Tags)
            .WithMany()
            .UsingEntity(j => j.ToTable("TranscriptionTag"));

        modelBuilder.Entity<Tag>()
            .HasIndex(t => t.Name)
            .IsUnique();

        modelBuilder.Entity<Transcription>()
            .HasIndex(t => t.Status);
    }
}
