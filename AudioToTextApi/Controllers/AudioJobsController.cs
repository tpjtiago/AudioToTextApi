using AudioToTextApi.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;

namespace AudioToTextApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AudioJobsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public AudioJobsController(AppDbContext db) => _db = db;

        // GET /api/audiojobs/{jobId}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetStatus(int id)
        {
            var job = await _db.AudioJobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == id);

            if (job == null)
                return NotFound(new { Message = "Job não encontrado." });

            return Ok(new
            {
                job.JobId,
                job.Status,
                job.Transcript,
                job.Interpretation,
                job.CreatedAt,
                job.UpdatedAt
            });
        }
    }
}
