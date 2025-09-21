using Google.Cloud.Speech.V1;
using Microsoft.AspNetCore.Mvc;
using Mscc.GenerativeAI;
using System.Diagnostics;

namespace AudioToTextApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TranscribeController : ControllerBase
    {
        private readonly IConfiguration _config;
        public TranscribeController(IConfiguration config) => _config = config;

        [HttpPost("process")]
        public async Task<IActionResult> ProcessAudio(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Nenhum arquivo enviado.");

            // --- 1. Salvar arquivo temporário ---
            var inputPath = Path.GetTempFileName();
            await using (var stream = new FileStream(inputPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // --- 2. Converter para WAV (Linear16, 16kHz, mono) usando FFmpeg ---
            var outputPath = Path.ChangeExtension(inputPath, ".wav");

            var ffmpeg = new ProcessStartInfo
            {
                FileName = @"C:\ffmpeg\bin\ffmpeg.exe",
                Arguments = $"-y -i \"{inputPath}\" -ar 16000 -ac 1 -f wav \"{outputPath}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(ffmpeg))
            {
                if (process == null) return StatusCode(500, "Falha ao iniciar o FFmpeg.");
                await process.WaitForExitAsync();
            }

            // --- 3. Ler áudio convertido ---
            var audioBytes = await System.IO.File.ReadAllBytesAsync(outputPath);

            // --- 4. Mandar para o Google Speech ---
            var speech = SpeechClient.Create();
            var audioResponse = await speech.RecognizeAsync(new RecognitionConfig
            {
                Encoding = RecognitionConfig.Types.AudioEncoding.Linear16,
                SampleRateHertz = 16000,
                LanguageCode = "pt-BR"
            }, RecognitionAudio.FromBytes(audioBytes));

            // --- 5. Extrair transcrição ---
            var transcript = string.Join(" ", audioResponse.Results.Select(r => r.Alternatives.FirstOrDefault()?.Transcript));

            // --- 6. Limpeza de arquivos temporários ---
            try
            {
                System.IO.File.Delete(inputPath);
                System.IO.File.Delete(outputPath);
            }
            catch { /* ignorar erros de limpeza */ }

            if (string.IsNullOrWhiteSpace(transcript))
                return Ok(new { Texto = "", Interpretacao = "Não foi possível transcrever o áudio." });

            // --- 7. Enviar para Gemini ---
            var apiKey = _config["Gemini:ApiKey"];
            IGenerativeAI genAi = string.IsNullOrWhiteSpace(apiKey) ? new GoogleAI() : new GoogleAI(apiKey);
            var model = genAi.GenerativeModel(model: Model.Gemini25Pro);

            var prompt = $"Texto transcrito: {transcript}\nResuma e identifique a intenção principal.";
            var geminiResponse = await model.GenerateContent(prompt);

            return Ok(new
            {
                Texto = transcript,
                Interpretacao = geminiResponse.Text
            });
        }
    }
}
