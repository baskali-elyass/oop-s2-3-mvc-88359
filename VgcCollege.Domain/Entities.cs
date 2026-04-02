using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace VgcCollege.Domain;

public class Branch
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Address { get; set; } = string.Empty;

    public ICollection<Course> Courses { get; set; } = new List<Course>();
}

public class Course
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public int BranchId { get; set; }

    [ValidateNever]
    public Branch Branch { get; set; } = null!;

    [Required]
    public DateTime StartDate { get; set; }

    [Required]
    public DateTime EndDate { get; set; }

    public ICollection<CourseEnrolment> Enrolments { get; set; } = new List<CourseEnrolment>();
    public ICollection<FacultyCourseAssignment> FacultyAssignments { get; set; } = new List<FacultyCourseAssignment>();
    public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
    public ICollection<Exam> Exams { get; set; } = new List<Exam>();
}

public class StudentProfile
{
    public int Id { get; set; }

    [Required]
    public string IdentityUserId { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(300)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    public DateTime? DateOfBirth { get; set; }

    [Required, MaxLength(20)]
    public string StudentNumber { get; set; } = string.Empty;

    public ICollection<CourseEnrolment> Enrolments { get; set; } = new List<CourseEnrolment>();
    public ICollection<AssignmentResult> AssignmentResults { get; set; } = new List<AssignmentResult>();
    public ICollection<ExamResult> ExamResults { get; set; } = new List<ExamResult>();
}

public class FacultyProfile
{
    public int Id { get; set; }

    [Required]
    public string IdentityUserId { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(300)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Phone { get; set; }

    public ICollection<FacultyCourseAssignment> CourseAssignments { get; set; } = new List<FacultyCourseAssignment>();
}

public class FacultyCourseAssignment
{
    public int Id { get; set; }
    public int FacultyProfileId { get; set; }

    [ValidateNever]
    public FacultyProfile FacultyProfile { get; set; } = null!;

    public int CourseId { get; set; }

    [ValidateNever]
    public Course Course { get; set; } = null!;

    public bool IsTutor { get; set; } = false;
}

public class CourseEnrolment
{
    public int Id { get; set; }
    public int StudentProfileId { get; set; }

    [ValidateNever]
    public StudentProfile StudentProfile { get; set; } = null!;

    public int CourseId { get; set; }

    [ValidateNever]
    public Course Course { get; set; } = null!;

    [Required]
    public DateTime EnrolDate { get; set; }

    [Required, MaxLength(50)]
    public string Status { get; set; } = "Active";

    public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
}

public class AttendanceRecord
{
    public int Id { get; set; }
    public int CourseEnrolmentId { get; set; }

    [ValidateNever]
    public CourseEnrolment CourseEnrolment { get; set; } = null!;

    [Required]
    public int WeekNumber { get; set; }

    [Required]
    public DateTime SessionDate { get; set; }

    public bool Present { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}

public class Assignment
{
    public int Id { get; set; }
    public int CourseId { get; set; }

    [ValidateNever]
    public Course Course { get; set; } = null!;

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Range(1, 1000)]
    public int MaxScore { get; set; }

    [Required]
    public DateTime DueDate { get; set; }

    public ICollection<AssignmentResult> Results { get; set; } = new List<AssignmentResult>();
}

public class AssignmentResult
{
    public int Id { get; set; }
    public int AssignmentId { get; set; }

    [ValidateNever]
    public Assignment Assignment { get; set; } = null!;

    public int StudentProfileId { get; set; }

    [ValidateNever]
    public StudentProfile StudentProfile { get; set; } = null!;

    [Range(0, 1000)]
    public double Score { get; set; }

    [MaxLength(1000)]
    public string? Feedback { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}

public class Exam
{
    public int Id { get; set; }
    public int CourseId { get; set; }

    [ValidateNever]
    public Course Course { get; set; } = null!;

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public DateTime Date { get; set; }

    [Range(1, 1000)]
    public int MaxScore { get; set; }

    public bool ResultsReleased { get; set; } = false;

    public ICollection<ExamResult> Results { get; set; } = new List<ExamResult>();
}

public class ExamResult
{
    public int Id { get; set; }
    public int ExamId { get; set; }

    [ValidateNever]
    public Exam Exam { get; set; } = null!;

    public int StudentProfileId { get; set; }

    [ValidateNever]
    public StudentProfile StudentProfile { get; set; } = null!;

    [Range(0, 1000)]
    public double Score { get; set; }

    [MaxLength(10)]
    public string? Grade { get; set; }
}
