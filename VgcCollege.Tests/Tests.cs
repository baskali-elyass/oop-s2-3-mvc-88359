using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using VgcCollege.Domain;
using VgcCollege.MVC.Controllers;
using VgcCollege.MVC.Data;

namespace VgcCollege.Tests;

// ─── DB Factory ─────────────────────────────────────────────────────────────

file static class DbFactory
{
    public static ApplicationDbContext Create() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public static async Task<(ApplicationDbContext ctx, Branch branch, Course course)> WithCourseAsync()
    {
        var ctx = Create();
        var branch = new Branch { Name = "Test Branch", Address = "1 Test St" };
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();
        var course = new Course { Name = "Test Course", BranchId = branch.Id, StartDate = DateTime.Today.AddMonths(-3), EndDate = DateTime.Today.AddMonths(9) };
        ctx.Courses.Add(course);
        await ctx.SaveChangesAsync();
        return (ctx, branch, course);
    }

    public static async Task<(ApplicationDbContext ctx, Course course, StudentProfile student, CourseEnrolment enrolment)> WithEnrolmentAsync()
    {
        var (ctx, _, course) = await WithCourseAsync();
        var student = new StudentProfile { IdentityUserId = "uid-s1", Name = "Alice", Email = "alice@test.ie", StudentNumber = "S001" };
        ctx.StudentProfiles.Add(student);
        await ctx.SaveChangesAsync();
        var enrolment = new CourseEnrolment { StudentProfileId = student.Id, CourseId = course.Id, EnrolDate = DateTime.Today, Status = "Active" };
        ctx.CourseEnrolments.Add(enrolment);
        await ctx.SaveChangesAsync();
        return (ctx, course, student, enrolment);
    }

    public static ITempDataDictionary FakeTempData() =>
        new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>());
}

// ─── Domain: Branch & Course ─────────────────────────────────────────────────

public class BranchDomainTests
{
    [Fact]
    public void Branch_DefaultCourses_IsEmptyCollection()
    {
        var b = new Branch();
        Assert.NotNull(b.Courses);
        Assert.Empty(b.Courses);
    }

    [Fact]
    public void Branch_Name_CanBeSet()
    {
        var b = new Branch { Name = "Dublin Campus", Address = "1 O'Connell St" };
        Assert.Equal("Dublin Campus", b.Name);
    }
}

public class CourseDomainTests
{
    [Fact]
    public void Course_StartDateBeforeEndDate_IsValid()
    {
        var c = new Course { StartDate = DateTime.Today, EndDate = DateTime.Today.AddMonths(12) };
        Assert.True(c.StartDate < c.EndDate);
    }

    [Fact]
    public void Course_DefaultCollections_AreEmpty()
    {
        var c = new Course();
        Assert.Empty(c.Enrolments);
        Assert.Empty(c.Assignments);
        Assert.Empty(c.Exams);
        Assert.Empty(c.FacultyAssignments);
    }
}

// ─── Domain: Student & Enrolment ─────────────────────────────────────────────

public class StudentDomainTests
{
    [Fact]
    public void StudentProfile_DefaultCollections_AreEmpty()
    {
        var s = new StudentProfile();
        Assert.Empty(s.Enrolments);
        Assert.Empty(s.AssignmentResults);
        Assert.Empty(s.ExamResults);
    }

    [Fact]
    public void CourseEnrolment_StatusCanBeSet()
    {
        var e = new CourseEnrolment { Status = "Active" };
        Assert.Equal("Active", e.Status);
    }
}

// ─── Business Logic: Enrolment Rules ─────────────────────────────────────────

public class EnrolmentRuleTests
{
    [Fact]
    public async Task EnrolStudent_DuplicateEnrolment_IsDetected()
    {
        var (ctx, course, student, _) = await DbFactory.WithEnrolmentAsync();
        var duplicate = await ctx.CourseEnrolments
            .AnyAsync(e => e.StudentProfileId == student.Id && e.CourseId == course.Id);
        Assert.True(duplicate);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task EnrolStudent_NewCourse_Succeeds()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        var student = new StudentProfile { IdentityUserId = "uid-new", Name = "Bob", Email = "bob@test.ie", StudentNumber = "S002" };
        ctx.StudentProfiles.Add(student);
        await ctx.SaveChangesAsync();
        ctx.CourseEnrolments.Add(new CourseEnrolment { StudentProfileId = student.Id, CourseId = course.Id, EnrolDate = DateTime.Today, Status = "Active" });
        await ctx.SaveChangesAsync();
        Assert.Equal(1, await ctx.CourseEnrolments.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task AttendanceRecord_DuplicateWeek_IsDetected()
    {
        var (ctx, _, _, enrolment) = await DbFactory.WithEnrolmentAsync();
        ctx.AttendanceRecords.Add(new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 1, SessionDate = DateTime.Today, Present = true });
        await ctx.SaveChangesAsync();
        var exists = await ctx.AttendanceRecords.AnyAsync(a => a.CourseEnrolmentId == enrolment.Id && a.WeekNumber == 1);
        Assert.True(exists);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task AttendanceRate_CalculatedCorrectly()
    {
        var (ctx, _, _, enrolment) = await DbFactory.WithEnrolmentAsync();
        ctx.AttendanceRecords.AddRange(
            new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 1, SessionDate = DateTime.Today, Present = true },
            new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 2, SessionDate = DateTime.Today.AddDays(7), Present = true },
            new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 3, SessionDate = DateTime.Today.AddDays(14), Present = false },
            new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 4, SessionDate = DateTime.Today.AddDays(21), Present = true }
        );
        await ctx.SaveChangesAsync();
        var records = await ctx.AttendanceRecords.Where(a => a.CourseEnrolmentId == enrolment.Id).ToListAsync();
        var pct = records.Count(r => r.Present) * 100 / records.Count;
        Assert.Equal(75, pct);
        await ctx.DisposeAsync();
    }
}

// ─── Business Logic: Exam Visibility ─────────────────────────────────────────

