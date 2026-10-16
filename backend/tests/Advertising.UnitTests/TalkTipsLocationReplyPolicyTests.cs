using Modules.AI.Services;
using Modules.Projects.Domain;
using Modules.TalkTips.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class TalkTipsLocationReplyPolicyTests
{
    [Theory]
    [InlineData("العنوان: ١٢ شارع فؤاد، الدور الثاني، بجوار الصيدلية.")]
    [InlineData("مكان السنتر: سموحة، برج النور، الدور الثالث.")]
    [InlineData("عنوان السنتر: ١٢ شارع فؤاد. حضرتك ساكن فين؟")]
    [InlineData("عنوان السنتر: ١٢ شارع فؤاد. حضرتك ساكن في انهي محافظة؟")]
    [InlineData("اللوكيشن: https://maps.app.goo.gl/TestPlace?g_st=iw")]
    [InlineData("https://www.google.com/maps?q=31.2001,29.9187&z=16")]
    [InlineData("العنوان تفصيلياً: سيدي جابر من على البحر، الشارع اللي قدام Point 6، على أوله كافيه ماسبيرو، شارع السلام، عمارة رقم 4، الدور الأول.\nhttps://maps.app.goo.gl/BdAB9aCm3nJf3KgF9")]
    public void Offline_address_and_map_replies_name_Alexandria_without_losing_details(string original)
    {
        var analysis = new MarketingAnalysisResult { ReplyContent = original, PublicCommentReply = original };

        TalkTipsLocationReplyPolicy.Apply(new ProjectSettings { IsTalkTipsTrialGateEnabled = true }, analysis);

        Assert.Equal("الإسكندرية — " + original, analysis.ReplyContent);
        Assert.Equal("الإسكندرية — " + original, analysis.PublicCommentReply);
    }

    [Theory]
    [InlineData("العنوان في الإسكندرية: ١٢ شارع فؤاد.")]
    [InlineData("عنوان السنتر: الاسكندرية، سموحة.")]
    [InlineData("Center address: 12 Fouad Street, Alexandria.")]
    public void An_address_that_already_identifies_the_city_is_not_prefixed_again(string original)
    {
        var analysis = new MarketingAnalysisResult { ReplyContent = original, PublicCommentReply = original };
        var settings = new ProjectSettings { IsTalkTipsTrialGateEnabled = true };

        TalkTipsLocationReplyPolicy.Apply(settings, analysis);
        TalkTipsLocationReplyPolicy.Apply(settings, analysis);

        Assert.Equal(original, analysis.ReplyContent);
        Assert.Equal(original, analysis.PublicCommentReply);
    }

    [Fact]
    public void Each_address_paragraph_names_the_city_without_rewriting_surrounding_text()
    {
        var analysis = new MarketingAnalysisResult
        {
            ReplyContent = "الحصة الجاية يوم الجمعة.\n\nالعنوان في الإسكندرية: ١٢ شارع فؤاد.\n\nاللوكيشن: https://maps.app.goo.gl/AnotherPlace\n\nمستنيينك!",
            PublicCommentReply = "أهلاً بيك!\n\nمكان السنتر: سموحة، الدور الثاني."
        };
        var settings = new ProjectSettings { IsTalkTipsTrialGateEnabled = true };

        TalkTipsLocationReplyPolicy.Apply(settings, analysis);
        TalkTipsLocationReplyPolicy.Apply(settings, analysis);

        Assert.Equal("الحصة الجاية يوم الجمعة.\n\nالعنوان في الإسكندرية: ١٢ شارع فؤاد.\n\nالإسكندرية — اللوكيشن: https://maps.app.goo.gl/AnotherPlace\n\nمستنيينك!", analysis.ReplyContent);
        Assert.Equal("أهلاً بيك!\n\nالإسكندرية — مكان السنتر: سموحة، الدور الثاني.", analysis.PublicCommentReply);
    }

    [Fact]
    public void Other_projects_keep_their_own_address_and_city()
    {
        const string original = "العنوان: ١٢ شارع التحرير، القاهرة.\nhttps://maps.app.goo.gl/OtherCenter";
        var analysis = new MarketingAnalysisResult { ReplyContent = original, PublicCommentReply = original };

        TalkTipsLocationReplyPolicy.Apply(new ProjectSettings { IsTalkTipsTrialGateEnabled = false }, analysis);

        Assert.Equal(original, analysis.ReplyContent);
        Assert.Equal(original, analysis.PublicCommentReply);
    }

    [Theory]
    [InlineData("الحصة أونلاين على Zoom، تقدر تدخل من أي مدينة.")]
    [InlineData("أهلاً بيك! معاد الحصة الجمعة الساعة ٧، جهّز الأسئلة اللي حابب تسألها.")]
    [InlineData("ابدأ التجربة هنا: https://talktips.example/trial")]
    [InlineData("عنوانك: ١٢ شارع التحرير، القاهرة.")]
    [InlineData("حضرتك قلت: عنواني ١٢ شارع التحرير، القاهرة.")]
    [InlineData("عنوان الدرس: طرق تقديم نفسك باللغة الإنجليزية.")]
    [InlineData("العنوان: مهارات المحادثة باللغة الإنجليزية.")]
    [InlineData("إنت في شارع التحرير بالقاهرة، فالأونلاين أنسب ليك.")]
    [InlineData("ده اللوكيشن اللي حضرتك بعته: https://maps.app.goo.gl/CustomerHome")]
    [InlineData("ده اللوكيشن اللي حضرتك بعته:\n\nhttps://maps.app.goo.gl/CustomerHome")]
    [InlineData("حضرتك بعت اللوكيشن ده: https://maps.app.goo.gl/CustomerHome")]
    [InlineData("ده اللوكيشن اللي بعته حضرتك: https://maps.app.goo.gl/CustomerHome")]
    [InlineData("موقعك: https://maps.app.goo.gl/CustomerHome")]
    public void Online_and_non_address_replies_are_unchanged(string original)
    {
        var analysis = new MarketingAnalysisResult { ReplyContent = original };

        TalkTipsLocationReplyPolicy.Apply(new ProjectSettings { IsTalkTipsTrialGateEnabled = true }, analysis);

        Assert.Equal(original, analysis.ReplyContent);
        Assert.Null(analysis.PublicCommentReply);
    }
}
