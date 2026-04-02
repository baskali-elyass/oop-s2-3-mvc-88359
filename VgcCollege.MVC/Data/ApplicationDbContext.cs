using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Domain;

namespace VgcCollege.MVC.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();
    public DbSet<FacultyProfile> FacultyProfiles => Set<FacultyProfile>();
    public DbSet<FacultyCourseAssignment> FacultyCourseAssignments => Set<FacultyCourseAssignment>();
    public DbSet<CourseEnrolment> CourseEnrolments => Set<CourseEnrolment>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<AssignmentResult> AssignmentResults => Set<AssignmentResult>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamResult> ExamResults => Set<ExamResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Course>()
            .HasOne(c => c.Branch).WithMany(b => b.Courses)
            .HasForeignKey(c => c.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<FacultyCourseAssignment>()
            .HasOne(f => f.FacultyProfile).WithMany(fp => fp.CourseAssignments)
            .HasForeignKey(f => f.FacultyProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FacultyCourseAssignment>()
            .HasOne(f => f.Course).WithMany(c => c.FacultyAssignments)
            .HasForeignKey(f => f.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CourseEnrolment>()
            .HasOne(e => e.StudentProfile).WithMany(s => s.Enrolments)
            .HasForeignKey(e => e.StudentProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CourseEnrolment>()
            .HasOne(e => e.Course).WithMany(c => c.Enrolments)
            .HasForeignKey(e => e.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AttendanceRecord>()
            .HasOne(a => a.CourseEnrolment).WithMany(e => e.AttendanceRecords)
            .HasForeignKey(a => a.CourseEnrolmentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Assignment>()
            .HasOne(a => a.Course).WithMany(c => c.Assignments)
            .HasForeignKey(a => a.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AssignmentResult>()
            .HasOne(r => r.Assignment).WithMany(a => a.Results)
            .HasForeignKey(r => r.AssignmentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AssignmentResult>()
            .HasOne(r => r.StudentProfile).WithMany(s => s.AssignmentResults)
            .HasForeignKey(r => r.StudentProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Exam>()
            .HasOne(e => e.Course).WithMany(c => c.Exams)
            .HasForeignKey(e => e.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExamResult>()
            .HasOne(r => r.Exam).WithMany(e => e.Results)
            .HasForeignKey(r => r.ExamId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExamResult>()
            .HasOne(r => r.StudentProfile).WithMany(s => s.ExamResults)
            .HasForeignKey(r => r.StudentProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }
}