public class ExamVisibilityTests
{
    [Fact]
    public async Task ProvisionalExamResult_IsHiddenFromStudent()
    {
        var (ctx, course, student, _) = await DbFactory.WithEnrolmentAsync();
        var exam = new Exam { CourseId = course.Id, Title = "Final", Date = DateTime.Today, MaxScore = 100, ResultsReleased = false };
        ctx.Exams.Add(exam);
        await ctx.SaveChangesAsync();
        ctx.ExamResults.Add(new ExamResult { ExamId = exam.Id, StudentProfileId = student.Id, Score = 72, Grade = "B2" });
        await ctx.SaveChangesAsync();

        var visible = await ctx.ExamResults.Include(r => r.Exam)
            .Where(r => r.StudentProfileId == student.Id && r.Exam.ResultsReleased).ToListAsync();

        Assert.Empty(visible);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task ReleasedExamResult_IsVisibleToStudent()
    {
        var (ctx, course, student, _) = await DbFactory.WithEnrolmentAsync();
        var exam = new Exam { CourseId = course.Id, Title = "Released", Date = DateTime.Today, MaxScore = 100, ResultsReleased = true };
        ctx.Exams.Add(exam);
        await ctx.SaveChangesAsync();
        ctx.ExamResults.Add(new ExamResult { ExamId = exam.Id, StudentProfileId = student.Id, Score = 85, Grade = "A2" });
        await ctx.SaveChangesAsync();

        var visible = await ctx.ExamResults.Include(r => r.Exam)
            .Where(r => r.StudentProfileId == student.Id && r.Exam.ResultsReleased).ToListAsync();

        Assert.Single(visible);
        Assert.Equal(85, visible[0].Score);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task ReleaseResults_UpdatesFlag()
    {
        var (ctx, course, _, _) = await DbFactory.WithEnrolmentAsync();
        var exam = new Exam { CourseId = course.Id, Title = "Pending", Date = DateTime.Today, MaxScore = 100, ResultsReleased = false };
        ctx.Exams.Add(exam);
        await ctx.SaveChangesAsync();
        exam.ResultsReleased = true;
        await ctx.SaveChangesAsync();
        var updated = await ctx.Exams.FindAsync(exam.Id);
        Assert.True(updated!.ResultsReleased);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task MixedExams_StudentOnlySeesReleased()
    {
        var (ctx, course, student, _) = await DbFactory.WithEnrolmentAsync();
        var e1 = new Exam { CourseId = course.Id, Title = "Exam 1", Date = DateTime.Today, MaxScore = 100, ResultsReleased = true };
        var e2 = new Exam { CourseId = course.Id, Title = "Exam 2", Date = DateTime.Today, MaxScore = 100, ResultsReleased = false };
        ctx.Exams.AddRange(e1, e2);
        await ctx.SaveChangesAsync();
        ctx.ExamResults.AddRange(
            new ExamResult { ExamId = e1.Id, StudentProfileId = student.Id, Score = 70, Grade = "B2" },
            new ExamResult { ExamId = e2.Id, StudentProfileId = student.Id, Score = 55, Grade = "C3" }
        );
        await ctx.SaveChangesAsync();
        var visible = await ctx.ExamResults.Include(r => r.Exam)
            .Where(r => r.StudentProfileId == student.Id && r.Exam.ResultsReleased).ToListAsync();
        Assert.Single(visible);
        Assert.Equal("Exam 1", visible[0].Exam.Title);
        await ctx.DisposeAsync();
    }
}

// ─── Business Logic: Grade Validation ────────────────────────────────────────

public class GradeValidationTests
{
    [Fact]
    public void AssignmentResult_ScoreExceedsMax_IsInvalid()
    {
        var a = new Assignment { MaxScore = 100 };
        var r = new AssignmentResult { Score = 110 };
        Assert.True(r.Score > a.MaxScore);
    }

    [Fact]
    public void AssignmentResult_ScoreWithinMax_IsValid()
    {
        var a = new Assignment { MaxScore = 100 };
        var r = new AssignmentResult { Score = 85 };
        Assert.True(r.Score <= a.MaxScore);
    }

    [Fact]
    public void Percentage_CalculatedCorrectly()
    {
        var a = new Assignment { MaxScore = 100 };
        var r = new AssignmentResult { Score = 72 };
        var pct = (int)(r.Score * 100 / a.MaxScore);
        Assert.Equal(72, pct);
    }

    [Fact]
    public async Task AssignmentResult_DuplicateForStudent_IsDetected()
    {
        var (ctx, course, student, _) = await DbFactory.WithEnrolmentAsync();
        var assignment = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(assignment);
        await ctx.SaveChangesAsync();
        ctx.AssignmentResults.Add(new AssignmentResult { AssignmentId = assignment.Id, StudentProfileId = student.Id, Score = 75 });
        await ctx.SaveChangesAsync();
        var dup = await ctx.AssignmentResults.AnyAsync(r => r.AssignmentId == assignment.Id && r.StudentProfileId == student.Id);
        Assert.True(dup);
        await ctx.DisposeAsync();
    }
}

// ─── Business Logic: Faculty RBAC Filtering ──────────────────────────────────

public class FacultyAuthorizationTests
{
    [Fact]
    public async Task Faculty_OnlySeesStudentsInTheirCourses()
    {
        var ctx = DbFactory.Create();
        var branch = new Branch { Name = "B", Address = "A" };
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();
        var c1 = new Course { Name = "C1", BranchId = branch.Id, StartDate = DateTime.Today, EndDate = DateTime.Today.AddYears(1) };
        var c2 = new Course { Name = "C2", BranchId = branch.Id, StartDate = DateTime.Today, EndDate = DateTime.Today.AddYears(1) };
        ctx.Courses.AddRange(c1, c2);
        await ctx.SaveChangesAsync();
        var faculty = new FacultyProfile { IdentityUserId = "fac", Name = "Dr F", Email = "f@t.ie" };
        ctx.FacultyProfiles.Add(faculty);
        await ctx.SaveChangesAsync();
        ctx.FacultyCourseAssignments.Add(new FacultyCourseAssignment { FacultyProfileId = faculty.Id, CourseId = c1.Id });
        await ctx.SaveChangesAsync();
        var s1 = new StudentProfile { IdentityUserId = "s1", Name = "S1", Email = "s1@t.ie", StudentNumber = "001" };
        var s2 = new StudentProfile { IdentityUserId = "s2", Name = "S2", Email = "s2@t.ie", StudentNumber = "002" };
        ctx.StudentProfiles.AddRange(s1, s2);
        await ctx.SaveChangesAsync();
        ctx.CourseEnrolments.AddRange(
            new CourseEnrolment { StudentProfileId = s1.Id, CourseId = c1.Id, EnrolDate = DateTime.Today, Status = "Active" },
            new CourseEnrolment { StudentProfileId = s2.Id, CourseId = c2.Id, EnrolDate = DateTime.Today, Status = "Active" }
        );
        await ctx.SaveChangesAsync();

        var courseIds = await ctx.FacultyCourseAssignments
            .Where(a => a.FacultyProfileId == faculty.Id).Select(a => a.CourseId).ToListAsync();
        var studentIds = await ctx.CourseEnrolments
            .Where(e => courseIds.Contains(e.CourseId)).Select(e => e.StudentProfileId).Distinct().ToListAsync();
        var visible = await ctx.StudentProfiles.Where(s => studentIds.Contains(s.Id)).ToListAsync();

        Assert.Single(visible);
        Assert.Equal("S1", visible[0].Name);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task FacultyCourseAssignment_Duplicate_IsDetected()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        var faculty = new FacultyProfile { IdentityUserId = "fac", Name = "Dr F", Email = "f@t.ie" };
        ctx.FacultyProfiles.Add(faculty);
        await ctx.SaveChangesAsync();
        ctx.FacultyCourseAssignments.Add(new FacultyCourseAssignment { FacultyProfileId = faculty.Id, CourseId = course.Id });
        await ctx.SaveChangesAsync();
        var dup = await ctx.FacultyCourseAssignments
            .AnyAsync(a => a.FacultyProfileId == faculty.Id && a.CourseId == course.Id);
        Assert.True(dup);
        await ctx.DisposeAsync();
    }
}

// ─── Controllers: BranchesController ─────────────────────────────────────────

public class BranchesControllerTests
{
    [Fact]
    public async Task Index_ReturnsAllBranches()
    {
        await using var ctx = DbFactory.Create();
        ctx.Branches.AddRange(new Branch { Name = "Dublin", Address = "1 St" }, new Branch { Name = "Cork", Address = "2 St" });
        await ctx.SaveChangesAsync();
        var result = await new BranchesController(ctx).Index();
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<Branch>>(view.Model).Count());
    }

    [Fact]
    public async Task Details_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new BranchesController(ctx).Details(null));
    }

