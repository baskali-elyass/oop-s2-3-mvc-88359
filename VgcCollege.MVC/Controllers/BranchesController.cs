using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Domain;
using VgcCollege.MVC.Data;

namespace VgcCollege.MVC.Controllers;

[Authorize(Roles = "Admin")]
public class BranchesController(ApplicationDbContext context) : Controller
{
    public async Task<IActionResult> Index()
        => View(await context.Branches.Include(b => b.Courses).OrderBy(b => b.Name).ToListAsync());

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null) return NotFound();
        var branch = await context.Branches.Include(b => b.Courses).FirstOrDefaultAsync(b => b.Id == id);
        return branch is null ? NotFound() : View(branch);
    }

    public IActionResult Create() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Name,Address")] Branch branch)
    {
        if (!ModelState.IsValid) return View(branch);
        context.Add(branch);
        await context.SaveChangesAsync();
        TempData["Success"] = $"Branch '{branch.Name}' created.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var branch = await context.Branches.FindAsync(id);
        return branch is null ? NotFound() : View(branch);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Address")] Branch branch)
    {
        if (id != branch.Id) return NotFound();
        if (!ModelState.IsValid) return View(branch);
        try
        {
            context.Update(branch);
            await context.SaveChangesAsync();
            TempData["Success"] = "Branch updated.";
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!context.Branches.Any(b => b.Id == id)) return NotFound();
            throw;
        }
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var branch = await context.Branches.Include(b => b.Courses).FirstOrDefaultAsync(b => b.Id == id);
        return branch is null ? NotFound() : View(branch);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var branch = await context.Branches.Include(b => b.Courses).FirstOrDefaultAsync(b => b.Id == id);
        if (branch is null) return NotFound();
        if (branch.Courses.Any())
        {
            TempData["Error"] = "Cannot delete a branch that has courses.";
            return RedirectToAction(nameof(Index));
        }
        context.Branches.Remove(branch);
        await context.SaveChangesAsync();
        TempData["Success"] = "Branch deleted.";
        return RedirectToAction(nameof(Index));
    }
}
