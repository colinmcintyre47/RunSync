// Data/RunSyncDbContext.cs
// The single entry point for all database operations in RunSync.
// Uses Entity Framework Core 8 with the Pomelo MySQL provider.
//
// Relationship summary:
//   User → StravaToken    : one-to-one,  cascade delete
//   User → StravaActivity : one-to-many, cascade delete
//
// The StravaActivity PK is Strava's own long ID (not auto-generated), which makes
// upserts idempotent — syncing the same activity twice doesn't create duplicates.
//
// → Registered in Program.cs via builder.Services.AddDbContext<RunSyncDbContext>()
// → Injected into StravaService.cs and ActivityService.cs via constructor DI
// → Migrations live in Data/Migrations/ — generate with: dotnet ef migrations add <Name>

using Microsoft.EntityFrameworkCore;
using RunSync.Api.Models.Entities;

namespace RunSync.Api.Data;

public class RunSyncDbContext : DbContext
{
    public RunSyncDbContext(DbContextOptions<RunSyncDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<StravaToken> StravaTokens { get; set; }
    public DbSet<StravaActivity> StravaActivities { get; set; }
    public DbSet<UserTrainingPlan> UserTrainingPlans { get; set; }
    public DbSet<StravaAppCredential> StravaAppCredentials { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── User ────────────────────────────────────────────────────────────
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.Email).HasMaxLength(256).IsRequired();
            entity.Property(u => u.PasswordHash).IsRequired();
            entity.Property(u => u.DisplayName).HasMaxLength(64).IsRequired();
        });

        // ── StravaToken (one-to-one with User) ──────────────────────────────
        modelBuilder.Entity<StravaToken>(entity =>
        {
            entity.HasOne(t => t.User)
                  .WithOne(u => u.StravaToken)
                  .HasForeignKey<StravaToken>(t => t.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── StravaAppCredential (one-to-one with User) ──────────────────────
        // Each user registers their own Strava API application; these are its credentials.
        // ClientSecretEncrypted holds AES-256-GCM ciphertext, never a plaintext secret.
        modelBuilder.Entity<StravaAppCredential>(entity =>
        {
            entity.HasOne(c => c.User)
                  .WithOne(u => u.StravaAppCredential)
                  .HasForeignKey<StravaAppCredential>(c => c.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Property(c => c.ClientId).HasMaxLength(64).IsRequired();

            // The ciphertext payload ("v1." + base64 of nonce|tag|ciphertext) is well under
            // 512 chars for a 40-character Strava secret, with headroom for key rotation.
            entity.Property(c => c.ClientSecretEncrypted).HasMaxLength(512).IsRequired();
        });

        // ── UserTrainingPlan (one-to-one with User) ─────────────────────────
        modelBuilder.Entity<UserTrainingPlan>(entity =>
        {
            entity.HasOne(p => p.User)
                  .WithOne(u => u.TrainingPlan)
                  .HasForeignKey<UserTrainingPlan>(p => p.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Property(p => p.GoalType).HasMaxLength(20).IsRequired();
            entity.Property(p => p.FitnessLevel).HasMaxLength(20).IsRequired();
            entity.Property(p => p.RunDays).HasMaxLength(100).IsRequired();
            entity.Property(p => p.LongRunDay).HasMaxLength(10).IsRequired();
        });

        // ── StravaActivity (one-to-many with User) ──────────────────────────
        modelBuilder.Entity<StravaActivity>(entity =>
        {
            // Use Strava's own ID as PK — prevents duplicates on re-sync
            entity.Property(a => a.Id).ValueGeneratedNever();

            entity.HasOne(a => a.User)
                  .WithMany(u => u.Activities)
                  .HasForeignKey(a => a.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Index on StartDateUtc enables fast date-range queries when matching to plan
            entity.HasIndex(a => a.StartDateUtc);

            // Composite index for the most common query: activities by user + date
            entity.HasIndex(a => new { a.UserId, a.StartDateLocal });
        });
    }
}
