using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace TaskApi
{
    public class TaskDbContext : DbContext
    {
        public TaskDbContext(DbContextOptions<TaskDbContext> options) : base(options) { }

        public DbSet<TaskItem> Tasks => Set<TaskItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var task = modelBuilder.Entity<TaskItem>();

            task.HasKey(x => x.Id);
            task.Property(x => x.Title)
                .IsRequired()
                .HasMaxLength(200);

            task.Property(x => x.Priority)
                .HasConversion<string>()
                .HasMaxLength(20);

        }
    }
}
