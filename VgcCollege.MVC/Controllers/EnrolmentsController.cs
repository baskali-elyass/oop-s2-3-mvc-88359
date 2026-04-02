using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Domain;
using VgcCollege.MVC.Data;

namespace VgcCollege.MVC.Controllers;

[Authorize(Roles = "Admin")]
public class EnrolmentsController(ApplicationDbContext context) : Controller
{
    public async Task<IActionResult> Index(int? courseId, int? studentId)
    {
        var query = context.CourseEnrolments
            .Include(e => e.StudentProfile)
            .Include(e => e.Course).ThenInclude(c => c.Branch)
            .AsQueryable();

        if (courseId.HasValue) query = query.Where(e => e.CourseId == courseId);
        if (studentId.HasValue) query = query.Where(e => e.StudentProfileId == studentId);

        ViewBag.FilterCourseId = courseId;
        ViewBag.FilterStudentId = studentId;
        ViewBag.Courses = new SelectList(await context.Courses.OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
        ViewBag.Students = new SelectList(await context.StudentProfiles.OrderBy(s => s.Name).ToListAsync(), "Id", "Name");

        return View(await query.OrderBy(e => e.Course.Name).ThenBy(e => e.StudentProfile.Name).ToListAsync());
    }

    public async Task<IActionResult> Create(int? studentId, int? courseId)
    {
        ViewBag.Students = new SelectList(await context.StudentProfiles.OrderBy(s => s.Name).ToListAsync(), "Id", "Name", studentId);
        ViewBag.Courses = new SelectList(await context.Courses.Include(c => c.Branch).OrderBy(c => c.Name).ToListAsync(), "Id", "Name", courseId);
        return View(new CourseEnrolment { EnrolDate = DateTime.Today, Status = "Active" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("StudentProfileId,CourseId,EnrolDate,Status")] CourseEnrolment enrolment)
    {
        if (await context.CourseEnrolments.AnyAsync(e => e.StudentProfileId == enrolment.StudentProfileId && e.CourseId == enrolment.CourseId))
            ModelState.AddModelError("", "Student is already enrolled in this course.");

        if (!ModelState.IsValid)
        {
            ViewBag.Students = new SelectList(await context.StudentProfiles.OrderBy(s => s.Name).ToListAsync(), "Id", "Name", enrolment.StudentProfileId);
            ViewBag.Courses = new SelectList(await context.Courses.Include(c => c.Branch).OrderBy(c => c.Name).ToListAsync(), "Id", "Name", enrolment.CourseId);
            return View(enrolment);
        }
        context.Add(enrolment);
        await context.SaveChangesAsync();
        TempData["Success"] = "Enrolment created.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var enrolment = await context.CourseEnrolments.FindAsync(id);
        if (enrolment is null) return NotFound();
        ViewBag.Students = new SelectList(await context.StudentProfiles.OrderBy(s => s.Name).ToListAsync(), "Id", "Name", enrolment.StudentProfileId);
        ViewBag.Courses = new SelectList(await context.Courses.Include(c => c.Branch).OrderBy(c => c.Name).ToListAsync(), "Id", "Name", enrolment.CourseId);
        return View(enrolment);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,StudentProfileId,CourseId,EnrolDate,Status")] CourseEnrolment enrolment)
    {
        if (id != enrolment.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewBag.Students = new SelectList(await context.StudentProfiles.OrderBy(s => s.Name).ToListAsync(), "Id", "Name", enrolment.StudentProfileId);
            ViewBag.Courses = new SelectList(await context.Courses.Include(c => c.Branch).OrderBy(c => c.Name).ToListAsync(), "Id", "Name", enrolment.CourseId);
            return View(enrolment);
        }
        try { context.Update(enrolment); await context.SaveChangesAsync(); TempData["Success"] = "Enrolment updated."; }
        catch (DbUpdateConcurrencyException) { if (!context.CourseEnrolments.Any(e => e.Id == id)) return NotFound(); throw; }
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var enrolment = await context.CourseEnrolments
            .Include(e => e.StudentProfile).Include(e => e.Course)
            .FirstOrDefaultAsync(e => e.Id == id);
        return enrolment is null ? NotFound() : View(enrolment);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var enrolment = await context.CourseEnrolments.FindAsync(id);
        if (enrolment is null) return NotFound();
        context.CourseEnrolments.Remove(enrolment);
        await context.SaveChangesAsync();
        TempData["Success"] = "Enrolment deleted.";
        return RedirectToAction(nameof(Index));
    }
}
