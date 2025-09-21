using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace AudioToTextApi.Data
{
    public class AudioJob
    {
        public int Id { get; set; } // chave primária auto-increment
        public string JobId { get; set; }
        public string FilePath { get; set; }
        public string Status { get; set; } // processing / done / failed
        public string? Transcript { get; set; }
        public string? Interpretation { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<AudioJob> AudioJobs { get; set; }
    }
}
