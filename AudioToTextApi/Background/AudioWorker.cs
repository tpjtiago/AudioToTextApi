using AudioToTextApi.Data;
using Google.Api.Gax;
using Google.Cloud.PubSub.V1;
using Google.Cloud.Speech.V1;
using Microsoft.EntityFrameworkCore;
using Mscc.GenerativeAI;
using Newtonsoft.Json;

namespace AudioToTextApi.Background
{
    public class AudioWorker : BackgroundService
    {
        private readonly IConfiguration _config;
        private readonly IServiceScopeFactory _scopeFactory;

        public AudioWorker(IConfiguration config, IServiceScopeFactory scopeFactory)
        {
            _config = config;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var projectId = _config["Gcp:ProjectId"];
            var subscriptionId = _config["Gcp:PubSubSubscription"];

            var subscriber = await SubscriberClient.CreateAsync(
                SubscriptionName.FromProjectSubscription(projectId, subscriptionId));

            Console.WriteLine($"👂 Worker iniciado - aguardando mensagens em {subscriptionId}");

            await subscriber.StartAsync(async (msg, ct) =>
            {
                var payload = JsonConvert.DeserializeObject<JobMessage>(msg.Data.ToStringUtf8());

                Console.WriteLine($"📥 Recebi job {payload.JobId} para {payload.FilePath}");

                // Usar scope para injetar DbContext
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // Criar ou atualizar registro no banco
                var jobEntity = await db.AudioJobs.FirstOrDefaultAsync(j => j.JobId == payload.JobId);
                if (jobEntity == null)
                {
                    jobEntity = new AudioJob
                    {
                        JobId = payload.JobId,
                        FilePath = payload.FilePath,
                        Status = "processing",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    db.AudioJobs.Add(jobEntity);
                    await db.SaveChangesAsync();
                }
                else
                {
                    jobEntity.Status = "processing";
                    jobEntity.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }

                try
                {
                    var speech = SpeechClient.Create();
                    var longOp = await speech.LongRunningRecognizeAsync(new LongRunningRecognizeRequest
                    {
                        Config = new RecognitionConfig
                        {
                            Encoding = RecognitionConfig.Types.AudioEncoding.Linear16,
                            LanguageCode = "pt-BR"
                        },
                        Audio = RecognitionAudio.FromStorageUri(payload.FilePath)
                    });

                    var pollTask = longOp.PollUntilCompletedAsync(
                                           new PollSettings(Expiration.FromTimeout(TimeSpan.FromMinutes(30)), TimeSpan.FromSeconds(15))
                                       );

                    var cancelTask = Task.Delay(Timeout.Infinite, ct);
                    var finished = await Task.WhenAny(pollTask, cancelTask);

                    if (finished != pollTask)
                        throw new OperationCanceledException(ct);

                    var completedOp = await pollTask;

                    var transcript = string.Join(" ",
                        completedOp.Result.Results
                            .Select(r => r.Alternatives.FirstOrDefault()?.Transcript ?? "")
                            .Where(t => !string.IsNullOrWhiteSpace(t))
                    );

                    if (string.IsNullOrWhiteSpace(transcript))
                    {
                        jobEntity.Status = "failed";
                        jobEntity.UpdatedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync();
                        return SubscriberClient.Reply.Ack;
                    }

                    var apiKey = _config["Gemini:ApiKey"];
                    IGenerativeAI genAi = new GoogleAI(apiKey);
                    var model = genAi.GenerativeModel(Model.Gemini25Pro);
                    var geminiResponse = await model.GenerateContent(
                        $"Texto transcrito: {transcript}\nResuma e identifique a intenção principal."
                    );

                    // Atualiza banco com resultado
                    jobEntity.Status = "done";
                    jobEntity.Transcript = transcript;
                    jobEntity.Interpretation = geminiResponse.Text;
                    jobEntity.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();

                    Console.WriteLine($"✅ Job {payload.JobId} concluído.");
                    return SubscriberClient.Reply.Ack;
                }
                catch (OperationCanceledException)
                {
                    jobEntity.Status = "failed";
                    jobEntity.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                    return SubscriberClient.Reply.Nack;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Erro no job {payload.JobId}: {ex.Message}");
                    jobEntity.Status = "failed";
                    jobEntity.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                    return SubscriberClient.Reply.Nack;
                }
            });
        }

        public class JobMessage
        {
            public string JobId { get; set; }
            public string FilePath { get; set; }
        }
    }
}