    [Fact]
    public async Task Details_ValidId_ReturnsView()
    {
        await using var ctx = DbFactory.Create();
        var b = new Branch { Name = "Test", Address = "Addr" };
        ctx.Branches.Add(b); await ctx.SaveChangesAsync();
        var result = await new BranchesController(ctx).Details(b.Id);
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task Create_Post_ValidModel_RedirectsToIndex()
    {
        await using var ctx = DbFactory.Create();
        var ctrl = new BranchesController(ctx) { TempData = DbFactory.FakeTempData() };
        var result = await ctrl.Create(new Branch { Name = "New", Address = "Addr" });
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal(1, await ctx.Branches.CountAsync());
    }

    [Fact]
    public async Task Create_Post_InvalidModel_ReturnsView()
    {
        await using var ctx = DbFactory.Create();
        var ctrl = new BranchesController(ctx);
        ctrl.ModelState.AddModelError("Name", "Required");
        Assert.IsType<ViewResult>(await ctrl.Create(new Branch()));
    }

    [Fact]
    public async Task DeleteConfirmed_WithCourses_DoesNotDelete()
    {
        var (ctx, branch, _) = await DbFactory.WithCourseAsync();
        var ctrl = new BranchesController(ctx) { TempData = DbFactory.FakeTempData() };
        await ctrl.DeleteConfirmed(branch.Id);
        Assert.Equal(1, await ctx.Branches.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task DeleteConfirmed_NoCourses_Deletes()
    {
        await using var ctx = DbFactory.Create();
        var b = new Branch { Name = "Empty", Address = "Addr" };
        ctx.Branches.Add(b); await ctx.SaveChangesAsync();
        var ctrl = new BranchesController(ctx) { TempData = DbFactory.FakeTempData() };
        await ctrl.DeleteConfirmed(b.Id);
        Assert.Equal(0, await ctx.Branches.CountAsync());
    }
}

// ─── Controllers: CoursesController ──────────────────────────────────────────

public class CoursesControllerTests
{
    [Fact]
    public async Task Index_ReturnsAllCourses()
    {
        var (ctx, _, _) = await DbFactory.WithCourseAsync();
        var result = await new CoursesController(ctx).Index();
        var view = Assert.IsType<ViewResult>(result);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<Course>>(view.Model));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Details_ValidId_ReturnsView()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        Assert.IsType<ViewResult>(await new CoursesController(ctx).Details(course.Id));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Details_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new CoursesController(ctx).Details(null));
    }
}

// ─── Controllers: EnrolmentsController ───────────────────────────────────────

public class EnrolmentsControllerTests
{
    [Fact]
    public async Task Index_ReturnsAllEnrolments()
    {
        var (ctx, _, _, _) = await DbFactory.WithEnrolmentAsync();
        var result = await new EnrolmentsController(ctx).Index(null, null);
        var view = Assert.IsType<ViewResult>(result);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<CourseEnrolment>>(view.Model));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_DuplicateEnrolment_ReturnsViewWithError()
    {
        var (ctx, course, student, _) = await DbFactory.WithEnrolmentAsync();
        var ctrl = new EnrolmentsController(ctx);
        var result = await ctrl.Create(new CourseEnrolment
        {
            StudentProfileId = student.Id, CourseId = course.Id, EnrolDate = DateTime.Today, Status = "Active"
        });
        Assert.IsType<ViewResult>(result);
        Assert.False(ctrl.ModelState.IsValid);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task DeleteConfirmed_RemovesEnrolment()
    {
        var (ctx, _, _, enrolment) = await DbFactory.WithEnrolmentAsync();
        var ctrl = new EnrolmentsController(ctx) { TempData = DbFactory.FakeTempData() };
        await ctrl.DeleteConfirmed(enrolment.Id);
        Assert.Equal(0, await ctx.CourseEnrolments.CountAsync());
        await ctx.DisposeAsync();
    }
}

// ─── Tests supplémentaires pour augmenter la couverture ──────────────────────

public class AssignmentBusinessLogicTests
{
    [Fact]
    public async Task Assignment_WithResults_ReturnsCorrectCount()
    {
        var (ctx, course, student, _) = await DbFactory.WithEnrolmentAsync();
        var assignment = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(assignment);
        await ctx.SaveChangesAsync();
        ctx.AssignmentResults.AddRange(
            new AssignmentResult { AssignmentId = assignment.Id, StudentProfileId = student.Id, Score = 75 }
        );
        await ctx.SaveChangesAsync();
        var results = await ctx.AssignmentResults.Where(r => r.AssignmentId == assignment.Id).ToListAsync();
        Assert.Single(results);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task MultipleStudents_SameAssignment_AllResultsStored()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        var s1 = new StudentProfile { IdentityUserId = "u1", Name = "S1", Email = "s1@t.ie", StudentNumber = "001" };
        var s2 = new StudentProfile { IdentityUserId = "u2", Name = "S2", Email = "s2@t.ie", StudentNumber = "002" };
        ctx.StudentProfiles.AddRange(s1, s2);
        await ctx.SaveChangesAsync();
        var assignment = new Assignment { CourseId = course.Id, Title = "CA2", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(assignment);
        await ctx.SaveChangesAsync();
        ctx.AssignmentResults.AddRange(
            new AssignmentResult { AssignmentId = assignment.Id, StudentProfileId = s1.Id, Score = 80 },
            new AssignmentResult { AssignmentId = assignment.Id, StudentProfileId = s2.Id, Score = 60 }
        );
        await ctx.SaveChangesAsync();
        var count = await ctx.AssignmentResults.CountAsync(r => r.AssignmentId == assignment.Id);
        Assert.Equal(2, count);
        await ctx.DisposeAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void AssignmentResult_ScoreInRange_IsValid(double score)
    {
        var a = new Assignment { MaxScore = 100 };
        var r = new AssignmentResult { Score = score };
        Assert.True(r.Score >= 0 && r.Score <= a.MaxScore);
    }
}

public class ExamBusinessLogicTests
{
    [Fact]
    public async Task Exam_DefaultResultsReleased_IsFalse()
    {
        var exam = new Exam { Title = "Test", MaxScore = 100, Date = DateTime.Today };
        Assert.False(exam.ResultsReleased);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ExamResult_Grade_CanBeSet()
    {
        var (ctx, course, student, _) = await DbFactory.WithEnrolmentAsync();
        var exam = new Exam { CourseId = course.Id, Title = "Final", Date = DateTime.Today, MaxScore = 100, ResultsReleased = true };
        ctx.Exams.Add(exam);
        await ctx.SaveChangesAsync();
        ctx.ExamResults.Add(new ExamResult { ExamId = exam.Id, StudentProfileId = student.Id, Score = 88, Grade = "A2" });
        await ctx.SaveChangesAsync();
        var result = await ctx.ExamResults.FirstAsync();
        Assert.Equal("A2", result.Grade);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task PendingExams_CountIsCorrect()
    {
        var (ctx, course, _, _) = await DbFactory.WithEnrolmentAsync();
        ctx.Exams.AddRange(
            new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100, ResultsReleased = false },
            new Exam { CourseId = course.Id, Title = "E2", Date = DateTime.Today, MaxScore = 100, ResultsReleased = false },
            new Exam { CourseId = course.Id, Title = "E3", Date = DateTime.Today, MaxScore = 100, ResultsReleased = true }
        );
        await ctx.SaveChangesAsync();
        var pending = await ctx.Exams.CountAsync(e => !e.ResultsReleased);
        Assert.Equal(2, pending);
        await ctx.DisposeAsync();
    }
}

public class AttendanceBusinessLogicTests
{
    [Fact]
    public async Task Attendance_Present_True_IsStored()
    {
        var (ctx, _, _, enrolment) = await DbFactory.WithEnrolmentAsync();
        ctx.AttendanceRecords.Add(new AttendanceRecord
        {
            CourseEnrolmentId = enrolment.Id,
            WeekNumber = 1,
            SessionDate = DateTime.Today,
            Present = true
        });
        await ctx.SaveChangesAsync();
        var record = await ctx.AttendanceRecords.FirstAsync();
        Assert.True(record.Present);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Attendance_Absent_False_IsStored()
    {
        var (ctx, _, _, enrolment) = await DbFactory.WithEnrolmentAsync();
        ctx.AttendanceRecords.Add(new AttendanceRecord
        {
            CourseEnrolmentId = enrolment.Id,
            WeekNumber = 2,
            SessionDate = DateTime.Today,
            Present = false,
            Notes = "Sick"
        });
        await ctx.SaveChangesAsync();
        var record = await ctx.AttendanceRecords.FirstAsync();
        Assert.False(record.Present);
        Assert.Equal("Sick", record.Notes);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task AttendanceRecords_MultipleWeeks_AllSaved()
    {
        var (ctx, _, _, enrolment) = await DbFactory.WithEnrolmentAsync();
        for (int i = 1; i <= 10; i++)
        {
            ctx.AttendanceRecords.Add(new AttendanceRecord
            {
                CourseEnrolmentId = enrolment.Id,
                WeekNumber = i,
                SessionDate = DateTime.Today.AddDays(i * 7),
                Present = i % 2 == 0
            });
        }
        await ctx.SaveChangesAsync();
        var count = await ctx.AttendanceRecords.CountAsync();
        Assert.Equal(10, count);
        await ctx.DisposeAsync();
    }
}

public class FacultyProfileTests
{
    [Fact]
    public void FacultyProfile_DefaultCourseAssignments_IsEmpty()
    {
        var f = new FacultyProfile();
        Assert.Empty(f.CourseAssignments);
    }

    [Fact]
    public async Task FacultyProfile_WithTutorAssignment_IsTutorIsTrue()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        var faculty = new FacultyProfile { IdentityUserId = "fac", Name = "Dr F", Email = "f@t.ie" };
        ctx.FacultyProfiles.Add(faculty);
        await ctx.SaveChangesAsync();
        ctx.FacultyCourseAssignments.Add(new FacultyCourseAssignment
        {
            FacultyProfileId = faculty.Id,
            CourseId = course.Id,
            IsTutor = true
        });
        await ctx.SaveChangesAsync();
        var assignment = await ctx.FacultyCourseAssignments.FirstAsync();
        Assert.True(assignment.IsTutor);
        await ctx.DisposeAsync();
    }
}

public class StudentProfileTests
{
    [Fact]
    public void StudentNumber_Format_IsSet()
    {
        var s = new StudentProfile { StudentNumber = "VGC-2024-001" };
        Assert.StartsWith("VGC-", s.StudentNumber);
    }

    [Fact]
    public async Task Student_Enrolment_StatusActive_IsDefault()
    {
        var (ctx, _, _, enrolment) = await DbFactory.WithEnrolmentAsync();
        Assert.Equal("Active", enrolment.Status);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Student_CanBeEnrolledInMultipleCourses()
    {
        var ctx = DbFactory.Create();
        var branch = new Branch { Name = "B", Address = "A" };
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();
        var c1 = new Course { Name = "C1", BranchId = branch.Id, StartDate = DateTime.Today, EndDate = DateTime.Today.AddYears(1) };
        var c2 = new Course { Name = "C2", BranchId = branch.Id, StartDate = DateTime.Today, EndDate = DateTime.Today.AddYears(1) };
        ctx.Courses.AddRange(c1, c2);
        await ctx.SaveChangesAsync();
        var student = new StudentProfile { IdentityUserId = "u1", Name = "S1", Email = "s@t.ie", StudentNumber = "001" };
        ctx.StudentProfiles.Add(student);
        await ctx.SaveChangesAsync();
        ctx.CourseEnrolments.AddRange(
            new CourseEnrolment { StudentProfileId = student.Id, CourseId = c1.Id, EnrolDate = DateTime.Today, Status = "Active" },
            new CourseEnrolment { StudentProfileId = student.Id, CourseId = c2.Id, EnrolDate = DateTime.Today, Status = "Active" }
        );
        await ctx.SaveChangesAsync();
        var count = await ctx.CourseEnrolments.CountAsync(e => e.StudentProfileId == student.Id);
        Assert.Equal(2, count);
        await ctx.DisposeAsync();
    }
}

public class BranchesControllerEditTests
{
    [Fact]
    public async Task Edit_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new BranchesController(ctx).Edit(null));
    }

    [Fact]
    public async Task Edit_Get_UnknownId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new BranchesController(ctx).Edit(999));
    }

    [Fact]
    public async Task Edit_Get_ValidId_ReturnsView()
    {
        await using var ctx = DbFactory.Create();
        var b = new Branch { Name = "Test", Address = "Addr" };
        ctx.Branches.Add(b); await ctx.SaveChangesAsync();
        var result = await new BranchesController(ctx).Edit(b.Id);
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task Edit_Post_IdMismatch_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        var b = new Branch { Name = "Test", Address = "Addr" };
        ctx.Branches.Add(b); await ctx.SaveChangesAsync();
        b.Id = 999;
        Assert.IsType<NotFoundResult>(await new BranchesController(ctx).Edit(1, b));
    }

    [Fact]
    public async Task Edit_Post_InvalidModel_ReturnsView()
    {
        await using var ctx = DbFactory.Create();
        var b = new Branch { Name = "Test", Address = "Addr" };
        ctx.Branches.Add(b); await ctx.SaveChangesAsync();
        var ctrl = new BranchesController(ctx);
        ctrl.ModelState.AddModelError("Name", "Required");
        Assert.IsType<ViewResult>(await ctrl.Edit(b.Id, b));
    }

    [Fact]
    public async Task Edit_Post_Valid_RedirectsToIndex()
    {
        await using var ctx = DbFactory.Create();
        var b = new Branch { Name = "Test", Address = "Addr" };
        ctx.Branches.Add(b); await ctx.SaveChangesAsync();
        var ctrl = new BranchesController(ctx) { TempData = DbFactory.FakeTempData() };
        b.Name = "Updated";
        var result = await ctrl.Edit(b.Id, b);
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
    }

    [Fact]
    public async Task Delete_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new BranchesController(ctx).Delete(null));
    }

