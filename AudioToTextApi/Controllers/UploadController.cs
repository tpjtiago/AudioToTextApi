using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Google.Cloud.PubSub.V1;

namespace AudioToTextApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UploadController : ControllerBase
    {
        private readonly IConfiguration _config;
        public UploadController(IConfiguration config) => _config = config;

        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Nenhum arquivo enviado.");

            var bucket = _config["Gcs:Bucket"];
            var projectId = _config["Gcp:ProjectId"];
            var topicId = _config["Gcp:PubSubTopic"];

            // 1. Salvar no GCS
            var storage = await Google.Cloud.Storage.V1.StorageClient.CreateAsync();
            var objectName = Guid.NewGuid() + Path.GetExtension(file.FileName);

            using (var stream = file.OpenReadStream())
            {
                await storage.UploadObjectAsync(bucket, objectName, null, stream);
            }

            var filePath = $"gs://{bucket}/{objectName}";
            var jobId = Guid.NewGuid().ToString();

            // 2. Publicar no Pub/Sub
            var publisher = await PublisherClient.CreateAsync(
                TopicName.FromProjectTopic(projectId, topicId)
            );

            var message = new
            {
                JobId = jobId,
                FilePath = filePath
            };

            string json = JsonConvert.SerializeObject(message);
            await publisher.PublishAsync(ByteString.CopyFromUtf8(json));

            return Ok(new { JobId = jobId, Status = "processing" });
        }
    }
}
