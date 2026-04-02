using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Domain;
using VgcCollege.MVC.Data;

namespace VgcCollege.MVC.Controllers;

[Authorize(Roles = "Admin")]
public class FacultyController(ApplicationDbContext context, UserManager<IdentityUser> userManager) : Controller
{
    public async Task<IActionResult> Index()
        => View(await context.FacultyProfiles
            .Include(f => f.CourseAssignments).ThenInclude(a => a.Course)
            .OrderBy(f => f.Name).ToListAsync());

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null) return NotFound();
        var faculty = await context.FacultyProfiles
            .Include(f => f.CourseAssignments).ThenInclude(a => a.Course).ThenInclude(c => c.Branch)
            .FirstOrDefaultAsync(f => f.Id == id);
        return faculty is null ? NotFound() : View(faculty);
    }

    public IActionResult Create() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Name,Email,Phone")] FacultyProfile profile)
    {
        if (!ModelState.IsValid) return View(profile);

        var user = new IdentityUser { UserName = profile.Email, Email = profile.Email, EmailConfirmed = true };
        var result = await userManager.CreateAsync(user, "Faculty@1234");
        if (!result.Succeeded)
        {
            foreach (var err in result.Errors) ModelState.AddModelError("", err.Description);
            return View(profile);
        }
        await userManager.AddToRoleAsync(user, "Faculty");
        profile.IdentityUserId = user.Id;
        context.Add(profile);
        await context.SaveChangesAsync();
        TempData["Success"] = $"Faculty '{profile.Name}' created. Default password: Faculty@1234";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var faculty = await context.FacultyProfiles.FindAsync(id);
        return faculty is null ? NotFound() : View(faculty);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,IdentityUserId,Name,Email,Phone")] FacultyProfile profile)
    {
        if (id != profile.Id) return NotFound();
        if (!ModelState.IsValid) return View(profile);
        try { context.Update(profile); await context.SaveChangesAsync(); TempData["Success"] = "Faculty updated."; }
        catch (DbUpdateConcurrencyException) { if (!context.FacultyProfiles.Any(f => f.Id == id)) return NotFound(); throw; }
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var faculty = await context.FacultyProfiles.FirstOrDefaultAsync(f => f.Id == id);
        return faculty is null ? NotFound() : View(faculty);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var faculty = await context.FacultyProfiles.FindAsync(id);
        if (faculty is null) return NotFound();
        context.FacultyProfiles.Remove(faculty);
        await context.SaveChangesAsync();
        TempData["Success"] = "Faculty deleted.";
        return RedirectToAction(nameof(Index));
    }

    // Assign faculty to a course
    public async Task<IActionResult> Assign(int? id)
    {
        if (id is null) return NotFound();
        var faculty = await context.FacultyProfiles.FindAsync(id);
        if (faculty is null) return NotFound();
        ViewBag.Faculty = faculty;
        ViewBag.Courses = new SelectList(await context.Courses.Include(c => c.Branch).OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
        return View(new FacultyCourseAssignment { FacultyProfileId = id.Value });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign([Bind("FacultyProfileId,CourseId,IsTutor")] FacultyCourseAssignment assignment)
    {
        if (await context.FacultyCourseAssignments.AnyAsync(a => a.FacultyProfileId == assignment.FacultyProfileId && a.CourseId == assignment.CourseId))
            ModelState.AddModelError("", "Faculty already assigned to this course.");

        if (!ModelState.IsValid)
        {
            ViewBag.Faculty = await context.FacultyProfiles.FindAsync(assignment.FacultyProfileId);
            ViewBag.Courses = new SelectList(await context.Courses.Include(c => c.Branch).OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
            return View(assignment);
        }
        context.Add(assignment);
        await context.SaveChangesAsync();
        TempData["Success"] = "Faculty assigned to course.";
        return RedirectToAction(nameof(Details), new { id = assignment.FacultyProfileId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unassign(int id, int facultyId)
    {
        var assignment = await context.FacultyCourseAssignments.FindAsync(id);
        if (assignment is null) return NotFound();
        context.FacultyCourseAssignments.Remove(assignment);
        await context.SaveChangesAsync();
        TempData["Success"] = "Assignment removed.";
        return RedirectToAction(nameof(Details), new { id = facultyId });
    }
}
