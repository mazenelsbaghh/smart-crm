using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Hubs;
using Modules.GroupAppointments.Domain;
using Modules.GroupAppointments.Services;
using Shared.Infrastructure;
using Modules.WhatsApp.Services;

namespace Modules.AI.Services;

public sealed class AiGroupBookingRequest
{
    public Guid ProjectId { get; init; }
    public Guid GroupId { get; init; }
    public Guid? RequesterCustomerId { get; init; }
    public Guid? SourceConversationId { get; init; }
    public SuggestedGroupBookingPerson[]? SuggestedPeople { get; init; } = [];
    public string? Timezone { get; init; }
    public bool RequesterAllowsTransfer { get; init; }
    public string? RequesterCity { get; init; }
    public string? AttendanceMode { get; init; }
    public IReadOnlyList<string> CustomerMessages { get; init; } = [];
}

public enum AiGroupBookingFailure
{
    None,
    InvalidSuggestion,
    GroupUnavailable,
    GroupExpired,
    GroupFull,
    TemporaryFailure,
    BookingAlreadyExists,
    FriendNotOnWhatsApp,
    WhatsAppVerificationUnavailable,
    OnlineRequired,
    CityRequired,
    AttendanceModeMismatch,
    TimeChoiceMismatch
}

public sealed record AiGroupBookingResult(
    bool Succeeded,
    int DisplayedRemainingPlaces,
    AiGroupBookingFailure Failure = AiGroupBookingFailure.None)
{
    public string[] BookedPeople { get; init; } = [];
    public string[] UnbookedPeople { get; init; } = [];
    public string? ConfirmedAppointmentDetails { get; init; }

    public string? CustomerReplyOverride
    {
        get
        {
            if (Failure == AiGroupBookingFailure.None)
            {
                return null;
            }

            var attendanceGuidance = Failure switch
            {
                AiGroupBookingFailure.OnlineRequired => "الحجز ده لازم يكون أونلاين. المجموعة دي في السنتر ومش متاحة للحضور أونلاين؛ اختار معاد من مجموعات الأونلاين علشان نأكد الحجز.",
                AiGroupBookingFailure.CityRequired => "السنتر في الإسكندرية. قبل تأكيد الحجز، هل الشخص المطلوب حجزه مقيم في الإسكندرية ويقدر يحضر السنتر هناك؟ خارج الإسكندرية الحضور أونلاين فقط.",
                AiGroupBookingFailure.AttendanceModeMismatch => "المجموعة دي أونلاين ومش مطابقة لاختيار الحضور في السنتر. اختار مجموعة بنفس طريقة الحضور المطلوبة علشان نأكد الحجز.",
                AiGroupBookingFailure.TimeChoiceMismatch => "المعاد المقترح مش مطابق للساعة اللي حضرتك اخترتها، فمأكدتش حجز جديد ولا غيرت حجزك. محتاجين نحدد موعد متاح يطابق اختيار حضرتك؛ تحب نراجع المواعيد المناسبة؟",
                _ => null
            };
            if (attendanceGuidance is not null)
                return BookedPeople.Length == 0 ? attendanceGuidance
                    : $"تم تسجيل {string.Join("، ", BookedPeople)}، لكن باقي الأشخاص لم يتم حجزهم. {attendanceGuidance}";

            var guidance = Failure switch
            {
                AiGroupBookingFailure.InvalidSuggestion =>
                    "ابعت الاسم ورقم الموبايل الصحيح علشان نكمل الحجز.",
                AiGroupBookingFailure.FriendNotOnWhatsApp =>
                    "في رقم مش مسجل على واتساب؛ ابعت رقم عليه واتساب علشان نكمل الحجز.",
                AiGroupBookingFailure.WhatsAppVerificationUnavailable =>
                    "مش قادر أتحقق من واتساب دلوقتي؛ هنكمل تأكيد باقي الحجز بعد ما نقدر نتحقق.",
                AiGroupBookingFailure.GroupFull =>
                    "المجموعة اكتملت؛ اختار معاد تاني للباقي.",
                AiGroupBookingFailure.BookingAlreadyExists =>
                    "في حجز موجود بالفعل للرقم ده؛ محتاج مراجعة المسؤول قبل تغييره.",
                AiGroupBookingFailure.GroupUnavailable or AiGroupBookingFailure.GroupExpired =>
                    "المجموعة مش متاحة حاليًا؛ اختار معاد تاني.",
                _ => "حصلت مشكلة مؤقتة؛ حاول تاني بعد شوية من فضلك."
            };
            if (BookedPeople.Length == 0)
            {
                return Failure switch
                {
                    AiGroupBookingFailure.InvalidSuggestion =>
                        "محتاج الاسم ورقم موبايل صحيح علشان أقدر أسجل الحجز. ابعتهُم لي من فضلك.",
                    AiGroupBookingFailure.FriendNotOnWhatsApp =>
                        "في رقم مش مسجل على واتساب، والحجز ما اتأكدش. ابعت رقم عليه واتساب علشان نكمل.",
                    AiGroupBookingFailure.WhatsAppVerificationUnavailable =>
                        "مش قادر أتحقق إن الرقم عليه واتساب دلوقتي، فالحجز لسه ما اتأكدش. نكمل لما التحقق يكون متاح.",
                    AiGroupBookingFailure.GroupFull =>
                        "للأسف المجموعة دي اكتملت دلوقتي. اختار معاد تاني وأنا أسجلك فيه.",
                    AiGroupBookingFailure.BookingAlreadyExists =>
                        "في حجز موجود بالفعل للرقم ده؛ محتاج مراجعة المسؤول قبل تغييره.",
                    AiGroupBookingFailure.GroupUnavailable or AiGroupBookingFailure.GroupExpired =>
                        "المجموعة دي مش متاحة للحجز حاليًا. اختار معاد تاني وأنا أسجلك فيه.",
                    _ => "حصلت مشكلة مؤقتة ومقدرتش أثبت الحجز. حاول تاني بعد شوية من فضلك."
                };
            }

            var bookedNames = string.Join("، ", BookedPeople);
            var unbookedNames = UnbookedPeople.Length == 0
                ? "باقي الأشخاص"
                : string.Join("، ", UnbookedPeople);
            return $"تم تسجيل {bookedNames} بنجاح، لكن مقدرتش أسجل {unbookedNames}. {guidance}";
        }
    }
}

