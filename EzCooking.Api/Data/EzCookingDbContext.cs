using Microsoft.EntityFrameworkCore;

namespace EzCooking.Api.Data;

public sealed class EzCookingDbContext(DbContextOptions<EzCookingDbContext> options) : DbContext(options)
{
    public DbSet<RecipeEntity> Recipes => Set<RecipeEntity>();
    public DbSet<TelegramBotState> TelegramBotStates => Set<TelegramBotState>();
    public DbSet<TelegramSubmission> TelegramSubmissions => Set<TelegramSubmission>();
    public DbSet<RecipeTranslation> RecipeTranslations => Set<RecipeTranslation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var recipe = modelBuilder.Entity<RecipeEntity>();
        recipe.ToTable("recipes");
        recipe.HasKey(x => x.Id);
        recipe.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        recipe.Property(x => x.SourceUrl).HasColumnName("source_url");
        recipe.Property(x => x.Status).HasColumnName("status");
        recipe.Property(x => x.Error).HasColumnName("error");
        recipe.Property(x => x.Document).HasColumnName("document").HasColumnType("jsonb");
        recipe.Property(x => x.Version).HasColumnName("version");
        recipe.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        recipe.Property(x => x.AudioConsent).HasColumnName("audio_consent");
        recipe.Property(x => x.Dismissed).HasColumnName("dismissed");
        recipe.Property(x => x.UploadSha256).HasColumnName("upload_sha256");

        var translation = modelBuilder.Entity<RecipeTranslation>();
        translation.ToTable("recipe_translations");
        translation.HasKey(x => x.RecipeId);
        translation.Property(x => x.RecipeId).HasColumnName("recipe_id").ValueGeneratedNever();
        translation.Property(x => x.RecipeVersion).HasColumnName("recipe_version");
        translation.Property(x => x.Status).HasColumnName("status");
        translation.Property(x => x.Document).HasColumnName("document").HasColumnType("jsonb");
        translation.Property(x => x.Error).HasColumnName("error");
        translation.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        var state = modelBuilder.Entity<TelegramBotState>();
        state.ToTable("telegram_bot_state");
        state.HasKey(x => x.Singleton);
        state.Property(x => x.Singleton).HasColumnName("singleton").ValueGeneratedNever();
        state.Property(x => x.BotId).HasColumnName("bot_id");
        state.Property(x => x.UserId).HasColumnName("user_id");
        state.Property(x => x.NextUpdate).HasColumnName("next_update");
        state.Property(x => x.LastUpdateAt).HasColumnName("last_update_at");

        var submission = modelBuilder.Entity<TelegramSubmission>();
        submission.ToTable("telegram_submissions");
        submission.HasKey(x => x.Id);
        submission.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        submission.Property(x => x.SourceUrl).HasColumnName("source_url");
        submission.Property(x => x.TelegramMessageId).HasColumnName("telegram_message_id");
        submission.Property(x => x.CreatedAt).HasColumnName("created_at");
        submission.Property(x => x.State).HasColumnName("state");
        submission.Property(x => x.RecipeId).HasColumnName("recipe_id");
        submission.Property(x => x.LastNotifiedStatus).HasColumnName("last_notified_status");
    }
}
