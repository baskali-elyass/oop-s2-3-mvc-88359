# Acme Global College – Student & Course Management System

ASP.NET Core MVC application managing students, courses, faculty, enrolments, attendance, assignments and exams across 3 college branches.

## How to Run

### Prerequisites
- .NET 10 SDK (or .NET 8)

### Steps
1. Open `VgcCollege.slnx` in Visual Studio
2. Set `VgcCollege.MVC` as startup project
3. Press F5 — the SQLite database is created and seeded automatically

Or via terminal:
```
dotnet run --project VgcCollege.MVC
```
App runs at http://localhost:5000

## Demo Accounts

| Role    | Email                | Password       |
|---------|----------------------|----------------|
| Admin   | admin@vgc.ie         | Admin@1234     |
| Faculty | faculty1@vgc.ie      | Faculty@1234   |
| Faculty | faculty2@vgc.ie      | Faculty2@1234  |
| Student | student1@vgc.ie      | Student@1234   |
| Student | student2@vgc.ie      | Student2@1234  |
| Student | student3@vgc.ie      | Student3@1234  |

## How to Run Tests

```
dotnet test
```

With coverage:
```
dotnet test VgcCollege.Tests/VgcCollege.Tests.csproj --collect:"XPlat Code Coverage" --settings coverlet.runsettings
```

## Project Structure

```
VgcCollege.Domain/    — Domain entities (Branch, Course, Student, Faculty, etc.)
VgcCollege.MVC/       — ASP.NET Core MVC application
VgcCollege.Tests/     — xUnit test project (33 tests)
VgcCollege.slnx       — Solution file
coverlet.runsettings  — Coverage configuration
```

## Design Decisions

- SQLite for portability (no SQL Server required)
- EnsureCreatedAsync instead of migrations — DB created cleanly from seed data
- Provisional exam results hidden at the query level (not just UI hiding)
- Faculty restricted to their assigned courses server-side with [Authorize(Roles)]
- 3 branches, 4 courses, 2 faculty, 3 students seeded with full attendance/results