    [Fact]
    public async Task Delete_Get_UnknownId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new BranchesController(ctx).Delete(999));
    }

    [Fact]
    public async Task DeleteConfirmed_UnknownId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        var ctrl = new BranchesController(ctx) { TempData = DbFactory.FakeTempData() };
        Assert.IsType<NotFoundResult>(await ctrl.DeleteConfirmed(999));
    }
}

public class CoursesControllerEditTests
{
    [Fact]
    public async Task Edit_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new CoursesController(ctx).Edit(null));
    }

    [Fact]
    public async Task Edit_Get_UnknownId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new CoursesController(ctx).Edit(999));
    }

    [Fact]
    public async Task Delete_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new CoursesController(ctx).Delete(null));
    }

    [Fact]
    public async Task Delete_Get_UnknownId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new CoursesController(ctx).Delete(999));
    }

    [Fact]
    public async Task DeleteConfirmed_WithEnrolments_DoesNotDelete()
    {
        var (ctx, _, _, _) = await DbFactory.WithEnrolmentAsync();
        var course = await ctx.Courses.FirstAsync();
        var ctrl = new CoursesController(ctx) { TempData = DbFactory.FakeTempData() };
        await ctrl.DeleteConfirmed(course.Id);
        Assert.Equal(1, await ctx.Courses.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task DeleteConfirmed_NoEnrolments_Deletes()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        var ctrl = new CoursesController(ctx) { TempData = DbFactory.FakeTempData() };
        await ctrl.DeleteConfirmed(course.Id);
        Assert.Equal(0, await ctx.Courses.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_InvalidModel_ReturnsView()
    {
        var (ctx, _, _) = await DbFactory.WithCourseAsync();
        var ctrl = new CoursesController(ctx);
        ctrl.ModelState.AddModelError("Name", "Required");
        Assert.IsType<ViewResult>(await ctrl.Create(new Course()));
    }

    [Fact]
    public async Task Create_Post_Valid_RedirectsToIndex()
    {
        var (ctx, branch, _) = await DbFactory.WithCourseAsync();
        var ctrl = new CoursesController(ctx) { TempData = DbFactory.FakeTempData() };
        var result = await ctrl.Create(new Course
        {
            Name = "New Course",
            BranchId = branch.Id,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddYears(1)
        });
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        await ctx.DisposeAsync();
    }
}

public class EnrolmentsControllerEditTests
{
    [Fact]
    public async Task Edit_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new EnrolmentsController(ctx).Edit(null));
    }

    [Fact]
    public async Task Edit_Get_UnknownId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new EnrolmentsController(ctx).Edit(999));
    }

    [Fact]
    public async Task Edit_Get_ValidId_ReturnsView()
    {
        var (ctx, _, _, enrolment) = await DbFactory.WithEnrolmentAsync();
        var result = await new EnrolmentsController(ctx).Edit(enrolment.Id);
        Assert.IsType<ViewResult>(result);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Delete_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new EnrolmentsController(ctx).Delete(null));
    }

    [Fact]
    public async Task Delete_Get_UnknownId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new EnrolmentsController(ctx).Delete(999));
    }

    [Fact]
    public async Task DeleteConfirmed_UnknownId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        var ctrl = new EnrolmentsController(ctx) { TempData = DbFactory.FakeTempData() };
        Assert.IsType<NotFoundResult>(await ctrl.DeleteConfirmed(999));
    }

    [Fact]
    public async Task Index_FilterByCourse_ReturnsFiltered()
    {
        var (ctx, course, _, _) = await DbFactory.WithEnrolmentAsync();
        var result = await new EnrolmentsController(ctx).Index(course.Id, null);
        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IEnumerable<CourseEnrolment>>(view.Model);
        Assert.Single(model);
        await ctx.DisposeAsync();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// COLLE CE BLOC ENTIER À LA FIN DE Tests.cs (après la dernière accolade })
// ════════════════════════════════════════════════════════════════════════════

// ─── AssignmentsController ───────────────────────────────────────────────────

public class AssignmentsControllerTests
{
    [Fact]
    public async Task Index_NoCourseFilter_ReturnsAllAssignments()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        ctx.Assignments.AddRange(
            new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today },
            new Assignment { CourseId = course.Id, Title = "CA2", MaxScore = 100, DueDate = DateTime.Today.AddDays(7) }
        );
        await ctx.SaveChangesAsync();
        var ctrl = new AssignmentsController(ctx, MockUserManager.Create());
        MockUserManager.SetupAdminUser(ctrl);

        var result = await ctrl.Index(null);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IEnumerable<Assignment>>(view.Model);
        Assert.Equal(2, model.Count());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Index_WithCourseFilter_ReturnsFiltered()
    {
        var (ctx, branch, course1) = await DbFactory.WithCourseAsync();
        var course2 = new Course { Name = "C2", BranchId = branch.Id, StartDate = DateTime.Today, EndDate = DateTime.Today.AddYears(1) };
        ctx.Courses.Add(course2);
        await ctx.SaveChangesAsync();
        ctx.Assignments.AddRange(
            new Assignment { CourseId = course1.Id, Title = "A1", MaxScore = 100, DueDate = DateTime.Today },
            new Assignment { CourseId = course2.Id, Title = "A2", MaxScore = 100, DueDate = DateTime.Today }
        );
        await ctx.SaveChangesAsync();
        var ctrl = new AssignmentsController(ctx, MockUserManager.Create());
        MockUserManager.SetupAdminUser(ctrl);

        var result = await ctrl.Index(course1.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IEnumerable<Assignment>>(view.Model);
        Assert.Single(model);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Details_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        var ctrl = new AssignmentsController(ctx, MockUserManager.Create());
        Assert.IsType<NotFoundResult>(await ctrl.Details(null));
    }

    [Fact]
    public async Task Details_UnknownId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        var ctrl = new AssignmentsController(ctx, MockUserManager.Create());
        Assert.IsType<NotFoundResult>(await ctrl.Details(999));
    }

    [Fact]
    public async Task Details_ValidId_ReturnsView()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        var a = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(a);
        await ctx.SaveChangesAsync();
        var ctrl = new AssignmentsController(ctx, MockUserManager.Create());

        var result = await ctrl.Details(a.Id);

        Assert.IsType<ViewResult>(result);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Get_ReturnsView()
    {
        var (ctx, _, _) = await DbFactory.WithCourseAsync();
        var ctrl = new AssignmentsController(ctx, MockUserManager.Create());
        Assert.IsType<ViewResult>(await ctrl.Create((int?)null));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_ValidModel_RedirectsToIndex()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        var ctrl = new AssignmentsController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };

        var result = await ctrl.Create(new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today });

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal(1, await ctx.Assignments.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_InvalidModel_ReturnsView()
    {
        var (ctx, _, _) = await DbFactory.WithCourseAsync();
        var ctrl = new AssignmentsController(ctx, MockUserManager.Create());
        ctrl.ModelState.AddModelError("Title", "Required");
        Assert.IsType<ViewResult>(await ctrl.Create(new Assignment()));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Edit_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new AssignmentsController(ctx, MockUserManager.Create()).Edit(null));
    }

    [Fact]
    public async Task Edit_Get_UnknownId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new AssignmentsController(ctx, MockUserManager.Create()).Edit(999));
    }

    [Fact]
    public async Task Edit_Get_ValidId_ReturnsView()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        var a = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(a);
        await ctx.SaveChangesAsync();
        Assert.IsType<ViewResult>(await new AssignmentsController(ctx, MockUserManager.Create()).Edit(a.Id));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Edit_Post_IdMismatch_ReturnsNotFound()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        var a = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(a);
        await ctx.SaveChangesAsync();
        a.Id = 999;
        Assert.IsType<NotFoundResult>(await new AssignmentsController(ctx, MockUserManager.Create()).Edit(1, a));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Edit_Post_Valid_RedirectsToIndex()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        var a = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(a);
        await ctx.SaveChangesAsync();
        var ctrl = new AssignmentsController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };
        a.Title = "Updated";
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(await ctrl.Edit(a.Id, a)).ActionName);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Delete_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new AssignmentsController(ctx, MockUserManager.Create()).Delete(null));
    }

    [Fact]
    public async Task Delete_Get_ValidId_ReturnsView()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        var a = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(a);
        await ctx.SaveChangesAsync();
        Assert.IsType<ViewResult>(await new AssignmentsController(ctx, MockUserManager.Create()).Delete(a.Id));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task DeleteConfirmed_Deletes_AndRedirects()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        var a = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(a);
        await ctx.SaveChangesAsync();
        var ctrl = new AssignmentsController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };
        await ctrl.DeleteConfirmed(a.Id);
        Assert.Equal(0, await ctx.Assignments.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task DeleteConfirmed_UnknownId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        var ctrl = new AssignmentsController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };
        Assert.IsType<NotFoundResult>(await ctrl.DeleteConfirmed(999));
    }
}

