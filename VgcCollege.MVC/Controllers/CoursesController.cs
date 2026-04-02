using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Domain;
using VgcCollege.MVC.Data;

namespace VgcCollege.MVC.Controllers;

[Authorize(Roles = "Admin")]
public class CoursesController(ApplicationDbContext context) : Controller
{
    public async Task<IActionResult> Index()
        => View(await context.Courses.Include(c => c.Branch).OrderBy(c => c.Name).ToListAsync());

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null) return NotFound();
        var course = await context.Courses
            .Include(c => c.Branch)
            .Include(c => c.Enrolments).ThenInclude(e => e.StudentProfile)
            .Include(c => c.FacultyAssignments).ThenInclude(a => a.FacultyProfile)
            .Include(c => c.Assignments)
            .Include(c => c.Exams)
            .FirstOrDefaultAsync(c => c.Id == id);
        return course is null ? NotFound() : View(course);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Branches = new SelectList(await context.Branches.OrderBy(b => b.Name).ToListAsync(), "Id", "Name");
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Name,BranchId,StartDate,EndDate")] Course course)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Branches = new SelectList(await context.Branches.OrderBy(b => b.Name).ToListAsync(), "Id", "Name");
            return View(course);
        }
        context.Add(course);
        await context.SaveChangesAsync();
        TempData["Success"] = $"Course '{course.Name}' created.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var course = await context.Courses.FindAsync(id);
        if (course is null) return NotFound();
        ViewBag.Branches = new SelectList(await context.Branches.OrderBy(b => b.Name).ToListAsync(), "Id", "Name", course.BranchId);
        return View(course);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,Name,BranchId,StartDate,EndDate")] Course course)
    {
        if (id != course.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewBag.Branches = new SelectList(await context.Branches.OrderBy(b => b.Name).ToListAsync(), "Id", "Name", course.BranchId);
            return View(course);
        }
        try { context.Update(course); await context.SaveChangesAsync(); TempData["Success"] = "Course updated."; }
        catch (DbUpdateConcurrencyException) { if (!context.Courses.Any(c => c.Id == id)) return NotFound(); throw; }
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var course = await context.Courses.Include(c => c.Branch).FirstOrDefaultAsync(c => c.Id == id);
        return course is null ? NotFound() : View(course);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var course = await context.Courses.Include(c => c.Enrolments).FirstOrDefaultAsync(c => c.Id == id);
        if (course is null) return NotFound();
        if (course.Enrolments.Any()) { TempData["Error"] = "Cannot delete a course with enrolments."; return RedirectToAction(nameof(Index)); }
        context.Courses.Remove(course);
        await context.SaveChangesAsync();
        TempData["Success"] = "Course deleted.";
        return RedirectToAction(nameof(Index));
    }
}
