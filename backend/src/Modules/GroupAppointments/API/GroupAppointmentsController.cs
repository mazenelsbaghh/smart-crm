using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Modules.Conversations.Hubs;
using Modules.GroupAppointments.Domain;
using Shared.Infrastructure;
using Shared.Security;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using FirebaseAdmin.Messaging;
using StackExchange.Redis;

namespace Modules.GroupAppointments.API
{
    [ApiController]
    [Route("api")]
    public class GroupAppointmentsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ITenantContext _tenantContext;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly IDatabase _redis;

        public GroupAppointmentsController(
            AppDbContext context, 
            ITenantContext tenantContext, 
            IHubContext<NotificationHub> hubContext,
            IConnectionMultiplexer redis)
        {
            _context = context;
            _tenantContext = tenantContext;
            _hubContext = hubContext;
            _redis = redis.GetDatabase();
        }

        // --- Admin/Agent Authorized Endpoints ---

        [HttpGet("group-appointments")]
        public async Task<IActionResult> GetGroups()
        {
            var groups = await _context.GroupAppointments
                .Include(g => g.Bookings)
                .OrderBy(g => g.DateTime)
                .ToListAsync();

            var result = groups.Select(g => new
            {
                g.Id,
                g.ProjectId,
                g.Name,
                g.DateTime,
                g.Capacity,
                g.IsActive,
                g.Days,
                g.Mode,
                g.CreatedAt,
                g.UpdatedAt,
                BookedCount = g.Bookings.Count,
                Bookings = g.Bookings.Select(b => new
                {
                    b.Id,
                    b.CustomerName,
                    b.CustomerPhone,
                    b.CustomerId,
                    b.CreatedAt
                })
            });

            return Ok(result);
        }