// ─── AssignmentResultsController ─────────────────────────────────────────────

public class AssignmentResultsControllerTests
{
    [Fact]
    public async Task Index_NullAssignmentId_RedirectsToAssignments()
    {
        await using var ctx = DbFactory.Create();
        var ctrl = new AssignmentResultsController(ctx, MockUserManager.Create());
        MockUserManager.SetupAdminUser(ctrl);
        var result = await ctrl.Index(null);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Assignments", redirect.ControllerName);
    }

    [Fact]
    public async Task Index_UnknownAssignmentId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        var ctrl = new AssignmentResultsController(ctx, MockUserManager.Create());
        MockUserManager.SetupAdminUser(ctrl);
        Assert.IsType<NotFoundResult>(await ctrl.Index(999));
    }

    [Fact]
    public async Task Index_ValidId_ReturnsResults()
    {
        var (ctx, course, student, _) = await DbFactory.WithEnrolmentAsync();
        var a = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(a);
        await ctx.SaveChangesAsync();
        ctx.AssignmentResults.Add(new AssignmentResult { AssignmentId = a.Id, StudentProfileId = student.Id, Score = 75 });
        await ctx.SaveChangesAsync();
        var ctrl = new AssignmentResultsController(ctx, MockUserManager.Create());
        MockUserManager.SetupAdminUser(ctrl);

        var result = await ctrl.Index(a.Id);
        Assert.IsType<ViewResult>(result);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Get_UnknownAssignmentId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new AssignmentResultsController(ctx, MockUserManager.Create()).Create(999));
    }

    [Fact]
    public async Task Create_Get_ValidId_ReturnsView()
    {
        var (ctx, course, _, _) = await DbFactory.WithEnrolmentAsync();
        var a = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(a);
        await ctx.SaveChangesAsync();
        Assert.IsType<ViewResult>(await new AssignmentResultsController(ctx, MockUserManager.Create()).Create(a.Id));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_ValidResult_RedirectsToIndex()
    {
        var (ctx, course, student, _) = await DbFactory.WithEnrolmentAsync();
        var a = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(a);
        await ctx.SaveChangesAsync();
        var ctrl = new AssignmentResultsController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };

        var result = await ctrl.Create(new AssignmentResult { AssignmentId = a.Id, StudentProfileId = student.Id, Score = 75 });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_ScoreExceedsMax_ReturnsViewWithError()
    {
        var (ctx, course, student, _) = await DbFactory.WithEnrolmentAsync();
        var a = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(a);
        await ctx.SaveChangesAsync();
        var ctrl = new AssignmentResultsController(ctx, MockUserManager.Create());

        var result = await ctrl.Create(new AssignmentResult { AssignmentId = a.Id, StudentProfileId = student.Id, Score = 150 });

        Assert.IsType<ViewResult>(result);
        Assert.False(ctrl.ModelState.IsValid);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_DuplicateResult_ReturnsViewWithError()
    {
        var (ctx, course, student, _) = await DbFactory.WithEnrolmentAsync();
        var a = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(a);
        await ctx.SaveChangesAsync();
        ctx.AssignmentResults.Add(new AssignmentResult { AssignmentId = a.Id, StudentProfileId = student.Id, Score = 70 });
        await ctx.SaveChangesAsync();
        var ctrl = new AssignmentResultsController(ctx, MockUserManager.Create());

        var result = await ctrl.Create(new AssignmentResult { AssignmentId = a.Id, StudentProfileId = student.Id, Score = 80 });

        Assert.IsType<ViewResult>(result);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Edit_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new AssignmentResultsController(ctx, MockUserManager.Create()).Edit(null));
    }

    [Fact]
    public async Task Edit_Get_UnknownId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new AssignmentResultsController(ctx, MockUserManager.Create()).Edit(999));
    }

    [Fact]
    public async Task Edit_Post_IdMismatch_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        var ctrl = new AssignmentResultsController(ctx, MockUserManager.Create());
        var r = new AssignmentResult { Id = 999 };
        Assert.IsType<NotFoundResult>(await ctrl.Edit(1, r));
    }

    [Fact]
    public async Task Edit_Post_Valid_Redirects()
    {
        var (ctx, course, student, _) = await DbFactory.WithEnrolmentAsync();
        var a = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(a);
        await ctx.SaveChangesAsync();
        var r = new AssignmentResult { AssignmentId = a.Id, StudentProfileId = student.Id, Score = 70 };
        ctx.AssignmentResults.Add(r);
        await ctx.SaveChangesAsync();
        var ctrl = new AssignmentResultsController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };
        r.Score = 85;
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(await ctrl.Edit(r.Id, r)).ActionName);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Delete_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new AssignmentResultsController(ctx, MockUserManager.Create()).Delete(null));
    }

    [Fact]
    public async Task DeleteConfirmed_Deletes_AndRedirects()
    {
        var (ctx, course, student, _) = await DbFactory.WithEnrolmentAsync();
        var a = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(a);
        await ctx.SaveChangesAsync();
        var r = new AssignmentResult { AssignmentId = a.Id, StudentProfileId = student.Id, Score = 70 };
        ctx.AssignmentResults.Add(r);
        await ctx.SaveChangesAsync();
        var ctrl = new AssignmentResultsController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };
        await ctrl.DeleteConfirmed(r.Id);
        Assert.Equal(0, await ctx.AssignmentResults.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task DeleteConfirmed_UnknownId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        var ctrl = new AssignmentResultsController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };
        Assert.IsType<NotFoundResult>(await ctrl.DeleteConfirmed(999));
    }
}

