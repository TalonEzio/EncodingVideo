using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EncodingVideo.Api.EntityFramework
{
    public class VideoDbContext(IConfiguration configuration) : DbContext
    {
        public DbSet<Video>? Videos { get; set; }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer(configuration.GetConnectionString("Default"));
            base.OnConfiguring(optionsBuilder);
        }
    }

    public class Video
    {
        public Guid Id { get; set; }
        [Column(TypeName = "nvarchar(200)")]
        public required string FileName { get; set; }

        [Range(1, 10L * 1024L * 1024L * 1024L)]
        public long Size { get; set; }
        public VideoStatus Status { get; set; }
        public Guid? ParentId { get; set; }
        public VideoQuality Quality { get; set; }
    }

    public enum VideoStatus
    {
        Queued = 1,
        Encoding = 2,
        Completed = 3
    }

    public enum VideoQuality
    {
        UltraHd = 2160,
        FullHd = 1080,
        Hd = 720,
        Sd = 480

    }
}
