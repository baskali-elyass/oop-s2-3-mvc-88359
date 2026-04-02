using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Domain;
using VgcCollege.MVC.Data;

namespace VgcCollege.MVC.Controllers;

[Authorize]
public class StudentsController(ApplicationDbContext context, UserManager<IdentityUser> userManager) : Controller
{
    // Admin sees all; Faculty sees only their students; Student sees only self
    public async Task<IActionResult> Index()
    {
        var userId = userManager.GetUserId(User)!;

        if (User.IsInRole("Admin"))
            return View(await context.StudentProfiles.OrderBy(s => s.Name).ToListAsync());

        if (User.IsInRole("Faculty"))
        {
            var faculty = await context.FacultyProfiles.FirstOrDefaultAsync(f => f.IdentityUserId == userId);
            if (faculty is null) return Forbid();
            var courseIds = await context.FacultyCourseAssignments
                .Where(a => a.FacultyProfileId == faculty.Id).Select(a => a.CourseId).ToListAsync();
            var studentIds = await context.CourseEnrolments
                .Where(e => courseIds.Contains(e.CourseId)).Select(e => e.StudentProfileId).Distinct().ToListAsync();
            return View(await context.StudentProfiles.Where(s => studentIds.Contains(s.Id)).OrderBy(s => s.Name).ToListAsync());
        }

        // Student: only self
        var me = await context.StudentProfiles.FirstOrDefaultAsync(s => s.IdentityUserId == userId);
        if (me is null) return Forbid();
        return View(new List<StudentProfile> { me });
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null) return NotFound();
        var userId = userManager.GetUserId(User)!;

        var student = await context.StudentProfiles
            .Include(s => s.Enrolments).ThenInclude(e => e.Course).ThenInclude(c => c.Branch)
            .Include(s => s.AssignmentResults).ThenInclude(r => r.Assignment).ThenInclude(a => a.Course)
            .Include(s => s.ExamResults).ThenInclude(r => r.Exam).ThenInclude(e => e.Course)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (student is null) return NotFound();

        // Student can only see themselves
        if (User.IsInRole("Student") && student.IdentityUserId != userId) return Forbid();

        // Faculty can only see their students
        if (User.IsInRole("Faculty"))
        {
            var faculty = await context.FacultyProfiles.FirstOrDefaultAsync(f => f.IdentityUserId == userId);
            if (faculty is null) return Forbid();
            var courseIds = await context.FacultyCourseAssignments
                .Where(a => a.FacultyProfileId == faculty.Id).Select(a => a.CourseId).ToListAsync();
            var hasStudent = await context.CourseEnrolments
                .AnyAsync(e => courseIds.Contains(e.CourseId) && e.StudentProfileId == student.Id);
            if (!hasStudent) return Forbid();
        }

        // Hide provisional exam results for students
        if (User.IsInRole("Student"))
        {
            student.ExamResults = student.ExamResults
                .Where(r => r.Exam.ResultsReleased).ToList();
        }

        return View(student);
    }

    [Authorize(Roles = "Admin")]
    public IActionResult Create() => View();

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([Bind("Name,Email,Phone,Address,DateOfBirth,StudentNumber")] StudentProfile profile)
    {
        if (await context.StudentProfiles.AnyAsync(s => s.StudentNumber == profile.StudentNumber))
            ModelState.AddModelError("StudentNumber", "Student number already exists.");
        if (await context.StudentProfiles.AnyAsync(s => s.Email == profile.Email))
            ModelState.AddModelError("Email", "Email already in use.");

        if (!ModelState.IsValid) return View(profile);

        // Create identity user
        var user = new IdentityUser { UserName = profile.Email, Email = profile.Email, EmailConfirmed = true };
        var result = await userManager.CreateAsync(user, "Student@1234");
        if (!result.Succeeded)
        {
            foreach (var err in result.Errors) ModelState.AddModelError("", err.Description);
            return View(profile);
        }
        await userManager.AddToRoleAsync(user, "Student");

        profile.IdentityUserId = user.Id;
        context.Add(profile);
        await context.SaveChangesAsync();
        TempData["Success"] = $"Student '{profile.Name}' created. Default password: Student@1234";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin,Faculty")]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var student = await context.StudentProfiles.FindAsync(id);
        return student is null ? NotFound() : View(student);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,Faculty")]
    public async Task<IActionResult> Edit(int id, [Bind("Id,IdentityUserId,Name,Email,Phone,Address,DateOfBirth,StudentNumber")] StudentProfile profile)
    {
        if (id != profile.Id) return NotFound();
        if (!ModelState.IsValid) return View(profile);
        try { context.Update(profile); await context.SaveChangesAsync(); TempData["Success"] = "Student updated."; }
        catch (DbUpdateConcurrencyException) { if (!context.StudentProfiles.Any(s => s.Id == id)) return NotFound(); throw; }
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var student = await context.StudentProfiles.FirstOrDefaultAsync(s => s.Id == id);
        return student is null ? NotFound() : View(student);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var student = await context.StudentProfiles.FindAsync(id);
        if (student is null) return NotFound();
        context.StudentProfiles.Remove(student);
        await context.SaveChangesAsync();
        TempData["Success"] = "Student deleted.";
        return RedirectToAction(nameof(Index));
    }
}
