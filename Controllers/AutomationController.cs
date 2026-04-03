using System.Text.Json;
using ETD.Api.Data;
using ETD.Api.Models;
using ETD.Api.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AutomationController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AutomationController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("jobs")]
        public IActionResult List([FromQuery] int take = 50)
        {
            if (take < 1) take = 1;
            if (take > 200) take = 200;

            var jobs = _context.AutomationJobs
                .AsNoTracking()
                .OrderByDescending(x => x.Id)
                .Take(take)
                .Select(x => new
                {
                    x.Id,
                    x.JobType,
                    x.QualificationId,
                    x.QualificationNumber,
                    x.Status,
                    x.RequiresApproval,
                    x.RequestedBy,
                    x.RequestedAtUtc,
                    x.ApprovedBy,
                    x.ApprovedAtUtc,
                    x.StartedAtUtc,
                    x.CompletedAtUtc,
                    x.OutputPath,
                    x.Error
                })
                .ToList();

            return Ok(jobs);
        }

        [HttpGet("jobs/{id:int}")]
        public IActionResult Get(int id)
        {
            var job = _context.AutomationJobs.AsNoTracking().FirstOrDefault(x => x.Id == id);
            if (job == null) return NotFound();
            return Ok(job);
        }

        [HttpPost("jobs/build-qualification")]
        public IActionResult QueueBuild([FromBody] QueueBuildRequest req)
        {
            if (req.QualificationId <= 0) return BadRequest("QualificationId is required.");

            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == req.QualificationId);
            if (qualification == null) return NotFound($"Qualification {req.QualificationId} not found.");

            var requiresApproval = req.RequiresApproval || req.RunImports || req.RunSeedWrite;
            var backendBase = string.IsNullOrWhiteSpace(req.BackendBase)
                ? $"{Request.Scheme}://{Request.Host}/api"
                : req.BackendBase.Trim().TrimEnd('/');
            var cfg = new
            {
                req.RunImports,
                req.RunSeedWrite,
                req.StartPage,
                req.ForceRestart,
                BackendBase = backendBase,
                ExecutionMode = string.IsNullOrWhiteSpace(req.ExecutionMode) ? "internal_pipeline" : req.ExecutionMode.Trim(),
                ScriptPath = string.IsNullOrWhiteSpace(req.ScriptPath)
                    ? EtdpPaths.CombineProject("AzureAgent", "smoke-test-agent.ps1")
                    : req.ScriptPath,
                PowerShellPath = string.IsNullOrWhiteSpace(req.PowerShellPath) ? "powershell.exe" : req.PowerShellPath
            };

            var job = new AutomationJob
            {
                JobType = "build_qualification",
                QualificationId = req.QualificationId,
                QualificationNumber = qualification.QualificationNumber,
                Status = requiresApproval ? "PendingApproval" : "Queued",
                RequiresApproval = requiresApproval,
                RequestedBy = req.RequestedBy,
                ConfigJson = JsonSerializer.Serialize(cfg)
            };
            _context.AutomationJobs.Add(job);
            _context.SaveChanges();

            return Ok(new
            {
                job.Id,
                job.Status,
                job.RequiresApproval,
                message = requiresApproval
                    ? "Job created and waiting for approval."
                    : "Job queued for execution."
            });
        }

        [HttpPost("jobs/{id:int}/approve")]
        public IActionResult Approve(int id, [FromBody] ApproveJobRequest req)
        {
            var job = _context.AutomationJobs.FirstOrDefault(x => x.Id == id);
            if (job == null) return NotFound();
            if (job.Status != "PendingApproval") return BadRequest($"Job is not pending approval. Current status: {job.Status}");

            job.Status = "Queued";
            job.ApprovedBy = string.IsNullOrWhiteSpace(req.ApprovedBy) ? "system" : req.ApprovedBy.Trim();
            job.ApprovedAtUtc = DateTime.UtcNow;
            _context.SaveChanges();
            return Ok(new { job.Id, job.Status, job.ApprovedBy, job.ApprovedAtUtc });
        }

        [HttpPost("jobs/{id:int}/cancel")]
        public IActionResult Cancel(int id, [FromBody] CancelJobRequest req)
        {
            var job = _context.AutomationJobs.FirstOrDefault(x => x.Id == id);
            if (job == null) return NotFound();

            if (job.Status is "Completed" or "Failed" or "Cancelled")
                return BadRequest($"Job cannot be cancelled from status: {job.Status}");

            if (job.Status == "Running")
                return BadRequest("Running jobs cannot be force-cancelled via API. Wait for completion.");

            job.Status = "Cancelled";
            job.Error = string.IsNullOrWhiteSpace(req.Reason) ? "Cancelled by operator." : req.Reason.Trim();
            job.CompletedAtUtc = DateTime.UtcNow;
            _context.SaveChanges();
            return Ok(new { job.Id, job.Status });
        }

        public sealed class QueueBuildRequest
        {
            public int QualificationId { get; set; }
            public bool RunImports { get; set; }
            public bool RunSeedWrite { get; set; }
            public bool RequiresApproval { get; set; } = true;
            public string? RequestedBy { get; set; }
            public string? BackendBase { get; set; }
            public int? StartPage { get; set; }
            public bool ForceRestart { get; set; }
            public string? ExecutionMode { get; set; }
            public string? ScriptPath { get; set; }
            public string? PowerShellPath { get; set; }
        }

        public sealed class ApproveJobRequest
        {
            public string? ApprovedBy { get; set; }
        }

        public sealed class CancelJobRequest
        {
            public string? Reason { get; set; }
        }
    }
}
