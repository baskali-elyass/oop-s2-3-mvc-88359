using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VgcCollege.MVC.Data;
using VgcCollege.MVC.Models;

namespace VgcCollege.MVC.Controllers;

[Authorize]
public class DashboardController(ApplicationDbContext context, UserManager<IdentityUser> userManager) : Controller
{
    public async Task<IActionResult> Index()
    {
        var userId = userManager.GetUserId(User)!;

        if (User.IsInRole("Admin"))
        {
            var vm = new AdminDashboardViewModel
            {
                TotalStudents   = await context.StudentProfiles.CountAsync(),
                TotalCourses    = await context.Courses.CountAsync(),
                TotalBranches   = await context.Branches.CountAsync(),
                TotalFaculty    = await context.FacultyProfiles.CountAsync(),
                ActiveEnrolments = await context.CourseEnrolments.CountAsync(e => e.Status == "Active"),
                PendingExamReleases = await context.Exams.CountAsync(e => !e.ResultsReleased),
            };
            return View("AdminDashboard", vm);
        }

        if (User.IsInRole("Faculty"))
        {
            var faculty = await context.FacultyProfiles.FirstOrDefaultAsync(f => f.IdentityUserId == userId);
            if (faculty is null) return View("NoProfile");

            var courseIds = await context.FacultyCourseAssignments
                .Where(a => a.FacultyProfileId == faculty.Id)
                .Select(a => a.CourseId).ToListAsync();

            var vm = new FacultyDashboardViewModel
            {
                FacultyName  = faculty.Name,
                MyCourses    = await context.Courses.Where(c => courseIds.Contains(c.Id)).Include(c => c.Branch).ToListAsync(),
                MyStudentCount = await context.CourseEnrolments.CountAsync(e => courseIds.Contains(e.CourseId) && e.Status == "Active"),
            };
            return View("FacultyDashboard", vm);
        }

        if (User.IsInRole("Student"))
        {
            var student = await context.StudentProfiles.FirstOrDefaultAsync(s => s.IdentityUserId == userId);
            if (student is null) return View("NoProfile");

            var vm = new StudentDashboardViewModel
            {
                StudentName  = student.Name,
                StudentNumber = student.StudentNumber,
                MyEnrolments = await context.CourseEnrolments
                    .Where(e => e.StudentProfileId == student.Id)
                    .Include(e => e.Course).ThenInclude(c => c.Branch)
                    .ToListAsync(),
            };
            return View("StudentDashboard", vm);
        }

        return View("Index");
    }
}
