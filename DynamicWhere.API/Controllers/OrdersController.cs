using DynamicWhere.API.Data;
using DynamicWhere.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(AppDbContext context, ILogger<OrdersController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves all orders with customer and product details.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Order>>> GetOrders()
    {
        var orders = await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .ToListAsync();

        return Ok(orders);
    }

    /// <summary>
    /// Retrieves a specific order by its unique identifier.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Order>> GetOrder(Guid id)
    {
        var order = await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null)
            return NotFound();

        return Ok(order);
    }

    /// <summary>
    /// Creates a new order with its associated items.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Order>> CreateOrder(Order order)
    {
        order.Id = Guid.NewGuid();
        order.OrderDate = DateTime.UtcNow;
        order.OrderNumber = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}";

        foreach (var item in order.OrderItems)
            item.Id = Guid.NewGuid();

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
    }

    /// <summary>
    /// Updates the status of an existing order.
    /// </summary>
    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateOrderStatus(Guid id, [FromBody] OrderStatus status)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order == null)
            return NotFound();

        order.Status = status;

        if (status == OrderStatus.Shipped && !order.ShippedDate.HasValue)
            order.ShippedDate = DateTime.UtcNow;
        else if (status == OrderStatus.Delivered && !order.DeliveredDate.HasValue)
            order.DeliveredDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Deletes an order from the system.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteOrder(Guid id)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order == null)
            return NotFound();

        _context.Orders.Remove(order);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