public sealed class AiGroupBookingOrchestrator(
    AppDbContext context,
    GroupBookingCoordinator bookingCoordinator,
    IHubContext<NotificationHub> hubContext,
    ILogger<AiGroupBookingOrchestrator> logger,
    IWhatsAppBookingNumberVerifier? numberVerifier = null)
{
    private readonly AppDbContext _context = context;
    private readonly GroupBookingCoordinator _bookingCoordinator = bookingCoordinator;
    private readonly IHubContext<NotificationHub> _hubContext = hubContext;
    private readonly ILogger<AiGroupBookingOrchestrator> _logger = logger;

    public async Task<AiGroupBookingResult> BookSuggestedPeopleAsync(
        AiGroupBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        var group = await _context.GroupAppointments
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate =>
                candidate.Id == request.GroupId &&
                candidate.ProjectId == request.ProjectId,
                cancellationToken);
        if (group == null)
        {
            _logger.LogWarning(
                "AI auto-booking failed: active group {GroupId} was not found in project {ProjectId}.",
                request.GroupId,
                request.ProjectId);
            return new(false, 0, AiGroupBookingFailure.GroupUnavailable);
        }
        if (!group.IsActive)
        {
            return new(false, 0, AiGroupBookingFailure.GroupUnavailable);
        }

        var requester = await FindRequesterAsync(request, cancellationToken);
        var selection = GetBookingCandidates(request, requester);
        var bookedPeople = new List<string>();
        GroupAppointment? confirmedGroup = null;
        var unbookedPeople = selection.InvalidPeople.ToList();
        var failure = unbookedPeople.Count > 0
            ? AiGroupBookingFailure.InvalidSuggestion
            : AiGroupBookingFailure.None;
        for (var index = 0; index < selection.Candidates.Count; index++)
        {
            var candidate = selection.Candidates[index];
            if (!candidate.CustomerId.HasValue)
            {
                var verification = numberVerifier is null
                    ? WhatsAppNumberVerification.Unavailable
                    : await numberVerifier.VerifyAsync(request.ProjectId, candidate.Phone, cancellationToken);
                if (verification != WhatsAppNumberVerification.Registered)
                {
                    unbookedPeople.Add(candidate.Name);
                    failure = verification == WhatsAppNumberVerification.NotRegistered
                        ? AiGroupBookingFailure.FriendNotOnWhatsApp : AiGroupBookingFailure.WhatsAppVerificationUnavailable;
                    continue;
                }
            }
            var result = await BookCandidateAsync(request, candidate, cancellationToken);
            if (result.Succeeded)
            {
                bookedPeople.Add(candidate.Name);
                confirmedGroup = result.Group;
            }
            else
            {
                unbookedPeople.Add(candidate.Name);
                failure = MapFailure(result.Status);
            }
            if (result.Status is GroupBookingStatus.GroupFull or
                GroupBookingStatus.GroupInactive or
                GroupBookingStatus.GroupExpired or
                GroupBookingStatus.GroupNotFound)
            {
                unbookedPeople.AddRange(selection.Candidates
                    .Skip(index + 1)
                    .Select(remainingCandidate => remainingCandidate.Name));
                break;
            }
        }

        var currentCapacity = await _context.GroupAppointments
            .AsNoTracking()
            .Where(candidate => candidate.ProjectId == request.ProjectId && candidate.Id == request.GroupId)
            .Select(candidate => (int?)candidate.Capacity)
            .SingleOrDefaultAsync(cancellationToken) ?? group.Capacity;
        var currentBookedCount = await _bookingCoordinator.CountBookingsAsync(
            request.ProjectId,
            request.GroupId,
            cancellationToken);
        var actualRemainingPlaces = Math.Max(0, currentCapacity - currentBookedCount);
        return new(
            bookedPeople.Count > 0,
            actualRemainingPlaces,
            failure)
        {
            BookedPeople = bookedPeople.ToArray(),
            UnbookedPeople = unbookedPeople.ToArray(),
            ConfirmedAppointmentDetails = confirmedGroup is null ? null
                : $"الحضور: {(Shared.Domain.AttendanceModes.Normalize(confirmedGroup.Mode) == Shared.Domain.AttendanceModes.Online ? "أونلاين" : "في السنتر في الإسكندرية")}" +
                    FreeSessionBookingPolicy.ScheduleDetails(confirmedGroup, TimezoneHelper.GetTimeZone(request.Timezone))
        };
    }

    private async Task<BookingRequester?> FindRequesterAsync(
        AiGroupBookingRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.RequesterCustomerId.HasValue)
        {
            return null;
        }

        return await _context.Customers
            .AsNoTracking()
            .Where(customer =>
                customer.ProjectId == request.ProjectId &&
                customer.Id == request.RequesterCustomerId.Value)
            .Select(customer => new BookingRequester(customer.Id, customer.Name, customer.PhoneNumber))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private CandidateSelection GetBookingCandidates(
        AiGroupBookingRequest request,
        BookingRequester? requester)
    {
        var people = request.SuggestedPeople is { Length: > 0 }
            ? request.SuggestedPeople
            : [new SuggestedGroupBookingPerson { IsRequester = true }];
        var candidates = new List<BookingCandidate>();
        var invalidPeople = new List<string>();
        var uniquePhones = new HashSet<string>(StringComparer.Ordinal);
        var requesterPhone = GroupBookingPhone.Normalize(requester?.PhoneNumber);

        foreach (var person in people)
        {
            var phone = person.IsRequester
                ? requesterPhone ?? GroupBookingPhone.Normalize(person.PhoneNumber)
                : GroupBookingPhone.Normalize(person.PhoneNumber);
            var name = (person.IsRequester
                ? person.Name?.Trim() ?? requester?.Name
                : person.Name)?.Trim();
            var usesRequesterPhoneForOtherPerson = !person.IsRequester && phone == requesterPhone;
            if (phone != null &&
                !usesRequesterPhoneForOtherPerson &&
                name is { Length: > 0 and <= 120 } &&
                uniquePhones.Add(phone))
            {
                var city = person.IsRequester
                    ? GroupBookingAttendancePolicy.EffectiveCity(request.RequesterCity, person.City)
                    : person.City;
                candidates.Add(new(name, phone, person.IsRequester ? requester?.CustomerId : null, city));
            }
            else
            {
                invalidPeople.Add(DisplayName(person, requester));
                _logger.LogWarning(
                    "Skipped invalid or duplicate AI booking person in project {ProjectId}.",
                    request.ProjectId);
            }
        }
        return new(candidates, invalidPeople);
    }

    private async Task<GroupBookingResult> BookCandidateAsync(
        AiGroupBookingRequest request,
        BookingCandidate candidate,
        CancellationToken cancellationToken)
    {
        var result = await _bookingCoordinator.BookAsync(new GroupBookingCommand
        {
            ProjectId = request.ProjectId,
            GroupId = request.GroupId,
            CustomerName = candidate.Name,
            CustomerPhone = candidate.Phone,
            KnownCustomerId = candidate.CustomerId,
            SourceConversationId = candidate.CustomerId == request.RequesterCustomerId ? request.SourceConversationId : null,
            CustomerCity = candidate.City,
            AttendanceMode = request.AttendanceMode,
            CustomerMessages = request.CustomerMessages,
            ExistingBookingPolicy = candidate.CustomerId.HasValue && request.RequesterAllowsTransfer
                ? ExistingGroupBookingPolicy.Transfer
                : ExistingGroupBookingPolicy.UpdateSameGroup,
            Origin = GroupBookingOrigin.Ai,
            ExpirationPolicy = GroupBookingExpirationPolicy.RejectAfterTwentyFourHours,
            Timezone = request.Timezone
        }, cancellationToken);
        if (result.Status == GroupBookingStatus.AlreadyInGroup)
        {
            _logger.LogInformation(
                "AI booking details saved for {Phone} already registered in group {GroupId}.",
                candidate.Phone,
                request.GroupId);
        }
        else if (result.Status == GroupBookingStatus.GroupFull)
        {
            _logger.LogWarning(
                "AI auto-booking stopped because group {GroupId} reached capacity.",
                request.GroupId);
        }

        if (result.Succeeded)
        {
            await BroadcastBookingAsync(request.ProjectId, result, cancellationToken);
        }
        return result;
    }

    private async Task BroadcastBookingAsync(
        Guid projectId,
        GroupBookingResult result,
        CancellationToken cancellationToken)
    {
        var group = result.Group!;
        var booking = result.Booking!;
        try
        {
            await _hubContext.Clients.Group($"project_{projectId}").SendAsync("GroupBookingUpdated", new
            {
                groupId = group.Id,
                groupName = group.Name,
                customerPhone = booking.CustomerPhone,
                customerName = booking.CustomerName,
                newBookedCount = result.BookedCount,
                capacity = group.Capacity,
                isFull = result.BookedCount >= group.Capacity,
                bookingId = booking.Id,
                isAttended = booking.IsAttended,
                isPaid = booking.IsPaid
            }, cancellationToken);
        }
        catch (Exception signalRException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                signalRException,
                "SignalR broadcast failed after AI group booking {BookingId}.",
                booking.Id);
        }
    }

    private sealed record BookingCandidate(string Name, string Phone, Guid? CustomerId, string? City);

    private sealed record BookingRequester(Guid CustomerId, string? Name, string? PhoneNumber);

    private sealed record CandidateSelection(
        IReadOnlyList<BookingCandidate> Candidates,
        IReadOnlyList<string> InvalidPeople);

    private static string DisplayName(SuggestedGroupBookingPerson person, BookingRequester? requester)
    {
        var name = (person.IsRequester ? person.Name ?? requester?.Name : person.Name)?.Trim();
        return name is { Length: > 0 and <= 120 }
            ? name
            : person.IsRequester ? "صاحب الطلب" : "شخص ببيانات غير مكتملة";
    }

    private static AiGroupBookingFailure MapFailure(GroupBookingStatus status) => status switch
    {
        GroupBookingStatus.InvalidRequest => AiGroupBookingFailure.InvalidSuggestion,
        GroupBookingStatus.OnlineRequired => AiGroupBookingFailure.OnlineRequired,
        GroupBookingStatus.CityRequired => AiGroupBookingFailure.CityRequired,
        GroupBookingStatus.AttendanceModeMismatch => AiGroupBookingFailure.AttendanceModeMismatch,
        GroupBookingStatus.TimeChoiceMismatch => AiGroupBookingFailure.TimeChoiceMismatch,
        GroupBookingStatus.BookingAlreadyExists => AiGroupBookingFailure.BookingAlreadyExists,
        GroupBookingStatus.GroupFull => AiGroupBookingFailure.GroupFull,
        GroupBookingStatus.GroupExpired => AiGroupBookingFailure.GroupExpired,
        GroupBookingStatus.GroupNotFound or GroupBookingStatus.GroupInactive =>
            AiGroupBookingFailure.GroupUnavailable,
        _ => AiGroupBookingFailure.TemporaryFailure
    };
}