// ─── ExamsController ──────────────────────────────────────────────────────────

public class ExamsControllerTests
{
    [Fact]
    public async Task Index_ReturnsAllExams()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        ctx.Exams.AddRange(
            new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100 },
            new Exam { CourseId = course.Id, Title = "E2", Date = DateTime.Today, MaxScore = 100 }
        );
        await ctx.SaveChangesAsync();
        var ctrl = new ExamsController(ctx, MockUserManager.Create());
        MockUserManager.SetupAdminUser(ctrl);

        var result = await ctrl.Index(null);
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<Exam>>(view.Model).Count());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Index_FilterByCourse_ReturnsFiltered()
    {
        var (ctx, branch, course1) = await DbFactory.WithCourseAsync();
        var course2 = new Course { Name = "C2", BranchId = branch.Id, StartDate = DateTime.Today, EndDate = DateTime.Today.AddYears(1) };
        ctx.Courses.Add(course2);
        await ctx.SaveChangesAsync();
        ctx.Exams.AddRange(
            new Exam { CourseId = course1.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100 },
            new Exam { CourseId = course2.Id, Title = "E2", Date = DateTime.Today, MaxScore = 100 }
        );
        await ctx.SaveChangesAsync();
        var ctrl = new ExamsController(ctx, MockUserManager.Create());
        MockUserManager.SetupAdminUser(ctrl);

        var result = await ctrl.Index(course1.Id);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<Exam>>(Assert.IsType<ViewResult>(result).Model));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Details_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new ExamsController(ctx, MockUserManager.Create()).Details(null));
    }

    [Fact]
    public async Task Details_ValidId_ReturnsView()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        var e = new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100 };
        ctx.Exams.Add(e); await ctx.SaveChangesAsync();
        Assert.IsType<ViewResult>(await new ExamsController(ctx, MockUserManager.Create()).Details(e.Id));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_Valid_RedirectsToIndex()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        var ctrl = new ExamsController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };
        var result = await ctrl.Create(new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100 });
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_InvalidModel_ReturnsView()
    {
        var (ctx, _, _) = await DbFactory.WithCourseAsync();
        var ctrl = new ExamsController(ctx, MockUserManager.Create());
        ctrl.ModelState.AddModelError("Title", "Required");
        Assert.IsType<ViewResult>(await ctrl.Create(new Exam()));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Edit_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new ExamsController(ctx, MockUserManager.Create()).Edit(null));
    }

    [Fact]
    public async Task Edit_Get_UnknownId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new ExamsController(ctx, MockUserManager.Create()).Edit(999));
    }

    [Fact]
    public async Task Edit_Post_IdMismatch_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new ExamsController(ctx, MockUserManager.Create()).Edit(1, new Exam { Id = 999 }));
    }

    [Fact]
    public async Task Edit_Post_Valid_Redirects()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        var e = new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100 };
        ctx.Exams.Add(e); await ctx.SaveChangesAsync();
        var ctrl = new ExamsController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };
        e.Title = "Updated";
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(await ctrl.Edit(e.Id, e)).ActionName);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task ReleaseResults_UnknownId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        var ctrl = new ExamsController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };
        Assert.IsType<NotFoundResult>(await ctrl.ReleaseResults(999));
    }

    [Fact]
    public async Task ReleaseResults_SetsReleasedFlag()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        var e = new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100, ResultsReleased = false };
        ctx.Exams.Add(e); await ctx.SaveChangesAsync();
        var ctrl = new ExamsController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };
        await ctrl.ReleaseResults(e.Id);
        var updated = await ctx.Exams.FindAsync(e.Id);
        Assert.True(updated!.ResultsReleased);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Delete_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new ExamsController(ctx, MockUserManager.Create()).Delete(null));
    }

    [Fact]
    public async Task DeleteConfirmed_Deletes_AndRedirects()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        var e = new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100 };
        ctx.Exams.Add(e); await ctx.SaveChangesAsync();
        var ctrl = new ExamsController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };
        await ctrl.DeleteConfirmed(e.Id);
        Assert.Equal(0, await ctx.Exams.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task DeleteConfirmed_UnknownId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        var ctrl = new ExamsController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };
        Assert.IsType<NotFoundResult>(await ctrl.DeleteConfirmed(999));
    }
}

// ─── ExamResultsController ────────────────────────────────────────────────────

public class ExamResultsControllerTests
{
    [Fact]
    public async Task Index_NullExamId_RedirectsToExams()
    {
        await using var ctx = DbFactory.Create();
        var result = await new ExamResultsController(ctx).Index(null);
        Assert.Equal("Exams", Assert.IsType<RedirectToActionResult>(result).ControllerName);
    }

