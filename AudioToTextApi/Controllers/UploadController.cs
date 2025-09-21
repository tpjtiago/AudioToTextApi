using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Google.Cloud.PubSub.V1;
using Google.Cloud.Storage.V1;
using System.Diagnostics;

namespace AudioToTextApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UploadController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly PublisherClient _publisher;
        private readonly StorageClient _storage;

        public UploadController(IConfiguration config, PublisherClient publisher, StorageClient storage)
        {
            _config = config;
            _publisher = publisher;
            _storage = storage;
        }

        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Nenhum arquivo enviado.");

            var bucket = _config["Gcs:Bucket"];
            var objectName = Guid.NewGuid() + ".flac";

            // --- Caminhos temporários ---
            var tempInput = Path.GetTempFileName() + Path.GetExtension(file.FileName);
            var tempOutput = Path.GetTempFileName() + ".wav";

            // --- Salva upload temporário ---
            using (var fs = new FileStream(tempInput, FileMode.Create))
            {
                await file.CopyToAsync(fs);
            }


            var ffmpeg = new ProcessStartInfo
            {
                FileName = @"C:\ffmpeg\bin\ffmpeg.exe",
                Arguments = $"-y -i \"{tempInput}\" -ar 16000 -ac 1 -f wav \"{tempOutput}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(ffmpeg))
            {
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    return StatusCode(500, $"Erro na conversão FFmpeg: {stderr}");
                }
            }

            // --- Upload para GCS ---
            using (var stream = System.IO.File.OpenRead(tempOutput))
            {
                await _storage.UploadObjectAsync(bucket, objectName, null, stream);
            }

            // --- Limpeza ---
            System.IO.File.Delete(tempInput);
            System.IO.File.Delete(tempOutput);

            var filePath = $"gs://{bucket}/{objectName}";
            var jobId = Guid.NewGuid().ToString();

            // --- Publicação no Pub/Sub ---
            var message = new { JobId = jobId, FilePath = filePath };
            string json = JsonConvert.SerializeObject(message);
            await _publisher.PublishAsync(ByteString.CopyFromUtf8(json));

            return Ok(new { JobId = jobId, Status = "processing", FilePath = filePath });
        }
    }
}
