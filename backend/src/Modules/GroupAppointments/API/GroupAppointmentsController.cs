using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Modules.Conversations.Hubs;
using Modules.GroupAppointments.Domain;
using Modules.GroupAppointments.Services;
using Shared.Infrastructure;
using Shared.Security;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using FirebaseAdmin.Messaging;
using StackExchange.Redis;
using Hangfire;
using Modules.CRM.Services;
using Shared.Queue;
using Modules.WhatsApp.Services;

namespace Modules.GroupAppointments.API
{
    [ApiController]
    [Authorize]
    [Route("api")]
    public class GroupAppointmentsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ITenantContext _tenantContext;
        private readonly IProjectAuthorizationService _authorization;
        private readonly GroupBookingCoordinator _bookingCoordinator;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly IDatabase _redis;
        private readonly WhatsAppAccountService _whatsAppAccounts;

        public GroupAppointmentsController(
            AppDbContext context,
            ITenantContext tenantContext,
            IProjectAuthorizationService authorization,
            GroupBookingCoordinator bookingCoordinator,
            IHubContext<NotificationHub> hubContext,
            IConnectionMultiplexer redis,
            WhatsAppAccountService whatsAppAccounts)
        {
            _context = context;
            _tenantContext = tenantContext;
            _authorization = authorization;
            _bookingCoordinator = bookingCoordinator;
            _hubContext = hubContext;
            _redis = redis.GetDatabase();
            _whatsAppAccounts = whatsAppAccounts;
        }

        // --- Admin/Agent Authorized Endpoints ---

        [HttpGet("group-appointments")]
        public async Task<IActionResult> GetGroups()
        {
            var projectId = _tenantContext.ProjectId;
            if (!_authorization.CanRead(User, projectId)) return Forbid();
            var settings = await _context.ProjectSettings.FirstOrDefaultAsync(s => s.ProjectId == projectId);
            var timezone = settings?.Timezone ?? "Africa/Cairo";

            var groups = await _context.GroupAppointments
                .Include(g => g.Bookings)
                .OrderBy(g => g.DateTime)
                .ToListAsync();

            var adjustedGroups = new List<GroupAppointment>();
            foreach (var g in groups)
            {
                var adjusted = await AdjustGroupIfPassedAsync(g, timezone);
                if (adjusted != null)
                {
                    adjustedGroups.Add(adjusted);
                }
            }

            var result = adjustedGroups.Select(g => new
            {
                g.Id,
                g.ProjectId,
                g.Name,
                g.DateTime,
                g.Capacity,
                g.IsActive,
                g.Days,
                g.Mode,
                g.InstructorName,
                g.FreeSessionDateTime,
                g.CourseSecondDateTime,
                g.WhatsAppAccountId,
                g.CreatedAt,
                g.UpdatedAt,
                BookedCount = g.Bookings.Count,
                Bookings = g.Bookings.OrderByDescending(b => b.CreatedAt).Select(b => new
                {
                    b.Id,
                    b.CustomerName,
                    b.CustomerPhone,
                    b.CustomerId,
                    b.CreatedAt,
                    b.IsAttended,
                    b.IsPaid
                })
            });

            return Ok(result);
        }

