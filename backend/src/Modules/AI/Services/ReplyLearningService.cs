using Microsoft.EntityFrameworkCore;
using Modules.AI.Domain;
using Shared.Infrastructure;

namespace Modules.AI.Services;

public sealed record ReplyLessonCandidate(string Code, Guid MessageId);
public sealed record ReplyLearningObservation(Guid ProjectId, Guid ConversationId, string Channel,
    IReadOnlyList<ReplyLessonCandidate> Candidates);

public sealed class ReplyLearningService(AppDbContext db)
{
    public static readonly IReadOnlyDictionary<string, string> Lessons = new Dictionary<string, string>
    {
        ["AnswerLatestQuestion"] = "أجب عن سؤال العميل الأخير مباشرة؛ لا تستبدل سؤال الموعد بإعادة السعر أو العرض.",
        ["ReuseKnownDetails"] = "راجع البيانات المذكورة في المحادثة وملف العميل قبل طلبها مرة أخرى؛ اطلب فقط الناقص أو المتعارض.",
        ["VerifyBookingBeforeConfirmation"] = "لا تؤكد الحجز أو الإلغاء أو الدفع إلا بعد نجاح الإجراء الفعلي؛ نية العميل ليست تنفيذًا.",
        ["RespectHumanHandoff"] = "عند طلب موظف فعّل طلب التحويل ولا تستمر في أسئلة البيع، ولا تدّع أن موظفًا تواصل قبل حدوث ذلك.",
        ["UseApprovedCommercialFacts"] = "استمد الأسعار والمواعيد والتوافر من المصادر المعتمدة الحالية فقط؛ لا تنقل تفاصيل عميل آخر أو تخمّن معلومة ناقصة.",
        ["AvoidRepeatedFallbacks"] = "لا تكرر اعتذارًا عامًا بلا حل؛ وضّح المعلومة الناقصة أو الخطوة العملية اللازمة للإجابة."
    };

    public async Task ObserveAsync(ReplyLearningObservation observation, CancellationToken ct)
    {
        if (observation.Candidates.Count == 0) return;
        await using var transaction = db.Database.IsNpgsql() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        if (db.Database.IsNpgsql())
        {
            var lockKey = $"reply-learning:{observation.ProjectId:N}:{observation.Channel}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
        }
        foreach (var candidate in observation.Candidates.Where(c => Lessons.ContainsKey(c.Code)).DistinctBy(c => c.Code).Take(3))
            await RecordEvidenceAsync(observation, candidate, ct);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    private async Task RecordEvidenceAsync(ReplyLearningObservation observation, ReplyLessonCandidate candidate, CancellationToken ct)
    {
        var lesson = await db.ReplyLessons.IgnoreQueryFilters().SingleOrDefaultAsync(l =>
            l.ProjectId == observation.ProjectId && l.Channel == observation.Channel && l.Code == candidate.Code, ct);
        if (lesson is null)
        {
            lesson = new ReplyLesson { ProjectId = observation.ProjectId, Channel = observation.Channel, Code = candidate.Code };
            db.ReplyLessons.Add(lesson);
        }
        if (await db.ReplyLessonEvidence.IgnoreQueryFilters().AnyAsync(e =>
            e.ProjectId == observation.ProjectId && e.LessonId == lesson.Id && e.ConversationId == observation.ConversationId, ct)) return;
        db.ReplyLessonEvidence.Add(new ReplyLessonEvidence { ProjectId = observation.ProjectId, LessonId = lesson.Id,
            ConversationId = observation.ConversationId, MessageId = candidate.MessageId });
        lesson.UpdatedAt = DateTime.UtcNow;
    }

    public async Task<string> InstructionsAsync(Guid projectId, string channel, CancellationToken ct = default)
    {
        var codes = await db.ReplyLessons.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.ProjectId == projectId && l.Channel == channel && l.Enabled
                && db.ReplyLessonEvidence.IgnoreQueryFilters().Count(e => e.ProjectId == projectId && e.LessonId == l.Id) >= 2)
            .OrderByDescending(l => l.UpdatedAt).ThenBy(l => l.Code).Take(5).Select(l => l.Code).ToListAsync(ct);
        var instructions = codes.Where(Lessons.ContainsKey).Select(code => "- " + Lessons[code]).ToArray();
        return instructions.Length == 0 ? string.Empty :
            "\n\nدروس سلوكية مستفادة من مراجعات هذا المشروع؛ تطبق مع تعليماته ومصادره المعتمدة ولا تغير الحقائق التجارية:\n" + string.Join("\n", instructions);
    }
}
