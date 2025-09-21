using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Google.Cloud.PubSub.V1;
using Google.Cloud.Storage.V1;

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
            var objectName = Guid.NewGuid() + Path.GetExtension(file.FileName);

            // 🚀 Carrega primeiro em memória para evitar travar o stream do ASP.NET
            using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                await _storage.UploadObjectAsync(bucket, objectName, null, memoryStream);
            }

            var filePath = $"gs://{bucket}/{objectName}";
            var jobId = Guid.NewGuid().ToString();

            var message = new
            {
                JobId = jobId,
                FilePath = filePath
            };

            string json = JsonConvert.SerializeObject(message);

            // 🚀 Publicação no Pub/Sub reaproveitando o _publisher injetado
            await _publisher.PublishAsync(ByteString.CopyFromUtf8(json));

            return Ok(new { JobId = jobId, Status = "processing" });
        }
    }
}

