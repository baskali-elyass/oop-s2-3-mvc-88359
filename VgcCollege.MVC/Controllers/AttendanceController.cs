using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Domain;
using VgcCollege.MVC.Data;

namespace VgcCollege.MVC.Controllers;

[Authorize(Roles = "Admin,Faculty")]
public class AttendanceController(ApplicationDbContext context, UserManager<IdentityUser> userManager) : Controller
{
    public async Task<IActionResult> Index(int? enrolmentId)
    {
        if (enrolmentId is null)
            return View(new List<AttendanceRecord>());

        var enrolment = await context.CourseEnrolments
            .Include(e => e.StudentProfile)
            .Include(e => e.Course)
            .FirstOrDefaultAsync(e => e.Id == enrolmentId);

        if (enrolment is null) return NotFound();

        // Faculty: check they teach this course
        if (User.IsInRole("Faculty"))
        {
            var userId = userManager.GetUserId(User)!;
            var faculty = await context.FacultyProfiles.FirstOrDefaultAsync(f => f.IdentityUserId == userId);
            if (faculty is null) return Forbid();
            var hasCourse = await context.FacultyCourseAssignments
                .AnyAsync(a => a.FacultyProfileId == faculty.Id && a.CourseId == enrolment.CourseId);
            if (!hasCourse) return Forbid();
        }

        ViewBag.Enrolment = enrolment;
        var records = await context.AttendanceRecords
            .Where(a => a.CourseEnrolmentId == enrolmentId)
            .OrderBy(a => a.WeekNumber)
            .ToListAsync();
        return View(records);
    }

    public async Task<IActionResult> Create(int enrolmentId)
    {
        var enrolment = await context.CourseEnrolments
            .Include(e => e.StudentProfile).Include(e => e.Course)
            .FirstOrDefaultAsync(e => e.Id == enrolmentId);
        if (enrolment is null) return NotFound();
        ViewBag.Enrolment = enrolment;
        return View(new AttendanceRecord { CourseEnrolmentId = enrolmentId, SessionDate = DateTime.Today });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("CourseEnrolmentId,WeekNumber,SessionDate,Present,Notes")] AttendanceRecord record)
    {
        if (await context.AttendanceRecords.AnyAsync(a => a.CourseEnrolmentId == record.CourseEnrolmentId && a.WeekNumber == record.WeekNumber))
            ModelState.AddModelError("WeekNumber", "Attendance for this week already recorded.");

        if (!ModelState.IsValid)
        {
            ViewBag.Enrolment = await context.CourseEnrolments
                .Include(e => e.StudentProfile).Include(e => e.Course)
                .FirstOrDefaultAsync(e => e.Id == record.CourseEnrolmentId);
            return View(record);
        }
        context.Add(record);
        await context.SaveChangesAsync();
        TempData["Success"] = "Attendance recorded.";
        return RedirectToAction(nameof(Index), new { enrolmentId = record.CourseEnrolmentId });
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var record = await context.AttendanceRecords
            .Include(a => a.CourseEnrolment).ThenInclude(e => e.StudentProfile)
            .Include(a => a.CourseEnrolment).ThenInclude(e => e.Course)
            .FirstOrDefaultAsync(a => a.Id == id);
        return record is null ? NotFound() : View(record);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,CourseEnrolmentId,WeekNumber,SessionDate,Present,Notes")] AttendanceRecord record)
    {
        if (id != record.Id) return NotFound();
        if (!ModelState.IsValid) return View(record);
        try { context.Update(record); await context.SaveChangesAsync(); TempData["Success"] = "Attendance updated."; }
        catch (DbUpdateConcurrencyException) { if (!context.AttendanceRecords.Any(a => a.Id == id)) return NotFound(); throw; }
        return RedirectToAction(nameof(Index), new { enrolmentId = record.CourseEnrolmentId });
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var record = await context.AttendanceRecords
            .Include(a => a.CourseEnrolment).ThenInclude(e => e.StudentProfile)
            .FirstOrDefaultAsync(a => a.Id == id);
        return record is null ? NotFound() : View(record);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var record = await context.AttendanceRecords.FindAsync(id);
        if (record is null) return NotFound();
        var enrolmentId = record.CourseEnrolmentId;
        context.AttendanceRecords.Remove(record);
        await context.SaveChangesAsync();
        TempData["Success"] = "Record deleted.";
        return RedirectToAction(nameof(Index), new { enrolmentId });
    }
}