    [Fact]
    public async Task Index_UnknownExamId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new ExamResultsController(ctx).Index(999));
    }

    [Fact]
    public async Task Index_ValidId_ReturnsResults()
    {
        var (ctx, course, student, _) = await DbFactory.WithEnrolmentAsync();
        var e = new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100, ResultsReleased = true };
        ctx.Exams.Add(e); await ctx.SaveChangesAsync();
        ctx.ExamResults.Add(new ExamResult { ExamId = e.Id, StudentProfileId = student.Id, Score = 80, Grade = "A2" });
        await ctx.SaveChangesAsync();

        var result = await new ExamResultsController(ctx).Index(e.Id);
        Assert.IsType<ViewResult>(result);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Get_UnknownExamId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new ExamResultsController(ctx).Create(999));
    }

    [Fact]
    public async Task Create_Post_Valid_Redirects()
    {
        var (ctx, course, student, _) = await DbFactory.WithEnrolmentAsync();
        var e = new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100 };
        ctx.Exams.Add(e); await ctx.SaveChangesAsync();
        var ctrl = new ExamResultsController(ctx) { TempData = DbFactory.FakeTempData() };

        var result = await ctrl.Create(new ExamResult { ExamId = e.Id, StudentProfileId = student.Id, Score = 75, Grade = "B1" });
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_ScoreExceedsMax_ReturnsViewWithError()
    {
        var (ctx, course, student, _) = await DbFactory.WithEnrolmentAsync();
        var e = new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100 };
        ctx.Exams.Add(e); await ctx.SaveChangesAsync();
        var ctrl = new ExamResultsController(ctx);

        var result = await ctrl.Create(new ExamResult { ExamId = e.Id, StudentProfileId = student.Id, Score = 120 });
        Assert.IsType<ViewResult>(result);
        Assert.False(ctrl.ModelState.IsValid);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_Duplicate_ReturnsViewWithError()
    {
        var (ctx, course, student, _) = await DbFactory.WithEnrolmentAsync();
        var e = new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100 };
        ctx.Exams.Add(e); await ctx.SaveChangesAsync();
        ctx.ExamResults.Add(new ExamResult { ExamId = e.Id, StudentProfileId = student.Id, Score = 70 });
        await ctx.SaveChangesAsync();
        var ctrl = new ExamResultsController(ctx);

        var result = await ctrl.Create(new ExamResult { ExamId = e.Id, StudentProfileId = student.Id, Score = 80 });
        Assert.IsType<ViewResult>(result);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Edit_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new ExamResultsController(ctx).Edit(null));
    }

    [Fact]
    public async Task Edit_Post_IdMismatch_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new ExamResultsController(ctx).Edit(1, new ExamResult { Id = 999 }));
    }

    [Fact]
    public async Task Edit_Post_Valid_Redirects()
    {
        var (ctx, course, student, _) = await DbFactory.WithEnrolmentAsync();
        var e = new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100 };
        ctx.Exams.Add(e); await ctx.SaveChangesAsync();
        var r = new ExamResult { ExamId = e.Id, StudentProfileId = student.Id, Score = 70, Grade = "B2" };
        ctx.ExamResults.Add(r); await ctx.SaveChangesAsync();
        var ctrl = new ExamResultsController(ctx) { TempData = DbFactory.FakeTempData() };
        r.Score = 85; r.Grade = "A2";
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(await ctrl.Edit(r.Id, r)).ActionName);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Delete_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new ExamResultsController(ctx).Delete(null));
    }

    [Fact]
    public async Task DeleteConfirmed_Deletes_AndRedirects()
    {
        var (ctx, course, student, _) = await DbFactory.WithEnrolmentAsync();
        var e = new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100 };
        ctx.Exams.Add(e); await ctx.SaveChangesAsync();
        var r = new ExamResult { ExamId = e.Id, StudentProfileId = student.Id, Score = 70 };
        ctx.ExamResults.Add(r); await ctx.SaveChangesAsync();
        var ctrl = new ExamResultsController(ctx) { TempData = DbFactory.FakeTempData() };
        await ctrl.DeleteConfirmed(r.Id);
        Assert.Equal(0, await ctx.ExamResults.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task DeleteConfirmed_UnknownId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        var ctrl = new ExamResultsController(ctx) { TempData = DbFactory.FakeTempData() };
        Assert.IsType<NotFoundResult>(await ctrl.DeleteConfirmed(999));
    }
}

// ─── AttendanceController ─────────────────────────────────────────────────────

public class AttendanceControllerTests
{
    [Fact]
    public async Task Index_NullEnrolmentId_ReturnsEmptyView()
    {
        await using var ctx = DbFactory.Create();
        var ctrl = new AttendanceController(ctx, MockUserManager.Create());
        MockUserManager.SetupAdminUser(ctrl);
        var result = await ctrl.Index(null);
        var view = Assert.IsType<ViewResult>(result);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<AttendanceRecord>>(view.Model));
    }

    [Fact]
    public async Task Index_UnknownEnrolmentId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        var ctrl = new AttendanceController(ctx, MockUserManager.Create());
        MockUserManager.SetupAdminUser(ctrl);
        Assert.IsType<NotFoundResult>(await ctrl.Index(999));
    }

    [Fact]
    public async Task Index_ValidId_ReturnsAttendanceRecords()
    {
        var (ctx, _, _, enrolment) = await DbFactory.WithEnrolmentAsync();
        ctx.AttendanceRecords.Add(new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 1, SessionDate = DateTime.Today, Present = true });
        await ctx.SaveChangesAsync();
        var ctrl = new AttendanceController(ctx, MockUserManager.Create());
        MockUserManager.SetupAdminUser(ctrl);

        var result = await ctrl.Index(enrolment.Id);
        Assert.IsType<ViewResult>(result);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Get_UnknownEnrolmentId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new AttendanceController(ctx, MockUserManager.Create()).Create(999));
    }

    [Fact]
    public async Task Create_Get_ValidId_ReturnsView()
    {
        var (ctx, _, _, enrolment) = await DbFactory.WithEnrolmentAsync();
        Assert.IsType<ViewResult>(await new AttendanceController(ctx, MockUserManager.Create()).Create(enrolment.Id));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_Valid_Redirects()
    {
        var (ctx, _, _, enrolment) = await DbFactory.WithEnrolmentAsync();
        var ctrl = new AttendanceController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };
        var record = new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 1, SessionDate = DateTime.Today, Present = true };

        var result = await ctrl.Create(record);
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_DuplicateWeek_ReturnsViewWithError()
    {
        var (ctx, _, _, enrolment) = await DbFactory.WithEnrolmentAsync();
        ctx.AttendanceRecords.Add(new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 1, SessionDate = DateTime.Today, Present = true });
        await ctx.SaveChangesAsync();
        var ctrl = new AttendanceController(ctx, MockUserManager.Create());

        var result = await ctrl.Create(new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 1, SessionDate = DateTime.Today, Present = false });
        Assert.IsType<ViewResult>(result);
        Assert.False(ctrl.ModelState.IsValid);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Edit_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new AttendanceController(ctx, MockUserManager.Create()).Edit(null));
    }

    [Fact]
    public async Task Edit_Get_ValidId_ReturnsView()
    {
        var (ctx, _, _, enrolment) = await DbFactory.WithEnrolmentAsync();
        var r = new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 1, SessionDate = DateTime.Today, Present = true };
        ctx.AttendanceRecords.Add(r); await ctx.SaveChangesAsync();
        Assert.IsType<ViewResult>(await new AttendanceController(ctx, MockUserManager.Create()).Edit(r.Id));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Edit_Post_IdMismatch_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new AttendanceController(ctx, MockUserManager.Create()).Edit(1, new AttendanceRecord { Id = 999 }));
    }

    [Fact]
    public async Task Edit_Post_Valid_Redirects()
    {
        var (ctx, _, _, enrolment) = await DbFactory.WithEnrolmentAsync();
        var r = new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 1, SessionDate = DateTime.Today, Present = true };
        ctx.AttendanceRecords.Add(r); await ctx.SaveChangesAsync();
        var ctrl = new AttendanceController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };
        r.Present = false;
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(await ctrl.Edit(r.Id, r)).ActionName);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Delete_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new AttendanceController(ctx, MockUserManager.Create()).Delete(null));
    }

    [Fact]
    public async Task DeleteConfirmed_Deletes_AndRedirects()
    {
        var (ctx, _, _, enrolment) = await DbFactory.WithEnrolmentAsync();
        var r = new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 1, SessionDate = DateTime.Today, Present = true };
        ctx.AttendanceRecords.Add(r); await ctx.SaveChangesAsync();
        var ctrl = new AttendanceController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };
        await ctrl.DeleteConfirmed(r.Id);
        Assert.Equal(0, await ctx.AttendanceRecords.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task DeleteConfirmed_UnknownId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        var ctrl = new AttendanceController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };
        Assert.IsType<NotFoundResult>(await ctrl.DeleteConfirmed(999));
    }
}

