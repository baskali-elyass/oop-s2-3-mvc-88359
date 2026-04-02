using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Domain;
using VgcCollege.MVC.Data;

namespace VgcCollege.MVC.Controllers;

[Authorize(Roles = "Admin,Faculty")]
public class AssignmentResultsController(ApplicationDbContext context, UserManager<IdentityUser> userManager) : Controller
{
    public async Task<IActionResult> Index(int? assignmentId)
    {
        if (assignmentId is null) return RedirectToAction("Index", "Assignments");

        var assignment = await context.Assignments.Include(a => a.Course).FirstOrDefaultAsync(a => a.Id == assignmentId);
        if (assignment is null) return NotFound();

        if (User.IsInRole("Faculty"))
        {
            var userId = userManager.GetUserId(User)!;
            var faculty = await context.FacultyProfiles.FirstOrDefaultAsync(f => f.IdentityUserId == userId);
            if (faculty is null) return Forbid();
            var hasCourse = await context.FacultyCourseAssignments
                .AnyAsync(a => a.FacultyProfileId == faculty.Id && a.CourseId == assignment.CourseId);
            if (!hasCourse) return Forbid();
        }

        ViewBag.Assignment = assignment;
        var results = await context.AssignmentResults
            .Include(r => r.StudentProfile)
            .Where(r => r.AssignmentId == assignmentId)
            .OrderBy(r => r.StudentProfile.Name)
            .ToListAsync();
        return View(results);
    }

    public async Task<IActionResult> Create(int assignmentId)
    {
        var assignment = await context.Assignments.Include(a => a.Course).FirstOrDefaultAsync(a => a.Id == assignmentId);
        if (assignment is null) return NotFound();

        var enrolledStudentIds = await context.CourseEnrolments
            .Where(e => e.CourseId == assignment.CourseId)
            .Select(e => e.StudentProfileId).ToListAsync();
        var gradedIds = await context.AssignmentResults
            .Where(r => r.AssignmentId == assignmentId)
            .Select(r => r.StudentProfileId).ToListAsync();
        var available = await context.StudentProfiles
            .Where(s => enrolledStudentIds.Contains(s.Id) && !gradedIds.Contains(s.Id))
            .OrderBy(s => s.Name).ToListAsync();

        ViewBag.Assignment = assignment;
        ViewBag.Students = new SelectList(available, "Id", "Name");
        return View(new AssignmentResult { AssignmentId = assignmentId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("AssignmentId,StudentProfileId,Score,Feedback")] AssignmentResult result)
    {
        if (await context.AssignmentResults.AnyAsync(r => r.AssignmentId == result.AssignmentId && r.StudentProfileId == result.StudentProfileId))
            ModelState.AddModelError("", "Result already exists for this student.");

        var assignment = await context.Assignments.FindAsync(result.AssignmentId);
        if (assignment != null && result.Score > assignment.MaxScore)
            ModelState.AddModelError("Score", $"Score cannot exceed maximum ({assignment.MaxScore}).");

        if (!ModelState.IsValid)
        {
            ViewBag.Assignment = assignment;
            var enrolledStudentIds = await context.CourseEnrolments
                .Where(e => e.CourseId == assignment!.CourseId).Select(e => e.StudentProfileId).ToListAsync();
            ViewBag.Students = new SelectList(await context.StudentProfiles
                .Where(s => enrolledStudentIds.Contains(s.Id)).OrderBy(s => s.Name).ToListAsync(), "Id", "Name");
            return View(result);
        }
        result.SubmittedAt = DateTime.UtcNow;
        context.Add(result);
        await context.SaveChangesAsync();
        TempData["Success"] = "Result saved.";
        return RedirectToAction(nameof(Index), new { assignmentId = result.AssignmentId });
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var result = await context.AssignmentResults
            .Include(r => r.Assignment).Include(r => r.StudentProfile)
            .FirstOrDefaultAsync(r => r.Id == id);
        return result is null ? NotFound() : View(result);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,AssignmentId,StudentProfileId,Score,Feedback,SubmittedAt")] AssignmentResult result)
    {
        if (id != result.Id) return NotFound();
        var assignment = await context.Assignments.FindAsync(result.AssignmentId);
        if (assignment != null && result.Score > assignment.MaxScore)
            ModelState.AddModelError("Score", $"Score cannot exceed maximum ({assignment.MaxScore}).");

        if (!ModelState.IsValid) return View(result);
        try { context.Update(result); await context.SaveChangesAsync(); TempData["Success"] = "Result updated."; }
        catch (DbUpdateConcurrencyException) { if (!context.AssignmentResults.Any(r => r.Id == id)) return NotFound(); throw; }
        return RedirectToAction(nameof(Index), new { assignmentId = result.AssignmentId });
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var result = await context.AssignmentResults
            .Include(r => r.Assignment).Include(r => r.StudentProfile)
            .FirstOrDefaultAsync(r => r.Id == id);
        return result is null ? NotFound() : View(result);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var result = await context.AssignmentResults.FindAsync(id);
        if (result is null) return NotFound();
        var assignmentId = result.AssignmentId;
        context.AssignmentResults.Remove(result);
        await context.SaveChangesAsync();
        TempData["Success"] = "Result deleted.";
        return RedirectToAction(nameof(Index), new { assignmentId });
    }
}
