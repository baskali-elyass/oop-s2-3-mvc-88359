using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Domain;
using VgcCollege.MVC.Data;

namespace VgcCollege.MVC.Controllers;

[Authorize(Roles = "Admin,Faculty")]
public class ExamsController(ApplicationDbContext context, UserManager<IdentityUser> userManager) : Controller
{
    public async Task<IActionResult> Index(int? courseId)
    {
        IQueryable<Exam> query = context.Exams.Include(e => e.Course).ThenInclude(c => c.Branch);

        if (User.IsInRole("Faculty"))
        {
            var userId = userManager.GetUserId(User)!;
            var faculty = await context.FacultyProfiles.FirstOrDefaultAsync(f => f.IdentityUserId == userId);
            if (faculty is null) return Forbid();
            var courseIds = await context.FacultyCourseAssignments
                .Where(a => a.FacultyProfileId == faculty.Id).Select(a => a.CourseId).ToListAsync();
            query = query.Where(e => courseIds.Contains(e.CourseId));
        }

        if (courseId.HasValue) query = query.Where(e => e.CourseId == courseId);
        ViewBag.Courses = new SelectList(await context.Courses.OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
        return View(await query.OrderBy(e => e.Date).ToListAsync());
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null) return NotFound();
        var exam = await context.Exams
            .Include(e => e.Course)
            .Include(e => e.Results).ThenInclude(r => r.StudentProfile)
            .FirstOrDefaultAsync(e => e.Id == id);
        return exam is null ? NotFound() : View(exam);
    }

    public async Task<IActionResult> Create(int? courseId)
    {
        ViewBag.Courses = new SelectList(await context.Courses.OrderBy(c => c.Name).ToListAsync(), "Id", "Name", courseId);
        return View(new Exam { Date = DateTime.Today });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("CourseId,Title,Date,MaxScore,ResultsReleased")] Exam exam)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Courses = new SelectList(await context.Courses.OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
            return View(exam);
        }
        context.Add(exam);
        await context.SaveChangesAsync();
        TempData["Success"] = "Exam created.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var exam = await context.Exams.FindAsync(id);
        if (exam is null) return NotFound();
        ViewBag.Courses = new SelectList(await context.Courses.OrderBy(c => c.Name).ToListAsync(), "Id", "Name", exam.CourseId);
        return View(exam);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,CourseId,Title,Date,MaxScore,ResultsReleased")] Exam exam)
    {
        if (id != exam.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewBag.Courses = new SelectList(await context.Courses.OrderBy(c => c.Name).ToListAsync(), "Id", "Name", exam.CourseId);
            return View(exam);
        }
        try { context.Update(exam); await context.SaveChangesAsync(); TempData["Success"] = "Exam updated."; }
        catch (DbUpdateConcurrencyException) { if (!context.Exams.Any(e => e.Id == id)) return NotFound(); throw; }
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReleaseResults(int id)
    {
        var exam = await context.Exams.FindAsync(id);
        if (exam is null) return NotFound();
        exam.ResultsReleased = true;
        await context.SaveChangesAsync();
        TempData["Success"] = $"Results for '{exam.Title}' released.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var exam = await context.Exams.Include(e => e.Course).FirstOrDefaultAsync(e => e.Id == id);
        return exam is null ? NotFound() : View(exam);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var exam = await context.Exams.FindAsync(id);
        if (exam is null) return NotFound();
        context.Exams.Remove(exam);
        await context.SaveChangesAsync();
        TempData["Success"] = "Exam deleted.";
        return RedirectToAction(nameof(Index));
    }
}