        [HttpGet("group-appointments/automation-overview")]
        public async Task<IActionResult> GetAutomationOverview()
        {
            var projectId = _tenantContext.ProjectId;
            if (!_authorization.CanRead(User, projectId)) return Forbid();
            var settings = await _context.ProjectSettings.FirstOrDefaultAsync(s => s.ProjectId == projectId);

            var groups = await _context.GroupAppointments
                .Include(g => g.Bookings)
                .OrderBy(g => g.DateTime)
                .ToListAsync();

            var groupBookingIds = groups
                .SelectMany(g => g.Bookings.Select(b => new { g.Id, b.CustomerId }))
                .ToList();

            var customerIds = groupBookingIds.Select(b => b.CustomerId).Distinct().ToList();
            var pendingFollowUps = await _context.FollowUps
                .Where(f => f.ProjectId == projectId && f.Status == "Pending" && customerIds.Contains(f.CustomerId))
                .ToListAsync();

            var isAutomationEnabled = settings?.IsWhatsAppGroupAutomationEnabled ?? false;
            var groupRows = groups.Select(group =>
            {
                var bookingCustomerIds = group.Bookings.Select(b => b.CustomerId).ToHashSet();
                var pendingFollowUpCount = pendingFollowUps.Count(f =>
                    bookingCustomerIds.Contains(f.CustomerId) &&
                    f.AppointmentTime.HasValue &&
                    f.AppointmentTime.Value == group.DateTime);
                var hasWhatsAppGroup = !string.IsNullOrWhiteSpace(group.WhatsAppGroupJid);
                var followUpStatus = !group.IsActive
                    ? "inactive"
                    : !isAutomationEnabled
                    ? "disabled"
                    : pendingFollowUpCount > 0
                        ? "active"
                        : hasWhatsAppGroup && group.Bookings.Count > 0
                            ? "created-no-pending"
                            : "waiting";

                return new
                {
                    group.Id,
                    group.Name,
                    group.Mode,
                    group.DateTime,
                    group.IsActive,
                    group.Capacity,
                    group.WhatsAppGroupJid,
                    group.WhatsAppGroupInviteLink,
                    group.WhatsAppAccountId,
                    BookedCount = group.Bookings.Count,
                    HasWhatsAppGroup = hasWhatsAppGroup,
                    PendingFollowUpCount = pendingFollowUpCount,
                    FollowUpStatus = followUpStatus
                };
            }).ToList();

            return Ok(new
            {
                IsEnabled = settings?.IsWhatsAppGroupAutomationEnabled ?? false,
                ManagerPhone = settings?.GroupAutomationManagerPhone ?? string.Empty,
                TotalGroups = groups.Count,
                ActiveGroups = groups.Count(g => g.IsActive),
                InactiveGroups = groups.Count(g => !g.IsActive),
                WhatsAppGroupsCreated = groups.Count(g => !string.IsNullOrWhiteSpace(g.WhatsAppGroupJid)),
                TotalBookings = groups.Sum(g => g.Bookings.Count),
                TotalBookingsInWhatsAppGroups = groups
                    .Where(g => !string.IsNullOrWhiteSpace(g.WhatsAppGroupJid))
                    .Sum(g => g.Bookings.Count),
                PendingFollowUps = groupRows.Sum(g => g.PendingFollowUpCount),
                Groups = groupRows
            });
        }

        [HttpPost("group-appointments/automation/run-now")]
        public IActionResult RunAutomationNow()
        {
            var projectId = _tenantContext.ProjectId;
            if (!_authorization.CanManageProject(User, projectId)) return Forbid();
            var jobId = BackgroundJob.Enqueue<FollowUpScheduler>(
                scheduler => scheduler.RunWhatsAppGroupAutomationLifecycleJobAsync(projectId));

            return Accepted(new
            {
                Message = "WhatsApp group automation lifecycle job queued.",
                JobId = jobId
            });
        }

