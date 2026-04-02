using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Domain;

namespace VgcCollege.MVC.Data;

public static class SeedData
{
    public static async Task InitialiseAsync(IServiceProvider services)
    {
        var ctx = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        await ctx.Database.EnsureCreatedAsync();

        foreach (var role in new[] { "Admin", "Faculty", "Student" })
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));

        async Task<IdentityUser> CreateUser(string email, string password, string role)
        {
            var existing = await userManager.FindByEmailAsync(email);
            if (existing is not null) return existing;
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            await userManager.CreateAsync(user, password);
            await userManager.AddToRoleAsync(user, role);
            return user;
        }

        var adminUser    = await CreateUser("admin@vgc.ie",    "Admin@1234",    "Admin");
        var faculty1User = await CreateUser("faculty1@vgc.ie", "Faculty@1234",  "Faculty");
        var faculty2User = await CreateUser("faculty2@vgc.ie", "Faculty2@1234", "Faculty");
        var student1User = await CreateUser("student1@vgc.ie", "Student@1234",  "Student");
        var student2User = await CreateUser("student2@vgc.ie", "Student2@1234", "Student");
        var student3User = await CreateUser("student3@vgc.ie", "Student3@1234", "Student");

        if (await ctx.Branches.AnyAsync()) return;

        // ── Branches ──
        var branches = new[]
        {
            new Branch { Name = "Dublin City Campus",  Address = "12 O'Connell Street, Dublin 1" },
            new Branch { Name = "Cork South Campus",   Address = "5 Patrick's Street, Cork" },
            new Branch { Name = "Galway West Campus",  Address = "3 Shop Street, Galway" },
        };
        ctx.Branches.AddRange(branches);
        await ctx.SaveChangesAsync();

        // ── Courses ──
        var today = DateTime.Today;
        var courses = new[]
        {
            new Course { Name = "BSc Computer Science",    BranchId = branches[0].Id, StartDate = today.AddMonths(-6), EndDate = today.AddMonths(6) },
            new Course { Name = "BSc Business Studies",    BranchId = branches[0].Id, StartDate = today.AddMonths(-4), EndDate = today.AddMonths(8) },
            new Course { Name = "Diploma in Data Science", BranchId = branches[1].Id, StartDate = today.AddMonths(-3), EndDate = today.AddMonths(9) },
            new Course { Name = "HND Software Dev",        BranchId = branches[2].Id, StartDate = today.AddMonths(-5), EndDate = today.AddMonths(7) },
        };
        ctx.Courses.AddRange(courses);
        await ctx.SaveChangesAsync();

        // ── Faculty Profiles ──
        var faculty1 = new FacultyProfile { IdentityUserId = faculty1User.Id, Name = "Dr. Aoife Murphy",   Email = "faculty1@vgc.ie", Phone = "01-2345678" };
        var faculty2 = new FacultyProfile { IdentityUserId = faculty2User.Id, Name = "Mr. Conor O'Brien",  Email = "faculty2@vgc.ie", Phone = "021-9876543" };
        ctx.FacultyProfiles.AddRange(faculty1, faculty2);
        await ctx.SaveChangesAsync();

        // ── Faculty → Course Assignments ──
        ctx.FacultyCourseAssignments.AddRange(
            new FacultyCourseAssignment { FacultyProfileId = faculty1.Id, CourseId = courses[0].Id, IsTutor = true },
            new FacultyCourseAssignment { FacultyProfileId = faculty1.Id, CourseId = courses[1].Id, IsTutor = false },
            new FacultyCourseAssignment { FacultyProfileId = faculty2.Id, CourseId = courses[2].Id, IsTutor = true },
            new FacultyCourseAssignment { FacultyProfileId = faculty2.Id, CourseId = courses[3].Id, IsTutor = true }
        );
        await ctx.SaveChangesAsync();

        // ── Student Profiles ──
        var student1 = new StudentProfile { IdentityUserId = student1User.Id, Name = "Alice Ryan",    Email = "student1@vgc.ie", Phone = "085-1111111", StudentNumber = "VGC-2024-001", Address = "10 Main St, Dublin" };
        var student2 = new StudentProfile { IdentityUserId = student2User.Id, Name = "Brian Doyle",   Email = "student2@vgc.ie", Phone = "086-2222222", StudentNumber = "VGC-2024-002", Address = "5 Park Ave, Cork" };
        var student3 = new StudentProfile { IdentityUserId = student3User.Id, Name = "Ciara Walsh",   Email = "student3@vgc.ie", Phone = "087-3333333", StudentNumber = "VGC-2024-003", Address = "8 Sea Rd, Galway" };
        ctx.StudentProfiles.AddRange(student1, student2, student3);
        await ctx.SaveChangesAsync();

        // ── Enrolments ──
        var enrolments = new[]
        {
            new CourseEnrolment { StudentProfileId = student1.Id, CourseId = courses[0].Id, EnrolDate = today.AddMonths(-6), Status = "Active" },
            new CourseEnrolment { StudentProfileId = student1.Id, CourseId = courses[1].Id, EnrolDate = today.AddMonths(-4), Status = "Active" },
            new CourseEnrolment { StudentProfileId = student2.Id, CourseId = courses[0].Id, EnrolDate = today.AddMonths(-6), Status = "Active" },
            new CourseEnrolment { StudentProfileId = student2.Id, CourseId = courses[2].Id, EnrolDate = today.AddMonths(-3), Status = "Active" },
            new CourseEnrolment { StudentProfileId = student3.Id, CourseId = courses[3].Id, EnrolDate = today.AddMonths(-5), Status = "Active" },
            new CourseEnrolment { StudentProfileId = student3.Id, CourseId = courses[2].Id, EnrolDate = today.AddMonths(-3), Status = "Active" },
        };
        ctx.CourseEnrolments.AddRange(enrolments);
        await ctx.SaveChangesAsync();

        // ── Attendance ──
        var attendanceRecords = new List<AttendanceRecord>();
        foreach (var enrolment in enrolments)
        {
            for (int week = 1; week <= 8; week++)
            {
                attendanceRecords.Add(new AttendanceRecord
                {
                    CourseEnrolmentId = enrolment.Id,
                    WeekNumber = week,
                    SessionDate = today.AddMonths(-6).AddDays(week * 7),
                    Present = week != 3 && week != 6, // miss weeks 3 & 6
                    Notes = (week == 3 || week == 6) ? "Absent" : null
                });
            }
        }
        ctx.AttendanceRecords.AddRange(attendanceRecords);
        await ctx.SaveChangesAsync();

        // ── Assignments ──
        var assignments = new[]
        {
            new Assignment { CourseId = courses[0].Id, Title = "CA1 - Database Design",     MaxScore = 100, DueDate = today.AddMonths(-4) },
            new Assignment { CourseId = courses[0].Id, Title = "CA2 - Web Application",     MaxScore = 100, DueDate = today.AddMonths(-2) },
            new Assignment { CourseId = courses[1].Id, Title = "CA1 - Business Plan",        MaxScore = 100, DueDate = today.AddMonths(-3) },
            new Assignment { CourseId = courses[2].Id, Title = "CA1 - Data Analysis Report", MaxScore = 100, DueDate = today.AddMonths(-2) },
            new Assignment { CourseId = courses[3].Id, Title = "CA1 - Software Prototype",   MaxScore = 100, DueDate = today.AddMonths(-3) },
        };
        ctx.Assignments.AddRange(assignments);
        await ctx.SaveChangesAsync();

        // ── Assignment Results ──
        ctx.AssignmentResults.AddRange(
            new AssignmentResult { AssignmentId = assignments[0].Id, StudentProfileId = student1.Id, Score = 72, Feedback = "Good work, improve normalisation." },
            new AssignmentResult { AssignmentId = assignments[0].Id, StudentProfileId = student2.Id, Score = 85, Feedback = "Excellent ER diagram." },
            new AssignmentResult { AssignmentId = assignments[1].Id, StudentProfileId = student1.Id, Score = 68, Feedback = "Functional but needs better validation." },
            new AssignmentResult { AssignmentId = assignments[1].Id, StudentProfileId = student2.Id, Score = 91, Feedback = "Outstanding effort." },
            new AssignmentResult { AssignmentId = assignments[2].Id, StudentProfileId = student1.Id, Score = 78, Feedback = "Well structured plan." },
            new AssignmentResult { AssignmentId = assignments[3].Id, StudentProfileId = student2.Id, Score = 65, Feedback = "Needs more statistical depth." },
            new AssignmentResult { AssignmentId = assignments[3].Id, StudentProfileId = student3.Id, Score = 80, Feedback = "Good analysis overall." },
            new AssignmentResult { AssignmentId = assignments[4].Id, StudentProfileId = student3.Id, Score = 74, Feedback = "Working prototype, improve UI." }
        );
        await ctx.SaveChangesAsync();

        // ── Exams ──
        var exams = new[]
        {
            new Exam { CourseId = courses[0].Id, Title = "End of Semester Exam",    Date = today.AddMonths(-1), MaxScore = 100, ResultsReleased = true },
            new Exam { CourseId = courses[0].Id, Title = "Supplemental Exam",       Date = today.AddDays(-14),  MaxScore = 100, ResultsReleased = false },
            new Exam { CourseId = courses[1].Id, Title = "Business Strategy Exam",  Date = today.AddMonths(-1), MaxScore = 100, ResultsReleased = true },
            new Exam { CourseId = courses[2].Id, Title = "Data Science Final Exam", Date = today.AddDays(-7),   MaxScore = 100, ResultsReleased = false },
            new Exam { CourseId = courses[3].Id, Title = "Software Dev Exam",       Date = today.AddMonths(-2), MaxScore = 100, ResultsReleased = true },
        };
        ctx.Exams.AddRange(exams);
        await ctx.SaveChangesAsync();

        // ── Exam Results ──
        ctx.ExamResults.AddRange(
            new ExamResult { ExamId = exams[0].Id, StudentProfileId = student1.Id, Score = 70, Grade = "B2" },
            new ExamResult { ExamId = exams[0].Id, StudentProfileId = student2.Id, Score = 82, Grade = "A2" },
            new ExamResult { ExamId = exams[1].Id, StudentProfileId = student1.Id, Score = 55, Grade = "C3" },
            new ExamResult { ExamId = exams[1].Id, StudentProfileId = student2.Id, Score = 61, Grade = "C1" },
            new ExamResult { ExamId = exams[2].Id, StudentProfileId = student1.Id, Score = 75, Grade = "B1" },
            new ExamResult { ExamId = exams[3].Id, StudentProfileId = student2.Id, Score = 67, Grade = "C1" },
            new ExamResult { ExamId = exams[3].Id, StudentProfileId = student3.Id, Score = 78, Grade = "B2" },
            new ExamResult { ExamId = exams[4].Id, StudentProfileId = student3.Id, Score = 88, Grade = "A2" }
        );
        await ctx.SaveChangesAsync();
    }
}
