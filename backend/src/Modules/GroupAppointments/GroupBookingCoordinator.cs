using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Modules.Conversations.Domain;
using Modules.GroupAppointments.Domain;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Queue;

namespace Modules.GroupAppointments.Services;

public enum ExistingGroupBookingPolicy
{
    Reject,
    Transfer
}

public enum GroupBookingOrigin
{
    Manual,
    Public,
    Ai
}

public enum GroupBookingExpirationPolicy
{
    Ignore,
    RejectAfterTwentyFourHours
}

public enum GroupBookingStatus
{
    Created,
    Transferred,
    AlreadyInGroup,
    InvalidRequest,
    GroupNotFound,
    GroupInactive,
    GroupExpired,
    GroupFull,
    BookingAlreadyExists
}

public sealed class GroupBookingCommand
{
    public Guid ProjectId { get; init; }
    public Guid GroupId { get; init; }
    public required string CustomerName { get; init; }
    public required string CustomerPhone { get; init; }
    public Guid? KnownCustomerId { get; init; }
    public ExistingGroupBookingPolicy ExistingBookingPolicy { get; init; }
    public GroupBookingOrigin Origin { get; init; }
    public GroupBookingExpirationPolicy ExpirationPolicy { get; init; }
    public bool IsPaid { get; init; }
    public bool IsAttended { get; init; }
    public string? Notes { get; init; }
    public string? Timezone { get; init; }
}

public sealed record GroupBookingResult(
    GroupBookingStatus Status,
    string? CanonicalPhone,
    GroupAppointment? Group = null,
    GroupAppointmentBooking? Booking = null,
    Customer? Customer = null,
    int BookedCount = 0,
    GroupAppointmentBooking? ExistingBooking = null,
    NotificationAlert? Alert = null)
{
    public bool Succeeded => Status is GroupBookingStatus.Created or
        GroupBookingStatus.Transferred or
        GroupBookingStatus.AlreadyInGroup;

    public bool Changed => Status is GroupBookingStatus.Created or GroupBookingStatus.Transferred;
}

public static class GroupBookingPhone
{
    public static string? Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone) || phone.Contains("@lid", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var normalized = new string(phone.Select(NormalizeSupportedDigit).ToArray());
        if (normalized.Any(character =>
                !(character is >= '0' and <= '9') &&
                !IsSupportedWhitespace(character) &&
                character is not '+' and not '-' and not '(' and not ')'))
        {
            return null;
        }

        var compact = new string(normalized
            .Where(character => !IsSupportedWhitespace(character) && character is not '-' and not '(' and not ')')
            .ToArray());
        if (compact.StartsWith('+'))
        {
            compact = compact[1..];
        }
        else if (compact.StartsWith("00", StringComparison.Ordinal))
        {
            compact = compact[2..];
        }

        if (compact.Length == 11 && compact.StartsWith("01", StringComparison.Ordinal))
        {
            compact = "2" + compact;
        }
        else if (compact.Length == 10 && compact.StartsWith('1'))
        {
            compact = "20" + compact;
        }

        return compact.Length is >= 7 and <= 15 &&
               compact[0] is >= '1' and <= '9' &&
               compact.All(character => character is >= '0' and <= '9')
            ? compact
            : null;
    }

    private static char NormalizeSupportedDigit(char character) => character switch
    {
        >= '\u0660' and <= '\u0669' => (char)('0' + character - '\u0660'),
        >= '\u06F0' and <= '\u06F9' => (char)('0' + character - '\u06F0'),
        _ => character
    };

    private static bool IsSupportedWhitespace(char character) =>
        character is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    public static string[] Candidates(string canonicalPhone)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal)
        {
            canonicalPhone,
            "+" + canonicalPhone,
            "00" + canonicalPhone
        };
        if (canonicalPhone.Length == 12 && canonicalPhone.StartsWith("20", StringComparison.Ordinal))
        {
            candidates.Add("0" + canonicalPhone[2..]);
            candidates.Add(canonicalPhone[2..]);
        }
        return candidates.ToArray();
    }
}

public sealed class GroupBookingCoordinator(AppDbContext context, IEventBus? eventBus = null)
{
    private readonly AppDbContext _context = context;
    private readonly IEventBus? _eventBus = eventBus;

    public async Task<GroupBookingResult> BookAsync(
        GroupBookingCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.CustomerPhone is null or { Length: > 64 } || command.Notes?.Length > 2000)
        {
            return new(GroupBookingStatus.InvalidRequest, null);
        }