        [HttpPost("group-appointments")]
        public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest request)
        {
            var projectId = _tenantContext.ProjectId;
            if (!_authorization.CanManageProject(User, projectId)) return Forbid();
            var whatsAppAccount = await _whatsAppAccounts.ResolveAsync(
                projectId,
                request.WhatsAppAccountId);
            if (whatsAppAccount is null)
                return BadRequest(new { code = "WHATSAPP_ACCOUNT_NOT_IN_PROJECT" });
            var mode = request.Mode ?? "offline";
            var autoName = mode == "online" ? "أونلاين" : "في السنتر";
            var group = new GroupAppointment
            {
                ProjectId = projectId,
                WhatsAppAccountId = whatsAppAccount.Id,
                Name = string.IsNullOrEmpty(request.Name) ? autoName : request.Name,
                DateTime = DateTime.SpecifyKind(request.DateTime, DateTimeKind.Utc),
                Capacity = request.Capacity,
                IsActive = request.IsActive,
                Days = request.Days ?? string.Empty,
                Mode = mode,
                InstructorName = request.InstructorName?.Trim() ?? string.Empty,
                FreeSessionDateTime = ToUtcOrNull(request.FreeSessionDateTime),
                CourseSecondDateTime = ToUtcOrNull(request.CourseSecondDateTime)
            };

            _context.GroupAppointments.Add(group);
            await _context.SaveChangesAsync();

            return Ok(group);
        }

        [HttpPut("group-appointments/{id}")]
        public async Task<IActionResult> UpdateGroup(Guid id, [FromBody] UpdateGroupRequest request)
        {
            var group = await _context.GroupAppointments.IgnoreQueryFilters()
                .FirstOrDefaultAsync(candidate => candidate.Id == id);
            if (group == null) return NotFound();
            if (!_authorization.CanManageProject(User, group.ProjectId)) return Forbid();
            if (request.WhatsAppAccountId.HasValue)
            {
                var whatsAppAccount = await _whatsAppAccounts.ResolveAsync(
                    group.ProjectId,
                    request.WhatsAppAccountId);
                if (whatsAppAccount is null)
                    return BadRequest(new { code = "WHATSAPP_ACCOUNT_NOT_IN_PROJECT" });
                if (group.WhatsAppAccountId != whatsAppAccount.Id
                    && (!string.IsNullOrWhiteSpace(group.WhatsAppGroupJid)
                        || await _context.FollowUps.IgnoreQueryFilters()
                            .AnyAsync(followUp => followUp.ProjectId == group.ProjectId
                                && followUp.AppointmentTime == group.DateTime
                                && followUp.WhatsAppAccountId == group.WhatsAppAccountId)))
                    return Conflict(new { code = "WHATSAPP_ACCOUNT_ALREADY_BOUND" });
                group.WhatsAppAccountId = whatsAppAccount.Id;
            }

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
            if (request.InstructorName != null)
            {
                group.InstructorName = request.InstructorName.Trim();
            }
            if (request.FreeSessionDateTime.HasValue)
            {
                group.FreeSessionDateTime = DateTime.SpecifyKind(request.FreeSessionDateTime.Value, DateTimeKind.Utc);
            }
            if (request.CourseSecondDateTime.HasValue)
            {
                group.CourseSecondDateTime = DateTime.SpecifyKind(request.CourseSecondDateTime.Value, DateTimeKind.Utc);
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new
                {
                    code = "GROUP_APPOINTMENT_CHANGED",
                    message = "The WhatsApp group was changed while this update was being saved. Refresh and try again."
                });
            }

            return Ok(group);
        }

        [HttpDelete("group-appointments/{id}")]
        public async Task<IActionResult> DeleteGroup(Guid id)
        {
            var group = await _context.GroupAppointments.IgnoreQueryFilters()
                .FirstOrDefaultAsync(candidate => candidate.Id == id);
            if (group == null) return NotFound();
            if (!_authorization.CanManageProject(User, group.ProjectId)) return Forbid();

            _context.GroupAppointments.Remove(group);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpPatch("group-appointments/{id}/toggle")]
        public async Task<IActionResult> ToggleGroup(Guid id)
        {
            var group = await _context.GroupAppointments.IgnoreQueryFilters()
                .FirstOrDefaultAsync(candidate => candidate.Id == id);
            if (group == null) return NotFound();
            if (!_authorization.CanManageProject(User, group.ProjectId)) return Forbid();

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
            if (!_authorization.CanManageProject(User, booking.ProjectId)) return Forbid();

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

        [HttpPatch("group-appointments/bookings/{bookingId}")]
        public async Task<IActionResult> UpdateBookingStatus(Guid bookingId, [FromBody] UpdateBookingStatusRequest request)
        {
            var booking = await _context.GroupAppointmentBookings
                .Include(b => b.GroupAppointment)
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (booking == null) return NotFound();
            if (!_authorization.CanManageProject(User, booking.ProjectId)) return Forbid();

            var previousPaid = booking.IsPaid;
            var previousAttended = booking.IsAttended;
            if (request.IsAttended.HasValue)
            {
                booking.IsAttended = request.IsAttended.Value;
            }

            if (request.IsPaid.HasValue)
            {
                booking.IsPaid = request.IsPaid.Value;

                if (booking.IsPaid)
                {
                    // Auto-cancel all pending follow-ups for this customer
                    var pendingFollowUps = await _context.FollowUps
                        .Where(f => f.CustomerId == booking.CustomerId && f.Status == "Pending" && f.ProjectId == booking.ProjectId)
                        .ToListAsync();

                    foreach (var f in pendingFollowUps)
                    {
                        f.Status = "Cancelled";
                        _context.Entry(f).State = EntityState.Modified;
                    }
                }
            }

            _context.Entry(booking).State = EntityState.Modified;
            if (previousPaid != booking.IsPaid || previousAttended != booking.IsAttended)
                IntegrationOutbox.Enqueue(_context, new AdvertisingBookingOutcomeChanged
                {
                    ProjectId = booking.ProjectId,
                    BookingId = booking.Id,
                    CustomerId = booking.CustomerId,
                    IsPaid = booking.IsPaid,
                    IsAttended = booking.IsAttended,
                    Value = 0m,
                    Currency = "EGP",
                    State = booking.IsPaid ? "Paid" : booking.IsAttended ? "Attended" : "Confirmed",
                    OutcomeOccurredAtUtc = DateTime.UtcNow,
                    SourceAggregateType = booking.GetType().Name,
                    SourceAggregateId = booking.Id,
                    SourceVersion = 1
                });
            await _context.SaveChangesAsync();

            // Broadcast SignalR update
            try
            {
                var bookedCount = await _context.GroupAppointmentBookings
                    .CountAsync(b => b.GroupAppointmentId == booking.GroupAppointmentId);

                await _hubContext.Clients.Group($"project_{booking.ProjectId}").SendAsync("GroupBookingUpdated", new
                {
                    groupId = booking.GroupAppointmentId,
                    groupName = booking.GroupAppointment?.Name,
                    customerPhone = booking.CustomerPhone,
                    customerName = booking.CustomerName,
                    newBookedCount = bookedCount,
                    capacity = booking.GroupAppointment?.Capacity,
                    isFull = booking.GroupAppointment != null && bookedCount >= booking.GroupAppointment.Capacity,
                    bookingId = booking.Id,
                    isAttended = booking.IsAttended,
                    isPaid = booking.IsPaid
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GroupAppointmentsController] SignalR booking update status error: {ex.Message}");
            }

            return Ok(new
            {
                booking.Id,
                booking.CustomerName,
                booking.CustomerPhone,
                booking.CustomerId,
                booking.IsAttended,
                booking.IsPaid,
                booking.CreatedAt
            });
        }

        [Authorize]
        [HttpPost("group-appointments/{groupId}/bookings/manual")]
        public async Task<IActionResult> CreateManualBooking(
            Guid groupId,
            [FromBody] ManualGroupBookingRequest request,
            CancellationToken cancellationToken = default)
        {
            var projectId = _tenantContext.ProjectId;
            if (!_authorization.CanManageProject(User, projectId))
            {
                return Forbid();
            }

            var validation = ValidateManualBookingRequest(request);
            if (validation.ErrorCode != null)
            {
                return BadRequest(new
                {
                    error = validation.ErrorMessage,
                    code = validation.ErrorCode
                });
            }
            var validRequest = validation.Request!;

            var result = await _bookingCoordinator.BookAsync(new GroupBookingCommand
            {
                ProjectId = projectId,
                GroupId = groupId,
                CustomerName = validRequest.CustomerName,
                CustomerPhone = validRequest.CustomerPhone,
                ExistingBookingPolicy = ExistingGroupBookingPolicy.Reject,
                Origin = GroupBookingOrigin.Manual,
                ExpirationPolicy = GroupBookingExpirationPolicy.Ignore,
                IsPaid = validRequest.IsPaid,
                IsAttended = validRequest.IsAttended,
                Notes = validRequest.Notes
            }, cancellationToken);

            if (result.Status == GroupBookingStatus.GroupNotFound)
            {
                return NotFound(new { error = "المجموعة غير موجودة.", code = "GROUP_NOT_FOUND" });
            }
            if (result.Status == GroupBookingStatus.GroupInactive)
            {
                return Conflict(new { error = "لا يمكن إضافة مشترك إلى مجموعة غير نشطة.", code = "GROUP_INACTIVE" });
            }
            if (result.Status == GroupBookingStatus.GroupFull)
            {
                return Conflict(new { error = "المجموعة ممتلئة ولا توجد أماكن متاحة.", code = "GROUP_FULL" });
            }
            if (result.Status == GroupBookingStatus.BookingAlreadyExists)
            {
                return ExistingManualBookingConflict(result.ExistingBooking!, groupId);
            }
            if (!result.Succeeded)
            {
                return BadRequest(new { error = "بيانات الحجز غير صالحة.", code = "MANUAL_BOOKING_INVALID" });
            }

            await BroadcastManualBookingAsync(result);
            return ManualBookingCreatedResponse(result);
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

            var timezone = settings.Timezone ?? "Africa/Cairo";

            var groups = await _context.GroupAppointments
                .Include(g => g.Bookings)
                .Where(g => g.ProjectId == projectId && g.IsActive)
                .OrderBy(g => g.DateTime)
                .ToListAsync();

            var adjustedGroups = new List<GroupAppointment>();
            foreach (var g in groups)
            {
                var adjusted = await AdjustGroupIfPassedAsync(g, timezone);
                if (adjusted != null && adjusted.IsActive)
                {
                    adjustedGroups.Add(adjusted);
                }
            }

            var result = adjustedGroups.Select(g => new
            {
                g.Id,
                g.Name,
                g.DateTime,
                g.Capacity,
                g.Mode,
                g.InstructorName,
                g.FreeSessionDateTime,
                g.CourseSecondDateTime,
                BookedCount = g.Bookings.Count,
                SlotsLeft = Math.Max(0, g.Capacity - g.Bookings.Count)
            });

            return Ok(result);
        }

        [AllowAnonymous]
        [HttpPost("public/group-appointments/book")]
        public async Task<IActionResult> BookGroupSlot(
            [FromBody] PublicBookRequest request,
            CancellationToken cancellationToken = default)
        {
            _tenantContext.SetProjectId(request.ProjectId);
            var customerName = request.CustomerName?.Trim() ?? string.Empty;
            if (customerName.Length is 0 or > 120 || request.CustomerPhone?.Length is null or > 64)
            {
                return BadRequest(new { error = "يرجى إدخال اسم صحيح ورقم هاتف صالح." });
            }

            var settings = await _context.ProjectSettings.FirstOrDefaultAsync(
                projectSettings => projectSettings.ProjectId == request.ProjectId,
                cancellationToken);
            if (settings == null || !settings.IsGroupAppointmentsEnabled)
            {
                return BadRequest(new { error = "خدمة حجز المواعيد غير مفعلة لهذا المشروع" });
            }

            var cleanPhone = GroupBookingPhone.Normalize(request.CustomerPhone);
            if (cleanPhone == null)
            {
                return BadRequest(new { error = "رقم الهاتف غير صالح. استخدم من 7 إلى 15 رقمًا بدون أي نص." });
            }

            var bookingResult = await _bookingCoordinator.BookAsync(new GroupBookingCommand
            {
                ProjectId = request.ProjectId,
                GroupId = request.GroupAppointmentId,
                CustomerName = customerName,
                CustomerPhone = cleanPhone,
                ExistingBookingPolicy = ExistingGroupBookingPolicy.Transfer,
                Origin = GroupBookingOrigin.Public,
                ExpirationPolicy = GroupBookingExpirationPolicy.RejectAfterTwentyFourHours,
                Timezone = settings.Timezone
            }, cancellationToken);
            if (bookingResult.Status is GroupBookingStatus.GroupNotFound or GroupBookingStatus.GroupInactive)
            {
                return BadRequest(new { error = "المجموعة المطلوبة غير متوفرة" });
            }
            if (bookingResult.Status == GroupBookingStatus.GroupExpired)
            {
                return BadRequest(new { error = "عذراً، لقد انتهى موعد هذه المجموعة بالفعل" });
            }
            if (bookingResult.Status == GroupBookingStatus.GroupFull)
            {
                return BadRequest(new { error = "المجموعة ممتلئة" });
            }
            if (bookingResult.Status == GroupBookingStatus.AlreadyInGroup)
            {
                var currentGroup = await _context.GroupAppointments
                    .AsNoTracking()
                    .Include(candidate => candidate.Bookings)
                    .FirstOrDefaultAsync(
                        candidate => candidate.ProjectId == request.ProjectId && candidate.Id == request.GroupAppointmentId,
                        cancellationToken);
                return Ok(currentGroup ?? bookingResult.Group);
            }
            if (!bookingResult.Succeeded)
            {
                return BadRequest(new { error = "يرجى إدخال اسم صحيح ورقم هاتف صالح." });
            }

            var group = bookingResult.Group!;
            var booking = bookingResult.Booking!;
            var customer = bookingResult.Customer!;
            var alert = bookingResult.Alert!;

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

        [HttpGet("group-appointments/instructors")]
        public async Task<IActionResult> GetInstructors()
        {
            var projectId = _tenantContext.ProjectId;
            if (!_authorization.CanRead(User, projectId)) return Forbid();
            var settings = await _context.ProjectSettings.FirstOrDefaultAsync(s => s.ProjectId == projectId);
            var instructors = SplitInstructors(settings?.ActiveInstructors);
            return Ok(new { instructors });
        }

        [HttpPut("group-appointments/instructors")]
        public async Task<IActionResult> UpdateInstructors([FromBody] UpdateInstructorsRequest request)
        {
            var projectId = _tenantContext.ProjectId;
            if (!_authorization.CanManageProject(User, projectId)) return Forbid();
            var settings = await _context.ProjectSettings.FirstOrDefaultAsync(s => s.ProjectId == projectId);
            if (settings == null)
            {
                return NotFound(new { error = "Settings not found for this project" });
            }

            var instructors = (request.Instructors ?? Array.Empty<string>())
                .Select(i => i.Trim())
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .ToArray();

            settings.ActiveInstructors = string.Join("\n", instructors);
            settings.UpdatedAt = DateTime.UtcNow;
            _context.Entry(settings).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            return Ok(new { instructors });
        }

        private async Task<GroupAppointment?> AdjustGroupIfPassedAsync(GroupAppointment group, string timezone)
        {
            var projectZone = Shared.Infrastructure.TimezoneHelper.GetTimeZone(timezone);
            var localGroupDateTime = TimeZoneInfo.ConvertTimeFromUtc(group.DateTime, projectZone);
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, projectZone);

            if (localNow > localGroupDateTime)
            {
                var timeDiff = localNow - localGroupDateTime;
                if (timeDiff.TotalHours >= 24)
                {
                    if (group.IsActive)
                    {
                        group.IsActive = false;
                        _context.Entry(group).State = EntityState.Modified;
                        await _context.SaveChangesAsync();
                        Console.WriteLine($"[GroupAppointments] Deactivated expired group {group.Id} because 24 hours have passed.");
                    }
                }
            }
            return group;
        }

        private static DateTime? ToUtcOrNull(DateTime? value)
        {
            return value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
        }

        private static string[] SplitInstructors(string? value)
        {
            return (value ?? string.Empty)
                .Split(new[] { '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static ManualBookingValidation ValidateManualBookingRequest(ManualGroupBookingRequest request)
        {
            var customerName = request.CustomerName?.Trim() ?? string.Empty;
            var notes = request.Notes?.Trim();
            if (customerName.Length is 0 or > 120 || request.CustomerPhone?.Length is null or > 64 || notes?.Length > 2000)
            {
                return ManualBookingValidation.Invalid(
                    "MANUAL_BOOKING_INVALID",
                    "يرجى إدخال اسم صحيح ورقم هاتف صالح، وألا تتجاوز الملاحظات الحد المسموح.");
            }

            var customerPhone = GroupBookingPhone.Normalize(request.CustomerPhone);
            return customerPhone == null
                ? ManualBookingValidation.Invalid("PHONE_INVALID", "رقم الهاتف غير صالح. استخدم من 7 إلى 15 رقمًا بدون أي نص.")
                : ManualBookingValidation.Valid(new(customerName, customerPhone, request.IsPaid, request.IsAttended, notes));
        }

        private ConflictObjectResult ExistingManualBookingConflict(
            GroupAppointmentBooking existingBooking,
            Guid requestedGroupId) =>
            Conflict(new
            {
                error = existingBooking.GroupAppointmentId == requestedGroupId
                    ? "هذا المشترك موجود بالفعل في المجموعة."
                    : $"هذا المشترك محجوز بالفعل في مجموعة {existingBooking.GroupAppointment?.Name ?? "أخرى"}. احذف الحجز أو انقله يدويًا أولًا.",
                code = "BOOKING_ALREADY_EXISTS",
                existingGroupId = existingBooking.GroupAppointmentId,
                existingGroupName = existingBooking.GroupAppointment?.Name
            });

        private async Task BroadcastManualBookingAsync(GroupBookingResult createdBooking)
        {
            var group = createdBooking.Group!;
            var booking = createdBooking.Booking!;
            var customer = createdBooking.Customer!;
            try
            {
                await _hubContext.Clients.Group($"project_{group.ProjectId}").SendAsync("GroupBookingUpdated", new
                {
                    groupId = group.Id,
                    groupName = group.Name,
                    customerPhone = booking.CustomerPhone,
                    customerName = booking.CustomerName,
                    newBookedCount = createdBooking.BookedCount,
                    capacity = group.Capacity,
                    isFull = createdBooking.BookedCount >= group.Capacity,
                    bookingId = booking.Id,
                    isAttended = booking.IsAttended,
                    isPaid = booking.IsPaid
                });

                await _hubContext.Clients.Group($"project_{group.ProjectId}").SendAsync("CustomerUpdated", new
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
                // The booking is committed; realtime delivery failure must not make clients retry the write.
                Console.WriteLine($"[GroupAppointmentsController] SignalR manual booking error: {ex.Message}");
            }
        }

        private ObjectResult ManualBookingCreatedResponse(GroupBookingResult createdBooking)
        {
            var group = createdBooking.Group!;
            var booking = createdBooking.Booking!;
            return StatusCode(StatusCodes.Status201Created, new
            {
                message = "تمت إضافة المشترك إلى المجموعة بنجاح.",
                booking = new
                {
                    booking.Id,
                    booking.CustomerId,
                    booking.CustomerName,
                    booking.CustomerPhone,
                    booking.IsPaid,
                    booking.IsAttended,
                    booking.CreatedAt
                },
                group = new
                {
                    group.Id,
                    group.Name,
                    group.Capacity,
                    bookedCount = createdBooking.BookedCount,
                    slotsLeft = Math.Max(0, group.Capacity - createdBooking.BookedCount),
                    isFull = createdBooking.BookedCount >= group.Capacity
                }
            });
        }

        private sealed record ValidatedManualBookingRequest(
            string CustomerName,
            string CustomerPhone,
            bool IsPaid,
            bool IsAttended,
            string? Notes);

        private sealed record ManualBookingValidation(
            ValidatedManualBookingRequest? Request,
            string? ErrorCode,
            string? ErrorMessage)
        {
            public static ManualBookingValidation Valid(ValidatedManualBookingRequest request) => new(request, null, null);

            public static ManualBookingValidation Invalid(string errorCode, string errorMessage) =>
                new(null, errorCode, errorMessage);
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
        public string? InstructorName { get; set; }
        public DateTime? FreeSessionDateTime { get; set; }
        public DateTime? CourseSecondDateTime { get; set; }
        public Guid? WhatsAppAccountId { get; set; }
    }

    public class UpdateGroupRequest
    {
        public string? Name { get; set; }
        public DateTime? DateTime { get; set; }
        public int? Capacity { get; set; }
        public bool? IsActive { get; set; }
        public string? Days { get; set; }
        public string? Mode { get; set; }
        public string? InstructorName { get; set; }
        public DateTime? FreeSessionDateTime { get; set; }
        public DateTime? CourseSecondDateTime { get; set; }
        public Guid? WhatsAppAccountId { get; set; }
    }

    public class PublicBookRequest
    {
        public Guid ProjectId { get; set; }
        public Guid GroupAppointmentId { get; set; }
        public string CustomerName { get; set; } = null!;
        public string CustomerPhone { get; set; } = null!;
    }

    public class UpdateBookingStatusRequest
    {
        public bool? IsAttended { get; set; }
        public bool? IsPaid { get; set; }
    }

    public class ManualGroupBookingRequest
    {
        public string? CustomerName { get; set; }
        public string? CustomerPhone { get; set; }
        public bool IsPaid { get; set; }
        public bool IsAttended { get; set; }
        public string? Notes { get; set; }
    }

    public class UpdateInstructorsRequest
    {
        public string[]? Instructors { get; set; }
    }
}
