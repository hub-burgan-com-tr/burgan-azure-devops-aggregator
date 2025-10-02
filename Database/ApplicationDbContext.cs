using BurganAzureDevopsAggregator.Models;
using Microsoft.EntityFrameworkCore;

namespace BurganAzureDevopsAggregator.Database
{
    public class ApplicationDbContext : DbContext
    {
        protected readonly IConfiguration Configuration;

        public ApplicationDbContext(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            // connect to sql server database
            options.UseSqlServer(Configuration.GetConnectionString("DefaultConnection"));
        }

        public DbSet<RuleModel> Rules { get; set; }
        public DbSet<RuleAction> RuleActions { get; set; }
        public DbSet<RuleActionParameter> RuleActionParameters { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RuleModel>()
            .HasKey(r => r.RuleId);

            // Priority için default değer 100 ayarla
            modelBuilder.Entity<RuleModel>()
                .Property(r => r.Priority)
                .HasDefaultValue(100);

            modelBuilder.Entity<RuleAction>()
            .HasKey(ra => ra.ActionId);
        modelBuilder.Entity<RuleAction>()
            .HasOne(ra => ra.Rule)
            .WithMany(r => r.Actions)
            .HasForeignKey(ra => ra.RuleId)
            .OnDelete(DeleteBehavior.Cascade);

        // RuleActionParameter için primary key ve ilişkileri yapılandır
        modelBuilder.Entity<RuleActionParameter>()
            .HasKey(rp => rp.ParameterId);
        // RuleActionParameter -> RuleAction ile olan ilişkiyi tanımlıyoruz
        modelBuilder.Entity<RuleActionParameter>()
            .HasOne(rp => rp.Action)
            .WithMany(ra => ra.Parameters)
            .HasForeignKey(rp => rp.ActionId)
            .OnDelete(DeleteBehavior.Cascade);

            base.OnModelCreating(modelBuilder);
        }
    }
}