        var customerName = command.CustomerName?.Trim() ?? string.Empty;
        var canonicalPhone = GroupBookingPhone.Normalize(command.CustomerPhone);
        if (customerName.Length is 0 or > 120 || canonicalPhone == null)
        {
            return new(GroupBookingStatus.InvalidRequest, canonicalPhone);
        }

        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        if (UsesPostgres())
        {
            await LockBookingIdentityAsync(command.ProjectId, command.GroupId, canonicalPhone, cancellationToken);
        }

        var group = await FindFreshGroupAsync(command.ProjectId, command.GroupId, cancellationToken);
        if (group == null)
        {
            await RollbackAsync(transaction, cancellationToken);
            return new(GroupBookingStatus.GroupNotFound, canonicalPhone);
        }
        if (!group.IsActive)
        {
            await RollbackAsync(transaction, cancellationToken);
            return new(GroupBookingStatus.GroupInactive, canonicalPhone, group);
        }
        if (command.ExpirationPolicy == GroupBookingExpirationPolicy.RejectAfterTwentyFourHours &&
            DateTime.UtcNow - DateTime.SpecifyKind(group.DateTime, DateTimeKind.Utc) >= TimeSpan.FromHours(24))
        {
            group.IsActive = false;
            await _context.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return new(GroupBookingStatus.GroupExpired, canonicalPhone, group);
        }

        var bookedCountBeforeWrite = await _context.GroupAppointmentBookings.CountAsync(
            booking => booking.ProjectId == command.ProjectId && booking.GroupAppointmentId == group.Id,
            cancellationToken);

        var customer = await FindCustomerAsync(command, canonicalPhone, cancellationToken);
        var existingBooking = await FindBookingAsync(
            command.ProjectId,
            canonicalPhone,
            customer?.Id ?? command.KnownCustomerId,
            cancellationToken);
        if (existingBooking != null && command.ExistingBookingPolicy == ExistingGroupBookingPolicy.Reject)
        {
            await RollbackAsync(transaction, cancellationToken);
            return new(
                GroupBookingStatus.BookingAlreadyExists,
                canonicalPhone,
                group,
                BookedCount: bookedCountBeforeWrite,
                ExistingBooking: existingBooking);
        }

        var alreadyInGroup = existingBooking?.GroupAppointmentId == group.Id;
        if (!alreadyInGroup && bookedCountBeforeWrite >= group.Capacity)
        {
            await RollbackAsync(transaction, cancellationToken);
            return new(GroupBookingStatus.GroupFull, canonicalPhone, group, BookedCount: bookedCountBeforeWrite);
        }

        if (customer == null && existingBooking != null)
        {
            customer = await _context.Customers.FirstOrDefaultAsync(
                candidate => candidate.ProjectId == command.ProjectId && candidate.Id == existingBooking.CustomerId,
                cancellationToken);
        }
        customer ??= CreateCustomer(command.ProjectId);
        ApplyCustomerChanges(customer, command, group, customerName, canonicalPhone, existingBooking, alreadyInGroup);

        if (alreadyInGroup)
        {
            existingBooking!.CustomerId = customer.Id;
            existingBooking.CustomerName = customerName;
            existingBooking.CustomerPhone = canonicalPhone;
            await SaveWithoutEntityIndexEventsAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            await PublishCustomerIndexAfterCommitAsync(customer);
            return new(
                GroupBookingStatus.AlreadyInGroup,
                canonicalPhone,
                group,
                existingBooking,
                customer,
                bookedCountBeforeWrite,
                existingBooking);
        }

        var bookingStatus = existingBooking == null
            ? GroupBookingStatus.Created
            : GroupBookingStatus.Transferred;
        var booking = existingBooking ?? new GroupAppointmentBooking
        {
            ProjectId = command.ProjectId
        };
        booking.GroupAppointmentId = group.Id;
        booking.CustomerId = customer.Id;
        booking.CustomerName = customerName;
        booking.CustomerPhone = canonicalPhone;
        booking.CreatedAt = DateTime.UtcNow;
        if (existingBooking == null)
        {
            booking.IsPaid = command.IsPaid;
            booking.IsAttended = command.IsAttended;
            _context.GroupAppointmentBookings.Add(booking);
            EnqueueBookingOutcome(booking);
        }
        else
        {
            booking.IsAttended = false;
        }

        if (command.Origin == GroupBookingOrigin.Manual && booking.IsPaid)
        {
            await CancelPendingFollowUpsAsync(command.ProjectId, customer.Id, cancellationToken);
        }

        var alert = command.Origin == GroupBookingOrigin.Public
            ? CreatePublicBookingAlert(command.ProjectId, group.Name, booking, bookingStatus)
            : null;
        if (alert != null)
        {
            _context.NotificationAlerts.Add(alert);
        }

        await SaveWithoutEntityIndexEventsAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        await PublishCustomerIndexAfterCommitAsync(customer);
        return new(
            bookingStatus,
            canonicalPhone,
            group,
            booking,
            customer,
            bookedCountBeforeWrite + 1,
            existingBooking,
            alert);
    }

    public Task<int> CountBookingsAsync(Guid projectId, Guid groupId, CancellationToken cancellationToken = default) =>
        _context.GroupAppointmentBookings.CountAsync(
            booking => booking.ProjectId == projectId && booking.GroupAppointmentId == groupId,
            cancellationToken);

    private async Task<GroupAppointment?> FindFreshGroupAsync(
        Guid projectId,
        Guid groupId,
        CancellationToken cancellationToken)
    {
        var trackedGroup = _context.ChangeTracker.Entries<GroupAppointment>()
            .FirstOrDefault(entry => entry.Entity.ProjectId == projectId && entry.Entity.Id == groupId);
        if (trackedGroup != null)
        {
            await trackedGroup.ReloadAsync(cancellationToken);
            return trackedGroup.State == EntityState.Detached ? null : trackedGroup.Entity;
        }

        return await _context.GroupAppointments.FirstOrDefaultAsync(
            candidate => candidate.ProjectId == projectId && candidate.Id == groupId,
            cancellationToken);
    }

    private async Task LockBookingIdentityAsync(
        Guid projectId,
        Guid groupId,
        string canonicalPhone,
        CancellationToken cancellationToken)
    {
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"GroupAppointments\" WHERE \"Id\" = {groupId} AND \"ProjectId\" = {projectId} FOR UPDATE",
            cancellationToken);
        var phoneLockIdentity = $"group-booking:{projectId:N}:{canonicalPhone}";
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({phoneLockIdentity}, 0))",
            cancellationToken);
    }

    private async Task<Customer?> FindCustomerAsync(
        GroupBookingCommand command,
        string canonicalPhone,
        CancellationToken cancellationToken)
    {
        if (command.KnownCustomerId.HasValue)
        {
            var knownCustomer = await _context.Customers.FirstOrDefaultAsync(
                candidate => candidate.ProjectId == command.ProjectId && candidate.Id == command.KnownCustomerId.Value,
                cancellationToken);
            if (knownCustomer != null)
            {
                return knownCustomer;
            }
        }

        if (UsesPostgres())
        {
            return await _context.Customers.FirstOrDefaultAsync(
                candidate => candidate.ProjectId == command.ProjectId &&
                             EF.Property<string>(candidate, GroupBookingPhoneFields.CustomerCanonical) == canonicalPhone,
                cancellationToken);
        }

        var candidates = GroupBookingPhone.Candidates(canonicalPhone);
        return await _context.Customers.FirstOrDefaultAsync(
            candidate => candidate.ProjectId == command.ProjectId && candidates.Contains(candidate.PhoneNumber),
            cancellationToken);
    }

    private async Task<GroupAppointmentBooking?> FindBookingAsync(
        Guid projectId,
        string canonicalPhone,
        Guid? customerId,
        CancellationToken cancellationToken)
    {
        GroupAppointmentBooking? booking;
        if (UsesPostgres())
        {
            booking = await _context.GroupAppointmentBookings
                .Include(candidate => candidate.GroupAppointment)
                .Where(candidate => candidate.ProjectId == projectId &&
                                    EF.Property<string>(candidate, GroupBookingPhoneFields.BookingCanonical) == canonicalPhone)
                .OrderByDescending(candidate => candidate.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            var candidates = GroupBookingPhone.Candidates(canonicalPhone);
            booking = await _context.GroupAppointmentBookings
                .Include(candidate => candidate.GroupAppointment)
                .Where(candidate => candidate.ProjectId == projectId && candidates.Contains(candidate.CustomerPhone))
                .OrderByDescending(candidate => candidate.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (booking != null || !customerId.HasValue)
        {
            return booking;
        }

        return await _context.GroupAppointmentBookings
            .Include(candidate => candidate.GroupAppointment)
            .Where(candidate => candidate.ProjectId == projectId && candidate.CustomerId == customerId.Value)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Customer CreateCustomer(Guid projectId)
    {
        var customer = new Customer
        {
            ProjectId = projectId,
            PhoneNumber = string.Empty,
            Name = string.Empty,
            City = string.Empty,
            LeadScore = 10,
            Tags = Array.Empty<string>(),
            Notes = string.Empty
        };
        _context.Customers.Add(customer);
        return customer;
    }

    private static void ApplyCustomerChanges(
        Customer customer,
        GroupBookingCommand command,
        GroupAppointment group,
        string customerName,
        string canonicalPhone,
        GroupAppointmentBooking? existingBooking,
        bool alreadyInGroup)
    {
        customer.Name = customerName;
        customer.PhoneNumber = canonicalPhone;
        customer.Tags = (customer.Tags ?? Array.Empty<string>())
            .Append("حجز مجموعة")
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (alreadyInGroup)
        {
            return;
        }

        var note = BuildCustomerNote(command, group, existingBooking != null);
        customer.Notes = string.IsNullOrWhiteSpace(customer.Notes)
            ? note
            : $"{customer.Notes.Trim()}\n{note}";
    }

    private static string BuildCustomerNote(
        GroupBookingCommand command,
        GroupAppointment group,
        bool isTransfer)
    {
        if (command.Origin == GroupBookingOrigin.Manual)
        {
            var note = $"تمت إضافته يدويًا إلى مجموعة: {group.Name}.";
            return string.IsNullOrWhiteSpace(command.Notes)
                ? note
                : $"{note}\nملاحظة الحجز اليدوي: {command.Notes.Trim()}";
        }

        if (command.Origin == GroupBookingOrigin.Ai)
        {
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.UtcNow,
                TimezoneHelper.GetTimeZone(command.Timezone));
            return $"تم حجز موعد في مجموعة: {group.Name} (تلقائياً بالـ AI) بتاريخ {localNow:yyyy-MM-dd HH:mm}";
        }

        var projectZone = TimezoneHelper.GetTimeZone(command.Timezone);
        var groupUtc = DateTime.SpecifyKind(group.DateTime, DateTimeKind.Utc);
        var localGroupTime = TimeZoneInfo.ConvertTimeFromUtc(groupUtc, projectZone);
        return isTransfer
            ? $"تم نقل حجز الطالب من مجموعة إلى مجموعة: {group.Name} بتاريخ {localGroupTime:yyyy-MM-dd HH:mm}"
            : $"تم حجز موعد في مجموعة: {group.Name} بتاريخ {localGroupTime:yyyy-MM-dd HH:mm}";
    }

    private async Task CancelPendingFollowUpsAsync(
        Guid projectId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var pendingFollowUps = await _context.FollowUps
            .Where(followUp => followUp.ProjectId == projectId &&
                               followUp.CustomerId == customerId &&
                               followUp.Status == "Pending")
            .ToListAsync(cancellationToken);
        foreach (var followUp in pendingFollowUps)
        {
            followUp.Status = "Cancelled";
        }
    }

    private static NotificationAlert CreatePublicBookingAlert(
        Guid projectId,
        string groupName,
        GroupAppointmentBooking booking,
        GroupBookingStatus status) => new()
        {
            ProjectId = projectId,
            UserId = Guid.Empty,
            Type = "Booking",
            Message = status == GroupBookingStatus.Transferred
                ? $"تم نقل حجز الطالب: {booking.CustomerName} ({booking.CustomerPhone}) إلى المجموعة {groupName}"
                : $"تم تسجيل حجز جديد: {booking.CustomerName} ({booking.CustomerPhone}) في المجموعة {groupName}",
            IsRead = false
        };

    private void EnqueueBookingOutcome(GroupAppointmentBooking booking) =>
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
            SourceAggregateType = nameof(GroupAppointmentBooking),
            SourceAggregateId = booking.Id,
            SourceVersion = 1
        });

    private async Task SaveWithoutEntityIndexEventsAsync(CancellationToken cancellationToken)
    {
        using var suppression = _context.SuppressEntityIndexEvents();
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task PublishCustomerIndexAfterCommitAsync(Customer customer)
    {
        if (_eventBus == null)
        {
            return;
        }

        try
        {
            await _eventBus.PublishAsync(new EntityIndexedEvent
            {
                EntityId = customer.Id,
                EntityType = "Customer",
                ProjectId = customer.ProjectId,
                Action = "Upsert",
                ContentJson = JsonSerializer.Serialize(customer)
            });
        }
        catch (Exception exception)
        {
            Console.WriteLine($"⚠️ Failed to publish committed customer index event: {exception.Message}");
        }
    }

    private bool UsesPostgres() =>
        string.Equals(
            _context.Database.ProviderName,
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            StringComparison.Ordinal);

    private static Task CommitAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken) =>
        transaction?.CommitAsync(cancellationToken) ?? Task.CompletedTask;

    private static Task RollbackAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken) =>
        transaction?.RollbackAsync(cancellationToken) ?? Task.CompletedTask;
}
