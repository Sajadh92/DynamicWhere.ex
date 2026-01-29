using DynamicWhere.API.Data;
using DynamicWhere.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CustomersController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<CustomersController> _logger;

    public CustomersController(AppDbContext context, ILogger<CustomersController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get all customers
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Customer>>> GetCustomers()
    {
        var customers = await _context.Customers.ToListAsync();
        return Ok(customers);
    }

    /// <summary>
    /// Get customer by ID with orders and reviews
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Customer>> GetCustomer(Guid id)
    {
        var customer = await _context.Customers
            .Include(c => c.Orders)
            .Include(c => c.Reviews)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (customer == null)
            return NotFound();

        return Ok(customer);
    }

    /// <summary>
    /// Create a new customer
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Customer>> CreateCustomer(Customer customer)
    {
        customer.Id = Guid.NewGuid();
        customer.RegisteredAt = DateTime.UtcNow;

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetCustomer), new { id = customer.Id }, customer);
    }

    /// <summary>
    /// Update an existing customer
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCustomer(Guid id, Customer customer)
    {
        if (id != customer.Id)
            return BadRequest();

        _context.Entry(customer).State = EntityState.Modified;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await CustomerExists(id))
                return NotFound();

            throw;
        }

        return NoContent();
    }

    /// <summary>
    /// Delete a customer
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCustomer(Guid id)
    {
        var customer = await _context.Customers.FindAsync(id);
        if (customer == null)
            return NotFound();

        _context.Customers.Remove(customer);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private async Task<bool> CustomerExists(Guid id)
    {
        return await _context.Customers.AnyAsync(e => e.Id == id);
    }
}
