using EncodingVideo.Api.EntityFramework;
using FFmpeg.NET.Enums;
using FFmpeg.NET;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting.Internal;
using InputFile = FFmpeg.NET.InputFile;
using System.IO;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;


namespace EncodingVideo.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [RequestFormLimits(MultipartBodyLengthLimit = 10L * 1024L * 1024L * 1024L)]
    [RequestSizeLimit(10L * 1024L * 1024L * 1024L)]
    public class VideoController(VideoDbContext context, IConfiguration configuration) : ControllerBase
    {
        readonly string _uploadFolder = Path.Combine(Environment.CurrentDirectory, "Uploads");



        [HttpPost]
        public async Task<IActionResult> UploadVideo([FromForm] UploadVideoRequest request)
        {
            var totalSize = request.Videos.Sum(x => x.Length);
            if (totalSize == 0) return BadRequest("Error, please select video");
            foreach (var video in request.Videos)
            {
                if (video.Length <= 0) return BadRequest("Video is not valid.");

                var conversionOptions1080P = new ConversionOptions
                {
                    VideoAspectRatio = VideoAspectRatio.R16_9,
                    VideoSize = VideoSize.Hd1080,
                    AudioSampleRate = AudioSampleRate.Hz44100,
                    VideoBitRate = 1000,
                    AudioBitRate = 128,
                    Threads = 1

                };

                var filePath = Path.Combine(_uploadFolder, video.FileName);

                await using (Stream fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await video.CopyToAsync(fileStream);
                }

                var ffmpeg = new Engine(Path.Combine(configuration["FFMpegPath"] ?? string.Empty, "ffmpeg.exe"));

                var inputFile = new InputFile(filePath);


                var outputFileName = Path.GetFileNameWithoutExtension(filePath) + "_1080p.mp4";
                var outputFile = new OutputFile(
                    Path.Combine(
                        _uploadFolder,
                        outputFileName)
                );

                var findVideo = await context.Videos!.FirstOrDefaultAsync(x => x.FileName.Equals(outputFileName));
                if (findVideo == null)
                {
                    context.Videos!.Add(new Video()
                    {
                        Id = Guid.NewGuid(),
                        FileName = outputFileName,
                        ParentId = null,
                        Quality = VideoQuality.FullHd,
                        Status = VideoStatus.Encoding,
                        Size = video.Length,

                    });
                }
                else
                {
                    findVideo.Status = VideoStatus.Encoding;
                }

                await context.SaveChangesAsync();


                ffmpeg.Complete += Ffmpeg_Complete_Using_Context;
                ffmpeg.Complete += Ffmpeg_Complete_Using_AdoNet;

                //CPU Bound, prefer Thread to Task
                new Thread(
                    () => ffmpeg.ConvertAsync(inputFile, outputFile, conversionOptions1080P, CancellationToken.None)
                    ).Start();
            }
            return Accepted();
        }


        [HttpGet]
        public async Task<IActionResult> GetVideo(Guid id)
        {
            var video = await context.Videos!.FirstOrDefaultAsync(x => x.Id.Equals(id));
            if (video == null) return BadRequest();

            switch (video.Status)
            {
                case VideoStatus.Queued:
                    return Accepted("Video is queued");
                case VideoStatus.Encoding:
                    return Accepted("Video is processing");
            }

            var memory = new MemoryStream();

            await using (var file = new FileStream(Path.Combine(_uploadFolder, video.FileName), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await file.CopyToAsync(memory);
            }
            memory.Position = 0;

            return File(memory, "video/mp4", video.FileName);
        }

        private static async void Ffmpeg_Complete_Using_AdoNet(object? sender, FFmpeg.NET.Events.ConversionCompleteEventArgs e)
        {
            await using var connection =
                new SqlConnection(
                    "Data Source=TalonEzio;Initial Catalog=VideoStreaming;Integrated Security=True;Trust Server Certificate=True");
            await connection.OpenAsync();

            var fileName = Path.GetFileName(e.Output.Name);

            var cmd = new SqlCommand("select id from dbo.Videos where fileName = @p1", connection);
            cmd.Parameters.AddWithValue("@p1", fileName);

            var data = await cmd.ExecuteReaderAsync();
            if (!data.HasRows) return;
            var guid = Guid.Empty;

            while (await data.ReadAsync())
            {
                guid = data.GetGuid(0);
            }

            await data.CloseAsync();

            var updateCmd = new SqlCommand("update dbo.Videos set status = @p1 where id = @p2", connection);
            updateCmd.Parameters.AddWithValue("@p1", VideoStatus.Completed);
            updateCmd.Parameters.AddWithValue("@p2", guid);

            await updateCmd.ExecuteNonQueryAsync();

            await connection.CloseAsync();
        }

        private async void Ffmpeg_Complete_Using_Context(object? sender,
            FFmpeg.NET.Events.ConversionCompleteEventArgs e)
        {
            var fileName = Path.GetFileName(e.Output.Name);

            var video = await context.Videos!.FirstOrDefaultAsync(x => x.FileName.Equals(fileName));
            if (video == null) return;
            video.Status = VideoStatus.Completed;

            await context.SaveChangesAsync();
        }
    }

    public class UploadVideoRequest
    {
        public required IEnumerable<IFormFile> Videos { get; set; }
    }

}

