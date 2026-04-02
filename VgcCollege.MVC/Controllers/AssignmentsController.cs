using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Domain;
using VgcCollege.MVC.Data;

namespace VgcCollege.MVC.Controllers;

[Authorize(Roles = "Admin,Faculty")]
public class AssignmentsController(ApplicationDbContext context, UserManager<IdentityUser> userManager) : Controller
{
    public async Task<IActionResult> Index(int? courseId)
    {
        IQueryable<Assignment> query = context.Assignments.Include(a => a.Course).ThenInclude(c => c.Branch);

        if (User.IsInRole("Faculty"))
        {
            var userId = userManager.GetUserId(User)!;
            var faculty = await context.FacultyProfiles.FirstOrDefaultAsync(f => f.IdentityUserId == userId);
            if (faculty is null) return Forbid();
            var courseIds = await context.FacultyCourseAssignments
                .Where(a => a.FacultyProfileId == faculty.Id).Select(a => a.CourseId).ToListAsync();
            query = query.Where(a => courseIds.Contains(a.CourseId));
        }

        if (courseId.HasValue) query = query.Where(a => a.CourseId == courseId);

        ViewBag.Courses = new SelectList(await context.Courses.OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
        return View(await query.OrderBy(a => a.DueDate).ToListAsync());
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null) return NotFound();
        var assignment = await context.Assignments
            .Include(a => a.Course)
            .Include(a => a.Results).ThenInclude(r => r.StudentProfile)
            .FirstOrDefaultAsync(a => a.Id == id);
        return assignment is null ? NotFound() : View(assignment);
    }

    public async Task<IActionResult> Create(int? courseId)
    {
        ViewBag.Courses = new SelectList(await context.Courses.OrderBy(c => c.Name).ToListAsync(), "Id", "Name", courseId);
        return View(new Assignment { DueDate = DateTime.Today.AddDays(14) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("CourseId,Title,MaxScore,DueDate")] Assignment assignment)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Courses = new SelectList(await context.Courses.OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
            return View(assignment);
        }
        context.Add(assignment);
        await context.SaveChangesAsync();
        TempData["Success"] = "Assignment created.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var assignment = await context.Assignments.FindAsync(id);
        if (assignment is null) return NotFound();
        ViewBag.Courses = new SelectList(await context.Courses.OrderBy(c => c.Name).ToListAsync(), "Id", "Name", assignment.CourseId);
        return View(assignment);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,CourseId,Title,MaxScore,DueDate")] Assignment assignment)
    {
        if (id != assignment.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewBag.Courses = new SelectList(await context.Courses.OrderBy(c => c.Name).ToListAsync(), "Id", "Name", assignment.CourseId);
            return View(assignment);
        }
        try { context.Update(assignment); await context.SaveChangesAsync(); TempData["Success"] = "Assignment updated."; }
        catch (DbUpdateConcurrencyException) { if (!context.Assignments.Any(a => a.Id == id)) return NotFound(); throw; }
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var assignment = await context.Assignments.Include(a => a.Course).FirstOrDefaultAsync(a => a.Id == id);
        return assignment is null ? NotFound() : View(assignment);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var assignment = await context.Assignments.FindAsync(id);
        if (assignment is null) return NotFound();
        context.Assignments.Remove(assignment);
        await context.SaveChangesAsync();
        TempData["Success"] = "Assignment deleted.";
        return RedirectToAction(nameof(Index));
    }
}