        [HttpPost("group-appointments")]
        public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest request)
        {
            var mode = request.Mode ?? "offline";
            var autoName = mode == "online" ? "أونلاين" : "في السنتر";
            var group = new GroupAppointment
            {
                Name = string.IsNullOrEmpty(request.Name) ? autoName : request.Name,
                DateTime = DateTime.SpecifyKind(request.DateTime, DateTimeKind.Utc),
                Capacity = request.Capacity,
                IsActive = request.IsActive,
                Days = request.Days ?? string.Empty,
                Mode = mode
            };

            _context.GroupAppointments.Add(group);
            await _context.SaveChangesAsync();

            return Ok(group);
        }

        [HttpPut("group-appointments/{id}")]
        public async Task<IActionResult> UpdateGroup(Guid id, [FromBody] UpdateGroupRequest request)
        {
            var group = await _context.GroupAppointments.FindAsync(id);
            if (group == null) return NotFound();

            group.Name = request.Name ?? group.Name;
            if (request.DateTime.HasValue)
            {
                group.DateTime = DateTime.SpecifyKind(request.DateTime.Value, DateTimeKind.Utc);
            }
            if (request.Capacity.HasValue)
            {
                group.Capacity = request.Capacity.Value;
            }
            if (request.IsActive.HasValue)
            {
                group.IsActive = request.IsActive.Value;
            }
            if (request.Days != null)
            {
                group.Days = request.Days;
            }
            if (request.Mode != null)
            {
                group.Mode = request.Mode;
                if (string.IsNullOrEmpty(request.Name))
                {
                    group.Name = request.Mode == "online" ? "أونلاين" : "في السنتر";
                }
            }

            _context.Entry(group).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            return Ok(group);
        }

        [HttpDelete("group-appointments/{id}")]
        public async Task<IActionResult> DeleteGroup(Guid id)
        {
            var group = await _context.GroupAppointments.FindAsync(id);
            if (group == null) return NotFound();

            _context.GroupAppointments.Remove(group);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpPatch("group-appointments/{id}/toggle")]
        public async Task<IActionResult> ToggleGroup(Guid id)
        {
            var group = await _context.GroupAppointments.FindAsync(id);
            if (group == null) return NotFound();

            group.IsActive = !group.IsActive;
            _context.Entry(group).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            return Ok(new { group.Id, group.IsActive });
        }

        [HttpDelete("group-appointments/bookings/{bookingId}")]
        public async Task<IActionResult> DeleteBooking(Guid bookingId)
        {
            var booking = await _context.GroupAppointmentBookings
                .Include(b => b.GroupAppointment)
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (booking == null) return NotFound();

            var group = booking.GroupAppointment;
            var groupId = booking.GroupAppointmentId;
            var projectId = booking.ProjectId;

            _context.GroupAppointmentBookings.Remove(booking);
            await _context.SaveChangesAsync();

            try
            {
                var bookedCount = await _context.GroupAppointmentBookings
                    .CountAsync(b => b.GroupAppointmentId == groupId);

                await _hubContext.Clients.Group($"project_{projectId}").SendAsync("GroupBookingUpdated", new
                {
                    groupId,
                    groupName = group?.Name,
                    customerPhone = booking.CustomerPhone,
                    customerName = booking.CustomerName,
                    newBookedCount = bookedCount,
                    capacity = group?.Capacity,
                    isFull = group != null && bookedCount >= group.Capacity
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GroupAppointmentsController] SignalR booking delete error: {ex.Message}");
            }

            return NoContent();
        }

        // --- Anonymous Public Booking Endpoints ---

        [AllowAnonymous]
        [HttpGet("public/group-appointments/active/{projectId}")]
        public async Task<IActionResult> GetActiveGroupsForPublic(Guid projectId)
        {
            _tenantContext.SetProjectId(projectId);

            var settings = await _context.ProjectSettings.FirstOrDefaultAsync(s => s.ProjectId == projectId);
            if (settings == null || !settings.IsGroupAppointmentsEnabled)
            {
                return Ok(Array.Empty<object>());
            }

            var groups = await _context.GroupAppointments
                .Include(g => g.Bookings)
                .Where(g => g.ProjectId == projectId && g.IsActive)
                .OrderBy(g => g.DateTime)
                .ToListAsync();

            var result = groups.Select(g => new
            {
                g.Id,
                g.Name,
                g.DateTime,
                g.Capacity,
                g.Mode,
                BookedCount = g.Bookings.Count,
                SlotsLeft = Math.Max(0, g.Capacity - g.Bookings.Count)
            });

            return Ok(result);
        }

        [AllowAnonymous]
        [HttpPost("public/group-appointments/book")]
        public async Task<IActionResult> BookGroupSlot([FromBody] PublicBookRequest request)
        {
            _tenantContext.SetProjectId(request.ProjectId);

            var settings = await _context.ProjectSettings.FirstOrDefaultAsync(s => s.ProjectId == request.ProjectId);
            if (settings == null || !settings.IsGroupAppointmentsEnabled)
            {
                return BadRequest(new { error = "خدمة حجز المواعيد غير مفعلة لهذا المشروع" });
            }

            // Lock or verify capacity atomically
            var group = await _context.GroupAppointments
                .Include(g => g.Bookings)
                .FirstOrDefaultAsync(g => g.Id == request.GroupAppointmentId && g.ProjectId == request.ProjectId);

            if (group == null || !group.IsActive)
            {
                return BadRequest(new { error = "المجموعة المطلوبة غير متوفرة" });
            }


            if (group.Bookings.Count >= group.Capacity)
            {
                return BadRequest(new { error = "المجموعة ممتلئة" });
            }

            var cleanPhone = request.CustomerPhone.Trim();
            var alreadyBooked = group.Bookings.Any(b => b.CustomerPhone == cleanPhone);
            if (alreadyBooked)
            {
                return BadRequest(new { error = "أنت مسجل بالفعل في هذه المجموعة" });
            }

            // Resolve customer
            var customer = await _context.Customers
                .FirstOrDefaultAsync(c => c.ProjectId == request.ProjectId && c.PhoneNumber == cleanPhone);

            if (customer == null)
            {
                customer = new Customer
                {
                    ProjectId = request.ProjectId,
                    PhoneNumber = cleanPhone,
                    Name = request.CustomerName.Trim(),
                    City = string.Empty,
                    LeadScore = 10,
                    Tags = new[] { "حجز مجموعة" },
                    Notes = $"تم الحجز تلقائياً في مجموعة: {group.Name}"
                };
                _context.Customers.Add(customer);
                await _context.SaveChangesAsync();
            }
            else
            {
                var tagsList = customer.Tags?.ToList() ?? new List<string>();
                if (!tagsList.Contains("حجز مجموعة"))
                {
                    tagsList.Add("حجز مجموعة");
                    customer.Tags = tagsList.ToArray();
                }
                TimeZoneInfo projectZone = Shared.Infrastructure.TimezoneHelper.GetTimeZone(settings?.Timezone);
                var utcTime = DateTime.SpecifyKind(group.DateTime, DateTimeKind.Utc);
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, projectZone);
                customer.Notes = (customer.Notes ?? string.Empty) + $"\nتم حجز موعد في مجموعة: {group.Name} بتاريخ {localTime:yyyy-MM-dd HH:mm}";
                _context.Entry(customer).State = EntityState.Modified;
            }

            // Create booking
            var booking = new GroupAppointmentBooking
            {
                ProjectId = request.ProjectId,
                GroupAppointmentId = request.GroupAppointmentId,
                CustomerId = customer.Id,
                CustomerName = request.CustomerName.Trim(),
                CustomerPhone = cleanPhone
            };

            _context.GroupAppointmentBookings.Add(booking);
            await _context.SaveChangesAsync();

            // Create alert
            var alert = new NotificationAlert
            {
                ProjectId = request.ProjectId,
                UserId = Guid.Empty,
                Type = "Booking",
                Message = $"تم تسجيل حجز جديد: {booking.CustomerName} ({booking.CustomerPhone}) في المجموعة {group.Name}",
                IsRead = false
            };
            _context.NotificationAlerts.Add(alert);
            await _context.SaveChangesAsync();

            // Broadcast SignalR
            try
            {
                await _hubContext.Clients.Group($"project_{request.ProjectId}").SendAsync("ReceiveNotification", new
                {
                    id = alert.Id,
                    type = "Booking",
                    message = alert.Message,
                    createdAt = alert.CreatedAt.ToString("o"),
                    payload = new
                    {
                        customerId = customer.Id,
                        groupId = group.Id,
                        severity = "Medium"
                    }
                });

                await _hubContext.Clients.Group($"project_{request.ProjectId}").SendAsync("CustomerUpdated", new
                {
                    id = customer.Id,
                    projectId = customer.ProjectId,
                    phoneNumber = customer.PhoneNumber,
                    name = customer.Name,
                    city = customer.City,
                    leadScore = customer.LeadScore,
                    tags = customer.Tags,
                    notes = customer.Notes,
                    budget = customer.Budget,
                    interests = customer.Interests,
                    label = customer.Label
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GroupAppointmentsController] SignalR error: {ex.Message}");
            }

            // Broadcast Push Notifications via Firebase Cloud Messaging
            try
            {
                var redisKey = $"fcm_tokens:{request.ProjectId}";
                var tokens = await _redis.SetMembersAsync(redisKey);
                if (tokens.Length > 0)
                {
                    var tokenList = tokens.Select(t => t.ToString()).ToList();
                    var fcmMessage = new MulticastMessage
                    {
                        Tokens = tokenList,
                        Notification = new Notification
                        {
                            Title = "حجز جديد 📅",
                            Body = $"تم تسجيل حجز جديد باسم: {booking.CustomerName} في المجموعة {group.Name}"
                        },
                        Data = new Dictionary<string, string>
                        {
                            { "type", "Booking" },
                            { "projectId", request.ProjectId.ToString() }
                        }
                    };

                    await FirebaseMessaging.DefaultInstance.SendMulticastAsync(fcmMessage);
                    Console.WriteLine($"[GroupAppointmentsController] Dispatched push notifications to {tokenList.Count} registered devices.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GroupAppointmentsController] Failed to dispatch FCM push notifications: {ex.Message}");
            }

            return Ok(new
            {
                message = "تم الحجز بنجاح",
                bookingId = booking.Id
            });
        }
    }

    public class CreateGroupRequest
    {
        public string? Name { get; set; }
        public DateTime DateTime { get; set; }
        public int Capacity { get; set; }
        public bool IsActive { get; set; } = true;
        public string? Days { get; set; }
        public string? Mode { get; set; }
    }

    public class UpdateGroupRequest
    {
        public string? Name { get; set; }
        public DateTime? DateTime { get; set; }
        public int? Capacity { get; set; }
        public bool? IsActive { get; set; }
        public string? Days { get; set; }
        public string? Mode { get; set; }
    }

    public class PublicBookRequest
    {
        public Guid ProjectId { get; set; }
        public Guid GroupAppointmentId { get; set; }
        public string CustomerName { get; set; } = null!;
        public string CustomerPhone { get; set; } = null!;
    }
}