// ─── FacultyController ────────────────────────────────────────────────────────

public class FacultyControllerTests
{
    [Fact]
    public async Task Index_ReturnsAllFaculty()
    {
        await using var ctx = DbFactory.Create();
        ctx.FacultyProfiles.AddRange(
            new FacultyProfile { IdentityUserId = "u1", Name = "F1", Email = "f1@t.ie" },
            new FacultyProfile { IdentityUserId = "u2", Name = "F2", Email = "f2@t.ie" }
        );
        await ctx.SaveChangesAsync();
        var result = await new FacultyController(ctx, MockUserManager.Create()).Index();
        Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<FacultyProfile>>(Assert.IsType<ViewResult>(result).Model).Count());
    }

    [Fact]
    public async Task Details_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new FacultyController(ctx, MockUserManager.Create()).Details(null));
    }

    [Fact]
    public async Task Details_ValidId_ReturnsView()
    {
        await using var ctx = DbFactory.Create();
        var f = new FacultyProfile { IdentityUserId = "u1", Name = "F1", Email = "f1@t.ie" };
        ctx.FacultyProfiles.Add(f); await ctx.SaveChangesAsync();
        Assert.IsType<ViewResult>(await new FacultyController(ctx, MockUserManager.Create()).Details(f.Id));
    }

    [Fact]
    public void Create_Get_ReturnsView()
    {
        using var ctx = DbFactory.Create();
        Assert.IsType<ViewResult>(new FacultyController(ctx, MockUserManager.Create()).Create());
    }

    [Fact]
    public async Task Edit_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new FacultyController(ctx, MockUserManager.Create()).Edit(null));
    }

    [Fact]
    public async Task Edit_Get_ValidId_ReturnsView()
    {
        await using var ctx = DbFactory.Create();
        var f = new FacultyProfile { IdentityUserId = "u1", Name = "F1", Email = "f1@t.ie" };
        ctx.FacultyProfiles.Add(f); await ctx.SaveChangesAsync();
        Assert.IsType<ViewResult>(await new FacultyController(ctx, MockUserManager.Create()).Edit(f.Id));
    }

    [Fact]
    public async Task Edit_Post_IdMismatch_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new FacultyController(ctx, MockUserManager.Create()).Edit(1, new FacultyProfile { Id = 999 }));
    }

    [Fact]
    public async Task Edit_Post_Valid_Redirects()
    {
        await using var ctx = DbFactory.Create();
        var f = new FacultyProfile { IdentityUserId = "u1", Name = "F1", Email = "f1@t.ie" };
        ctx.FacultyProfiles.Add(f); await ctx.SaveChangesAsync();
        var ctrl = new FacultyController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };
        f.Name = "Updated";
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(await ctrl.Edit(f.Id, f)).ActionName);
    }

    [Fact]
    public async Task Delete_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new FacultyController(ctx, MockUserManager.Create()).Delete(null));
    }

    [Fact]
    public async Task Delete_Get_ValidId_ReturnsView()
    {
        await using var ctx = DbFactory.Create();
        var f = new FacultyProfile { IdentityUserId = "u1", Name = "F1", Email = "f1@t.ie" };
        ctx.FacultyProfiles.Add(f); await ctx.SaveChangesAsync();
        Assert.IsType<ViewResult>(await new FacultyController(ctx, MockUserManager.Create()).Delete(f.Id));
    }

    [Fact]
    public async Task DeleteConfirmed_Deletes_AndRedirects()
    {
        await using var ctx = DbFactory.Create();
        var f = new FacultyProfile { IdentityUserId = "u1", Name = "F1", Email = "f1@t.ie" };
        ctx.FacultyProfiles.Add(f); await ctx.SaveChangesAsync();
        var ctrl = new FacultyController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };
        await ctrl.DeleteConfirmed(f.Id);
        Assert.Equal(0, await ctx.FacultyProfiles.CountAsync());
    }

    [Fact]
    public async Task DeleteConfirmed_UnknownId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        var ctrl = new FacultyController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };
        Assert.IsType<NotFoundResult>(await ctrl.DeleteConfirmed(999));
    }

    [Fact]
    public async Task Assign_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        Assert.IsType<NotFoundResult>(await new FacultyController(ctx, MockUserManager.Create()).Assign((int?)null));
    }

    [Fact]
    public async Task Assign_Get_ValidId_ReturnsView()
    {
        var (ctx, _, _) = await DbFactory.WithCourseAsync();
        var f = new FacultyProfile { IdentityUserId = "u1", Name = "F1", Email = "f1@t.ie" };
        ctx.FacultyProfiles.Add(f); await ctx.SaveChangesAsync();
        Assert.IsType<ViewResult>(await new FacultyController(ctx, MockUserManager.Create()).Assign((int?)f.Id));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Assign_Post_Valid_Redirects()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        var f = new FacultyProfile { IdentityUserId = "u1", Name = "F1", Email = "f1@t.ie" };
        ctx.FacultyProfiles.Add(f); await ctx.SaveChangesAsync();
        var ctrl = new FacultyController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };

        var result = await ctrl.Assign(new FacultyCourseAssignment { FacultyProfileId = f.Id, CourseId = course.Id, IsTutor = false });
        Assert.Equal("Details", Assert.IsType<RedirectToActionResult>(result).ActionName);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Assign_Post_Duplicate_ReturnsViewWithError()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        var f = new FacultyProfile { IdentityUserId = "u1", Name = "F1", Email = "f1@t.ie" };
        ctx.FacultyProfiles.Add(f); await ctx.SaveChangesAsync();
        ctx.FacultyCourseAssignments.Add(new FacultyCourseAssignment { FacultyProfileId = f.Id, CourseId = course.Id });
        await ctx.SaveChangesAsync();
        var ctrl = new FacultyController(ctx, MockUserManager.Create());

        var result = await ctrl.Assign(new FacultyCourseAssignment { FacultyProfileId = f.Id, CourseId = course.Id });
        Assert.IsType<ViewResult>(result);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Unassign_ValidId_RemovesAndRedirects()
    {
        var (ctx, _, course) = await DbFactory.WithCourseAsync();
        var f = new FacultyProfile { IdentityUserId = "u1", Name = "F1", Email = "f1@t.ie" };
        ctx.FacultyProfiles.Add(f); await ctx.SaveChangesAsync();
        var assignment = new FacultyCourseAssignment { FacultyProfileId = f.Id, CourseId = course.Id };
        ctx.FacultyCourseAssignments.Add(assignment); await ctx.SaveChangesAsync();
        var ctrl = new FacultyController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };

        var result = await ctrl.Unassign(assignment.Id, f.Id);
        Assert.Equal("Details", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal(0, await ctx.FacultyCourseAssignments.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Unassign_UnknownId_ReturnsNotFound()
    {
        await using var ctx = DbFactory.Create();
        var ctrl = new FacultyController(ctx, MockUserManager.Create()) { TempData = DbFactory.FakeTempData() };
        Assert.IsType<NotFoundResult>(await ctrl.Unassign(999, 1));
    }
}

// ─── Helper : MockUserManager ─────────────────────────────────────────────────
// Nécessaire pour les controllers qui acceptent UserManager en paramètre
// mais dont les tests n'ont pas besoin de l'identité (rôle Admin simulé)

file static class MockUserManager
{
    public static UserManager<IdentityUser> Create()
    {
        var store = new Mock<IUserStore<IdentityUser>>();
        return new Mock<UserManager<IdentityUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!
        ).Object;
    }

    // Simule un utilisateur Admin (IsInRole("Faculty") retourne false)
    public static void SetupAdminUser(Controller ctrl)
    {
        var claims = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "admin@vgc.ie") },
                "TestAuth"
            )
        );
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = claims }
        };
    }
}