using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Domain;
using VgcCollege.MVC.Data;

namespace VgcCollege.MVC.Controllers;

[Authorize(Roles = "Admin,Faculty")]
public class ExamResultsController(ApplicationDbContext context) : Controller
{
    public async Task<IActionResult> Index(int? examId)
    {
        if (examId is null) return RedirectToAction("Index", "Exams");
        var exam = await context.Exams.Include(e => e.Course).FirstOrDefaultAsync(e => e.Id == examId);
        if (exam is null) return NotFound();
        ViewBag.Exam = exam;
        var results = await context.ExamResults
            .Include(r => r.StudentProfile)
            .Where(r => r.ExamId == examId)
            .OrderBy(r => r.StudentProfile.Name)
            .ToListAsync();
        return View(results);
    }

    public async Task<IActionResult> Create(int examId)
    {
        var exam = await context.Exams.Include(e => e.Course).FirstOrDefaultAsync(e => e.Id == examId);
        if (exam is null) return NotFound();

        var enrolledIds = await context.CourseEnrolments
            .Where(e => e.CourseId == exam.CourseId).Select(e => e.StudentProfileId).ToListAsync();
        var gradedIds = await context.ExamResults
            .Where(r => r.ExamId == examId).Select(r => r.StudentProfileId).ToListAsync();
        var available = await context.StudentProfiles
            .Where(s => enrolledIds.Contains(s.Id) && !gradedIds.Contains(s.Id))
            .OrderBy(s => s.Name).ToListAsync();

        ViewBag.Exam = exam;
        ViewBag.Students = new SelectList(available, "Id", "Name");
        return View(new ExamResult { ExamId = examId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("ExamId,StudentProfileId,Score,Grade")] ExamResult result)
    {
        if (await context.ExamResults.AnyAsync(r => r.ExamId == result.ExamId && r.StudentProfileId == result.StudentProfileId))
            ModelState.AddModelError("", "Result already exists for this student.");
        var exam = await context.Exams.FindAsync(result.ExamId);
        if (exam != null && result.Score > exam.MaxScore)
            ModelState.AddModelError("Score", $"Score cannot exceed maximum ({exam.MaxScore}).");

        if (!ModelState.IsValid)
        {
            ViewBag.Exam = exam;
            var enrolledIds = await context.CourseEnrolments
                .Where(e => e.CourseId == exam!.CourseId).Select(e => e.StudentProfileId).ToListAsync();
            ViewBag.Students = new SelectList(await context.StudentProfiles
                .Where(s => enrolledIds.Contains(s.Id)).OrderBy(s => s.Name).ToListAsync(), "Id", "Name");
            return View(result);
        }
        context.Add(result);
        await context.SaveChangesAsync();
        TempData["Success"] = "Exam result saved.";
        return RedirectToAction(nameof(Index), new { examId = result.ExamId });
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var result = await context.ExamResults
            .Include(r => r.Exam).Include(r => r.StudentProfile)
            .FirstOrDefaultAsync(r => r.Id == id);
        return result is null ? NotFound() : View(result);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,ExamId,StudentProfileId,Score,Grade")] ExamResult result)
    {
        if (id != result.Id) return NotFound();
        var exam = await context.Exams.FindAsync(result.ExamId);
        if (exam != null && result.Score > exam.MaxScore)
            ModelState.AddModelError("Score", $"Score cannot exceed maximum ({exam.MaxScore}).");
        if (!ModelState.IsValid) return View(result);
        try { context.Update(result); await context.SaveChangesAsync(); TempData["Success"] = "Result updated."; }
        catch (DbUpdateConcurrencyException) { if (!context.ExamResults.Any(r => r.Id == id)) return NotFound(); throw; }
        return RedirectToAction(nameof(Index), new { examId = result.ExamId });
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var result = await context.ExamResults
            .Include(r => r.Exam).Include(r => r.StudentProfile)
            .FirstOrDefaultAsync(r => r.Id == id);
        return result is null ? NotFound() : View(result);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var result = await context.ExamResults.FindAsync(id);
        if (result is null) return NotFound();
        var examId = result.ExamId;
        context.ExamResults.Remove(result);
        await context.SaveChangesAsync();
        TempData["Success"] = "Result deleted.";
        return RedirectToAction(nameof(Index), new { examId });
    }
}
