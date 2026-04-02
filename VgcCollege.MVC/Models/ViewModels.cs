using VgcCollege.Domain;

namespace VgcCollege.MVC.Models;

public class AdminDashboardViewModel
{
    public int TotalStudents { get; set; }
    public int TotalCourses { get; set; }
    public int TotalBranches { get; set; }
    public int TotalFaculty { get; set; }
    public int ActiveEnrolments { get; set; }
    public int PendingExamReleases { get; set; }
}

public class FacultyDashboardViewModel
{
    public string FacultyName { get; set; } = "";
    public List<Course> MyCourses { get; set; } = new();
    public int MyStudentCount { get; set; }
}

public class StudentDashboardViewModel
{
    public string StudentName { get; set; } = "";
    public string StudentNumber { get; set; } = "";
    public List<CourseEnrolment> MyEnrolments { get; set; } = new();
}

public class ErrorViewModel
{
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}