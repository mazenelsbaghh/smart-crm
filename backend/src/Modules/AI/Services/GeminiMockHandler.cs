using System;
using System.Text.Json;

namespace Modules.AI.Services
{
    public interface IGeminiMockHandler
    {
        bool IsMockKey(string? apiKey);
        string GenerateMockReply(string messageContent, string? apiKey);
        string GenerateMockReply(string messageContent, byte[] fileBytes, string mimeType, string? apiKey, string? model);
    }

    public class GeminiMockHandler : IGeminiMockHandler
    {
        public bool IsMockKey(string? apiKey)
        {
            return string.IsNullOrEmpty(apiKey) || apiKey == "your_gemini_api_key_here" || apiKey.StartsWith("mock_");
        }

        public string GenerateMockReply(string messageContent, string? apiKey)
        {
            if (apiKey != null && apiKey.StartsWith("mock_json_"))
            {
                return apiKey.Substring("mock_json_".Length);
            }

            if (messageContent.Contains("أنت خبير محترف في تحليل المعلومات وتجهيز قواعد المعرفة للشركات"))
            {
                return @"[
  {
    ""question"": ""ما هي أوقات العمل الرسمية لديكم؟"",
    ""options"": [
      ""من الساعة 9 صباحاً حتى 5 مساءً من الأحد للخميس"",
      ""على مدار 24 ساعة طوال أيام الأسبوع"",
      ""من السبت للخميس من 10 صباحاً حتى 10 مساءً""
    ]
  },
  {
    ""question"": ""هل تقدمون خدمة التوصيل والشحن لجميع المحافظات؟"",
    ""options"": [
      ""نعم، التوصيل لجميع محافظات مصر"",
      ""الشحن للقاهرة والجيزة فقط"",
      ""لا نقوم بالشحن حالياً، الاستلام من مقر الشركة""
    ]
  },
  {
    ""question"": ""ما هي طرق الدفع المقبولة لديكم؟"",
    ""options"": [
      ""الدفع نقداً عند الاستلام فقط"",
      ""الدفع الإلكتروني (فيزا/ماستر كارد/فودافون كاش) أو عند الاستلام"",
      ""الدفع عن طريق التحويل البنكي فقط""
    ]
  },
  {
    ""question"": ""هل يوجد فترة ضمان على المنتجات؟"",
    ""options"": [
      ""نعم، ضمان لمدة سنة كاملة ضد عيوب الصناعة"",
      ""ضمان لمدة 6 أشهر فقط"",
      ""لا يوجد ضمان على الملحقات الاستهلاكية""
    ]
  },
  {
    ""question"": ""ما هي سياسة الاسترجاع والاستبدال المتبعة؟"",
    ""options"": [
      ""خلال 14 يوماً من الاستلام بشرط الحالة الأصلية"",
      ""خلال 30 يوماً للمنتجات التي بها عيب مصنعي"",
      ""لا يمكن الاسترجاع بعد فتح الغلاف الخارجي للمنتج""
    ]
  },
  {
    ""question"": ""هل يوجد فرع رسمي للشركة يمكن زيارته؟"",
    ""options"": [
      ""نعم، فرعنا الرئيسي في القاهرة (مدينة نصر)"",
      ""نعم، لدينا فرع في الإسكندرية (سيدي جابر)"",
      ""لا، نحن متجر إلكتروني فقط ونقدم خدمة الشحن المباشر""
    ]
  },
  {
    ""question"": ""كيف يمكن التواصل مع الدعم الفني في حال وجود مشكلة؟"",
    ""options"": [
      ""عبر الواتساب المباشر المتاح في الموقع"",
      ""بالاتصال الهاتفي على الخط الساخن"",
      ""بإرسال بريد إلكتروني لقسم الدعم الفني""
    ]
  },
  {
    ""question"": ""هل تقدمون خصومات عند الشراء بكميات كبيرة؟"",
    ""options"": [
      ""نعم، يوجد خصومات خاصة لطلبات الجملة والشركات"",
      ""نعم، نوفر كوبونات خصم عند تجاوز قيمة سلة الشراء حداً معيناً"",
      ""الأسعار ثابتة حالياً ولا توجد خصومات إضافية""
    ]
  },
  {
    ""question"": ""ما هي المدة المستغرقة لتوصيل الطلبات؟"",
    ""options"": [
      ""من 24 إلى 48 ساعة للقاهرة والجيزة"",
      ""من 3 إلى 5 أيام عمل لباقي المحافظات"",
      ""شحن دولي يستغرق من 7 إلى 10 أيام عمل""
    ]
  },
  {
    ""question"": ""هل يمكن إلغاء الطلب بعد تأكيده؟"",
    ""options"": [
      ""نعم، قبل خروج الشحنة مع شركة الشحن بدون رسوم"",
      ""نعم، ولكن يتحمل العميل مصاريف الشحن إذا خرجت الشحنة"",
      ""لا يمكن إلغاء الطلبات الخاصة المصنعة حسب الطلب""
    ]
  },
  {
    ""question"": ""هل توجد رسوم إضافية على بعض طرق الدفع؟"",
    ""options"": [
      ""لا، جميع طرق الدفع بدون أي رسوم إضافية"",
      ""نعم، رسوم إضافية عند الدفع عند الاستلام"",
      ""نعم، رسوم بسيطة لبوابات الدفع الإلكتروني المعينة""
    ]
  },
  {
    ""question"": ""كيف يتم تفعيل الضمان للمنتج؟"",
    ""options"": [
      ""تلقائياً باستخدام الرقم التسلسلي المسجل بالفاتورة"",
      ""بالتسجيل في موقعنا الإلكتروني بعد الشراء"",
      ""بالتواصل مع الدعم وإرسال صورة الفاتورة""
    ]
  },
  {
    ""question"": ""هل توفرون خدمات الدعم الفني بعد البيع؟"",
    ""options"": [
      ""نعم، دعم فني مجاني طوال فترة الضمان"",
      ""نعم، يوجد اشتراك دعم فني مدفوع"",
      ""الدعم الفني متاح فقط للمشاكل المصنعية""
    ]
  },
  {
    ""question"": ""هل هناك حد أقصى لعدد المنتجات في الطلب الواحد؟"",
    ""options"": [
      ""لا يوجد حد أقصى للطلبات العادية"",
      ""نعم، بحد أقصى 5 قطع من نفس المنتج"",
      ""نعم، بحد أقصى 10 قطع لحماية المخزون""
    ]
  },
  {
    ""question"": ""هل تدعمون الدفع عند الاستلام ببطاقات الائتمان؟"",
    ""options"": [
      ""نعم، مندوب الشحن يمتلك ماكينة دفع إلكتروني"",
      ""لا، الدفع عند الاستلام نقداً فقط"",
      ""الدفع بالفيزا متاح فقط مسبقاً عبر الموقع""
    ]
  },
  {
    ""question"": ""ما هي الدول المتاح الشحن إليها خارج مصر؟"",
    ""options"": [
      ""الشحن متاح لجميع دول الخليج العربي"",
      ""الشحن متاح لدول الشرق الأوسط وأوروبا"",
      ""الشحن متاح دولياً لجميع أنحاء العالم""
    ]
  },
  {
    ""question"": ""هل هناك اشتراكات شهرية أو باقات سنوية؟"",
    ""options"": [
      ""نعم، نوفر باقات مرنة تناسب الاستخدامات المختلفة"",
      ""لا، الدفع لمرة واحدة مدى الحياة"",
      ""نعم، اشتراك شهري وتخفيض عند الدفع السنوي مسبقاً""
    ]
  },
  {
    ""question"": ""هل توفرون فترة تجربة مجانية للخدمات؟"",
    ""options"": [
      ""نعم، فترة تجريبية مجانية لمدة 7 أيام دون الحاجة لبطاقة ائتمان"",
      ""نعم، فترة تجربة لمدة 14 يوماً مع تفعيل كامل المزايا"",
      ""لا نوفر فترة تجربة مجانية ولكن نوفر ضمان استعادة الأموال""
    ]
  },
  {
    ""question"": ""كيف يمكنني تعديل بيانات حسابي أو طلبي؟"",
    ""options"": [
      ""من خلال لوحة التحكم الخاصة بك بالموقع"",
      ""بالتواصل الفوري مع خدمة العملاء لتعديل الطلب قبل الشحن"",
      ""بإرسال طلب التعديل عبر البريد الإلكتروني""
    ]
  },
  {
    ""question"": ""هل يوجد تطبيق موبايل خاص بالشركة؟"",
    ""options"": [
      ""نعم، تطبيقنا متاح على أندرويد وآيفون متجر التطبيقات"",
      ""تطبيق الأندرويد متاح حالياً وتطبيق الآيفون قيد التطوير"",
      ""لا يوجد تطبيق حالياً، وننصح بزيارة موقعنا المتجاوب مع الموبايل""
    ]
  },
  {
    ""question"": ""ما هي شروط الاستفادة من الخصومات الحالية؟"",
    ""options"": [
      ""استخدام كود الخصم الفعال عند إتمام الطلب"",
      ""الخصم يطبق تلقائياً في سلة المشتريات"",
      ""الخصم مخصص فقط لأول عملية شراء للعملاء الجدد""
    ]
  },
  {
    ""question"": ""هل تقدمون دورات تدريبية أو شروحات للمنتجات؟"",
    ""options"": [
      ""نعم، نوفر مكتبة كاملة من الفيديوهات والمقالات التعليمية مجاناً"",
      ""نعم، نقدم جلسات تدريبية تفاعلية أونلاين لعملائنا"",
      ""لا نقدم تدريباً شخصياً ولكن نوفر دليلاً تفصيلياً للاستخدام""
    ]
  },
  {
    ""question"": ""كيف يمكنني تتبع الشحنة الخاصة بي؟"",
    ""options"": [
      ""من خلال رابط التتبع المرسل إليك عبر الرسائل النصية والبريد"",
      ""بالتواصل مع خدمة العملاء وتزويدهم برقم الطلب"",
      ""من خلال قسم الطلبات بلوحة تحكم حسابك بالموقع""
    ]
  },
  {
    ""question"": ""هل توفرون شحن سريع في نفس اليوم؟"",
    ""options"": [
      ""نعم، للطلبات المؤكدة قبل الساعة 12 ظهراً داخل القاهرة والجيزة"",
      ""لا نوفر شحن في نفس اليوم، ولكن يتم الشحن خلال 24 ساعة"",
      ""الشحن السريع متاح فقط لبعض المنتجات برسوم إضافية""
    ]
  },
  {
    ""question"": ""ماذا أفعل إذا استلمت منتجاً تالفاً أو غير مطابق؟"",
    ""options"": [
      ""تواصل معنا خلال 24 ساعة لترتيب استبدال مجاني سريع"",
      ""قم برفض الاستلام من المندوب وإبلاغ الدعم الفني فوراً"",
      ""أرسل صورة للمنتج التالف عبر الواتساب وسنقوم برد المبلغ""
    ]
  },
  {
    ""question"": ""هل هناك مصاريف شحن إضافية للمناطق النائية؟"",
    ""options"": [
      ""نعم، تضاف رسوم شحن إضافية تحددها شركة الشحن"",
      ""لا، سعر الشحن موحد لجميع محافظات مصر"",
      ""الشحن مجاني لجميع المناطق عند الشراء بقيمة معينة""
    ]
  },
  {
    ""question"": ""هل يمكن الدفع باستخدام المحافظ الإلكترونية؟"",
    ""options"": [
      ""نعم، ندعم فودافون كاش، اتصالات كاش، وأورنج كاش"",
      ""نعم، عبر مسح كود الـ QR مسبقاً أو عند الاستلام"",
      ""لا ندعم المحافظ الإلكترونية حالياً، الدفع بالفيزا أو كاش""
    ]
  },
  {
    ""question"": ""هل توفرون فاتورة ضريبية مع المشتريات؟"",
    ""options"": [
      ""نعم، نرسل فاتورة ضريبية إلكترونية مسجلة مع كل طلب"",
      ""نعم، نرسلها عند الطلب من العميل وتزويدنا بالبيانات الضريبية"",
      ""نوفر فاتورة شراء عادية فقط غير شاملة الضريبة الرسمية""
    ]
  },
  {
    ""question"": ""كيف يمكنني الانضمام لبرنامج التسويق بالعمولة؟"",
    ""options"": [
      ""بالتسجيل في صفحة التسويق بالعمولة بموقعنا والحصول على رابطك الخاص"",
      ""بالتواصل مع قسم المبيعات لتقديم طلب الشراكة"",
      ""البرنامج مغلق حالياً وسنقوم بفتحه قريباً""
    ]
  },
  {
    ""question"": ""ما هي متطلبات التشغيل أو الاستخدام الأساسية؟"",
    ""options"": [
      ""اتصال إنترنت مستقر ومتصفح ويب حديث فقط"",
      ""نظام تشغيل ويندوز 10 فما فوق ومواصفات متوسطة"",
      ""لا توجد متطلبات خاصة، الخدمة تعمل على كافة الأجهزة""
    ]
  }
]";
            }

            if (messageContent.Contains("أنت خبير محترف في صياغة الأسئلة والأجوبة لقواعد المعرفة"))
            {
                return @"[
  {
    ""question"": ""ما هي أوقات العمل الرسمية لديكم؟"",
    ""answer"": ""أوقات العمل الرسمية لدينا هي من الساعة 9 صباحاً حتى 5 مساءً من الأحد إلى الخميس.""
  },
  {
    ""question"": ""هل يتوفر شحن لجميع المحافظات؟"",
    ""answer"": ""نعم، نحن نقدم خدمة الشحن والتوصيل لجميع محافظات مصر.""
  },
  {
    ""question"": ""ما هي طرق الدفع المتاحة؟"",
    ""answer"": ""نقبل الدفع نقداً عند الاستلام، أو الدفع الإلكتروني عبر الفيزا وماستركارد وفودافون كاش.""
  },
  {
    ""question"": ""هل يوجد فترة ضمان على المنتجات؟"",
    ""answer"": ""نعم، يوجد ضمان لمدة سنة كاملة ضد عيوب الصناعة على جميع المنتجات باستثناء الملحقات الاستهلاكية.""
  },
  {
    ""question"": ""ما هي سياسة الاسترجاع والاستبدال المتبعة؟"",
    ""answer"": ""نسمح بالاسترجاع والاستبدال خلال 14 يوماً من الاستلام بشرط أن يكون المنتج بحالته الأصلية وغلافه غير مفتوح.""
  },
  {
    ""question"": ""هل يوجد فرع رسمي للشركة يمكن زيارته؟"",
    ""answer"": ""نعم، فروعنا الرئيسية تقع في القاهرة (مدينة نصر) والإسكندرية (سيدي جابر) ونرحب بزيارتكم في أوقات العمل الرسمية.""
  },
  {
    ""question"": ""كيف يمكن التواصل مع الدعم الفني في حال وجود مشكلة؟"",
    ""answer"": ""يمكنك التواصل مع الدعم الفني مباشرة عبر الواتساب المتاح بالموقع أو الاتصال بالخط الساخن خلال ساعات العمل الرسمية.""
  },
  {
    ""question"": ""هل تقدمون خصومات عند الشراء بكميات كبيرة؟"",
    ""answer"": ""نعم، نحن نقدم أسعاراً خاصة وخصومات لطلبات الجملة والمؤسسات، يرجى التواصل مع قسم المبيعات للحصول على عرض سعر.""
  },
  {
    ""question"": ""ما هي المدة المستغرقة لتوصيل الطلبات؟"",
    ""answer"": ""يستغرق التوصيل داخل القاهرة والجيزة من 24 إلى 48 ساعة، بينما يستغرق لباقي المحافظات من 3 إلى 5 أيام عمل.""
  },
  {
    ""question"": ""هل يمكن إلغاء الطلب بعد تأكيده؟"",
    ""answer"": ""نعم، يمكنك إلغاء الطلب مجاناً طالما لم يتم تسليمه لشركة الشحن، وفي حال خروج الشحنة يتحمل العميل مصاريف الشحن.""
  },
  {
    ""question"": ""هل توجد رسوم إضافية على بعض طرق الدفع؟"",
    ""answer"": ""لا توجد رسوم إضافية على طرق الدفع الإلكتروني، وتطبق رسوم شحن إضافية بسيطة في حال اختيار الدفع عند الاستلام.""
  },
  {
    ""question"": ""كيف يتم تفعيل الضمان للمنتج؟"",
    ""answer"": ""يتم تفعيل الضمان تلقائياً بمجرد إتمام الشراء وتسجيل الفاتورة برقمك التسلسلي في قاعدة بياناتنا دون أي إجراء إضافي.""
  },
  {
    ""question"": ""هل توفرون خدمات الدعم الفني بعد البيع؟"",
    ""answer"": ""نعم، فريق الدعم الفني متواجد لمساعدتك وحل أي استفسارات أو مشاكل تواجهك مجاناً طوال فترة الضمان.""
  },
  {
    ""question"": ""هل هناك حد أقصى لعدد المنتجات في الطلب الواحد؟"",
    ""answer"": ""نعم، لحماية المخزون والعدالة، نحدد حداً أقصى 10 قطع للمنتج الواحد في الطلب الواحد للعملاء الأفراد.""
  },
  {
    ""question"": ""هل تدعمون الدفع عند الاستلام ببطاقات الائتمان؟"",
    ""answer"": ""نعم، مناديب التوصيل مجهزون بماكينات دفع إلكتروني (POS) للدفع بالفيزا أو الماستركارد عند باب المنزل.""
  },
  {
    ""question"": ""ما هي الدول المتاح الشحن إليها خارج مصر؟"",
    ""answer"": ""نوفر خدمة الشحن الدولي لجميع دول الخليج العربي وبعض دول الشرق الأوسط وأوروبا بالتعاون مع شركات شحن دولية.""
  },
  {
    ""question"": ""هل هناك اشتراكات شهرية أو باقات سنوية؟"",
    ""answer"": ""نعم، تتوفر لدينا باقات اشتراك شهرية مرنة لخدماتنا السحابية مع خصم مميز يصل إلى 20% عند الدفع سنوياً مسبقاً.""
  },
  {
    ""question"": ""هل توفرون فترة تجربة مجانية للخدمات؟"",
    ""answer"": ""نعم، نوفر فترة تجريبية مجانية بالكامل لمدة 7 أيام للتعرف على كافة الميزات والخدمات قبل اتخاذ قرار الاشتراك المالي.""
  },
  {
    ""question"": ""كيف يمكنني تعديل بيانات حسابي أو طلبي؟"",
    ""answer"": ""يمكنك تعديل بياناتك الشخصية من لوحة التحكم، أما لتعديل عنوان أو محتوى طلبك يرجى التواصل مع الدعم فوراً قبل الشحن.""
  },
  {
    ""question"": ""هل يوجد تطبيق موبايل خاص بالشركة؟"",
    ""answer"": ""نعم، يتوفر تطبيقنا الرسمي للتحميل مجاناً على متجر Google Play للأندرويد ومتجر App Store للآيفون لمتابعة أعمالك بسهولة.""
  },
  {
    ""question"": ""ما هي شروط الاستفادة من الخصومات الحالية؟"",
    ""answer"": ""للاستفادة من الخصومات، يرجى كتابة كود الخصم الفعال في الحقل المخصص له قبل إتمام عملية الدفع في سلة المشتريات.""
  },
  {
    ""question"": ""هل تقدمون دورات تدريبية أو شروحات للمنتجات؟"",
    ""answer"": ""نعم، نوفر شروحات تفصيلية ومقاطع فيديو تعليمية مجانية على موقعنا، إلى جانب جلسات تدريب تفاعلية للفرق عند الطلب.""
  },
  {
    ""question"": ""كيف يمكنني تتبع الشحنة الخاصة بي؟"",
    ""answer"": ""بمجرد خروج الشحنة، سنرسل لك رسالة نصية وبريداً إلكترونياً يحتويان على رابط تتبع الشحنة مع شركة الشحن لتتبع موقعها خطوة بخطوة.""
  },
  {
    ""question"": ""هل توفرون شحن سريع في نفس اليوم؟"",
    ""answer"": ""نعم، تتوفر خدمة الشحن السريع في نفس اليوم للطلبات داخل القاهرة والجيزة فقط والمؤكدة قبل الساعة الثانية عشرة ظهراً.""
  },
  {
    ""question"": ""ماذا أفعل إذا استلمت منتجاً تالفاً أو غير مطابق؟"",
    ""answer"": ""يرجى تصوير المنتج والتواصل مع خدمة العملاء خلال 24 ساعة من الاستلام وسنقوم فوراً بإرسال شحنة بديلة مجانية ومطابقة.""
  },
  {
    ""question"": ""هل هناك مصاريف شحن إضافية للمناطق النائية؟"",
    ""options"": ""نعم، قد تطبق رسوم شحن إضافية طفيفة لبعض المحافظات الحدودية والمناطق البعيدة وتظهر الرسوم بالتفصيل قبل تأكيد الطلب."",
    ""answer"": ""نعم، قد تطبق رسوم شحن إضافية طفيفة لبعض المحافظات الحدودية والمناطق البعيدة وتظهر الرسوم بالتفصيل قبل تأكيد الطلب.""
  },
  {
    ""question"": ""هل يمكن الدفع باستخدام المحافظ الإلكترونية؟"",
    ""answer"": ""نعم، ندعم الدفع عبر جميع المحافظ الإلكترونية في مصر (فودافون كاش، اتصالات، أورنج، فوري، وغيرها) بكل سهولة وأمان.""
  },
  {
    ""question"": ""هل توفرون فاتورة ضريبية مع المشتريات؟"",
    ""answer"": ""نعم، نحن شركة رسمية مسجلة ونصدر فاتورة ضريبية إلكترونية مع جميع الطلبات ويتم إرسالها لبريدك الإلكتروني بعد الاستلام.""
  },
  {
    ""question"": ""كيف يمكنني الانضمام لبرنامج التسويق بالعمولة؟"",
    ""answer"": ""يمكنك التقديم للانضمام لبرنامج التسويق بالعمولة من خلال ملء النموذج المتاح في صفحة 'التسويق بالعمولة' وسيتواصل معك فريقنا لتفعيل حسابك.""
  },
  {
    ""question"": ""ما هي متطلبات التشغيل أو الاستخدام الأساسية؟"",
    ""answer"": ""الخدمة تعمل سحابياً بالكامل ولا تحتاج سوى اتصال مستقر بالإنترنت وجهاز كمبيوتر أو موبايل مع متصفح ويب حديث.""
  }
]";
            }

            if (messageContent.Contains("JSON format") || messageContent.Contains("JSON") || messageContent.Contains("\"intent\""))
            {
                // Check if it's the Customer Memory Extraction / Profile generation prompt
                if (messageContent.Contains("Analyze the following WhatsApp conversation"))
                {
                    string transcriptPart = messageContent;
                    int transcriptIdx = messageContent.IndexOf("Conversation Transcript:");
                    if (transcriptIdx != -1)
                    {
                        transcriptPart = messageContent.Substring(transcriptIdx);
                    }

                    string? profileName = null;
                    if (transcriptPart.Contains("اسمي أدهم") || transcriptPart.Contains("معاك أدهم") || transcriptPart.Contains("أدهم مدبولي"))
                    {
                        profileName = "أدهم مدبولي";
                    }
                    else if (transcriptPart.Contains("اسمي أحمد") || transcriptPart.Contains("معاك أحمد") || transcriptPart.Contains("أحمد"))
                    {
                        profileName = "أحمد";
                    }
                    else if (transcriptPart.Contains("اسمي محمد") || transcriptPart.Contains("معاك محمد") || transcriptPart.Contains("محمد"))
                    {
                        profileName = "محمد";
                    }
                    string profileCity = "القاهرة";
                    decimal profileBudget = 1500;
                    int profileLeadScore = 85;
                    string profilePipelineStage = "Proposal";
                    string profileLabel = "استفسار عام";
                    string profileSummary = "عميل مهتم بالتسجيل في الدورة ويبحث عن تفاصيل الأسعار وتسهيلات الدفع ويعيش في القاهرة.";
                    string profileFactsJson = "[\"مهتم بالدورة المكثفة\", \"يفضل التواصل واتساب\", \"يعيش في القاهرة\"]";
                    if (transcriptPart.Contains("email") || transcriptPart.Contains("Email"))
                    {
                        profileFactsJson = "[\"Prefers contact via email\", \"مهتم بالدورة المكثفة\", \"يفضل التواصل واتساب\", \"يعيش في القاهرة\"]";
                    }
                    string profileTriggersJson = "[\"خصم لفترة محدودة\", \"البدء الفوري\"]";
                    string profileObjectionsJson = "[\"السعر مرتفع قليلاً\"]";
                    if (transcriptPart.Contains("expensive") || transcriptPart.Contains("price") || transcriptPart.Contains("Price") || transcriptPart.Contains("Expensive"))
                    {
                        profileObjectionsJson = "[\"Price sensitive / Objections about cost\"]";
                    }

                    if (transcriptPart.Contains("الإسكندرية") || transcriptPart.Contains("اسكندرية"))
                    {
                        profileCity = "الإسكندرية";
                        profileSummary = "عميل مهتم بالتسجيل في الدورة ويبحث عن تفاصيل الأسعار وتسهيلات الدفع ويعيش في الإسكندرية.";
                        profileFactsJson = "[\"مهتم بالدورة المكثفة\", \"يفضل التواصل واتساب\", \"يعيش في الإسكندرية\"]";
                    }
                    else if (transcriptPart.Contains("الجيزة") || transcriptPart.Contains("جيزة"))
                    {
                        profileCity = "الجيزة";
                        profileSummary = "عميل مهتم بالتسجيل في الدورة ويبحث عن تفاصيل الأسعار وتسهيلات الدفع ويعيش في الجيزة.";
                        profileFactsJson = "[\"مهتم بالدورة المكثفة\", \"يفضل التواصل واتساب\", \"يعيش في الجيزة\"]";
                    }

                    if (transcriptPart.Contains("سعر") || transcriptPart.Contains("بكام"))
                    {
                        profileLabel = "استفسار عن السعر";
                    }
                    else if (transcriptPart.Contains("تفاصيل"))
                    {
                        profileLabel = "استفسار عن التفاصيل";
                    }
                    else if (transcriptPart.Contains("حجز") || transcriptPart.Contains("احجز") || transcriptPart.Contains("سجل"))
                    {
                        profileLabel = "طلب حجز";
                    }
                    else if (transcriptPart.Contains("شحن") || transcriptPart.Contains("توصيل"))
                    {
                        profileLabel = "استفسار عن الشحن";
                    }
                    else if (messageContent.Contains("شكوى") || messageContent.Contains("مشكلة"))
                    {
                        profileLabel = "شكوى";
                    }

                    return $@"{{
  ""facts"": {profileFactsJson},
  ""triggers"": {profileTriggersJson},
  ""objections"": {profileObjectionsJson},
  ""summary"": ""{profileSummary}"",
  ""name"": {(profileName == null ? "null" : $"\"{profileName}\"")},
  ""city"": ""{profileCity}"",
  ""budget"": {profileBudget},
  ""leadScore"": {profileLeadScore},
  ""pipelineStage"": ""{profilePipelineStage}"",
  ""label"": ""{profileLabel}""
}}";
                }

                // Extract customer message to formulate a context-appropriate mock reply
                string customerMessage = "";
                int msgIdx = messageContent.LastIndexOf("Customer Message: \"");
                if (msgIdx != -1)
                {
                    customerMessage = messageContent.Substring(msgIdx + "Customer Message: \"".Length).Trim().TrimEnd('"');
                }

                string replyContent = "أهلاً بك! كيف يمكنني مساعدتك اليوم؟";
                string intent = "greeting";
                string label = "ترحيب";
                string? city = null;

                if (customerMessage.Contains("سعر") || customerMessage.Contains("بكام"))
                {
                    intent = "inquiry";
                    label = "استفسار عن السعر";
                    replyContent = "بالتأكيد! تفاصيل السعر هي 500 جنيه مصري، وهناك خصم خاص لفترة محدودة. هل تحب تأكيد الطلب؟";
                }
                else if (customerMessage.Contains("تفاصيل"))
                {
                    intent = "inquiry";
                    label = "استفسار عن التفاصيل";
                    replyContent = "بالتأكيد! تفاصيل الكورس هي كالتالي: الكورس مكثف ويغطي أساسيات الذكاء الاصطناعي وبناء التطبيقات. هل تحب معرفة المزيد؟";
                }
                else if (customerMessage.Contains("شحن") || customerMessage.Contains("facebook.com") || customerMessage.Contains("share"))
                {
                    intent = "inquiry";
                    label = "استفسار";
                    replyContent = "أهلاً بك! سعر الشحن يختلف حسب محافظتك. هل تحب تفاصيل أكثر؟";
                }
                else if (customerMessage.Contains("مشكلة") || customerMessage.Contains("شكوى") || customerMessage.Contains("بطيء"))
                {
                    intent = "complaint";
                    label = "شكوى";
                    replyContent = "نعتذر بشدة عن أي إزعاج. يرجى تزويدنا بالتفاصيل لنقوم بحل المشكلة فوراً.";
                }
                else if (customerMessage.Contains("قاهرة") || customerMessage.Contains("القاهرة"))
                {
                    intent = "inquiry";
                    label = "استفسار";
                    city = "القاهرة";
                    replyContent = "أهلاً بأهل القاهرة! نوصل للقاهرة خلال 24 ساعة.";
                }
                else if (messageContent.Contains("City: Missing"))
                {
                    intent = "inquiry";
                    label = "استفسار";
                    replyContent = "أهلاً بك! تشرفنا بحضرتك يا فندم. ممكن نعرف حضرتك بتكلمنا من أي مدينة؟";
                }

                // Dynamically generate context-aware mock follow-up information
                string dueDateStr = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ssZ");
                string apptTimeStr = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-ddTHH:mm:ssZ");
                bool followUpNeeded = true;
                string followUpType = "Nurturing";
                string followUpNotes = "مرحباً يا فندم، حابين نطمن على تفاصيل الحجز ونعرف لو في أي استفسار آخر؟";

                if (customerMessage.Contains("حجز") || customerMessage.Contains("احجز") || customerMessage.Contains("سجل") || customerMessage.Contains("تسجيل") || customerMessage.Contains("موعد"))
                {
                    followUpType = "AppointmentReminder";
                    followUpNotes = "تذكير: موعد كورس الذكاء الاصطناعي غداً في تمام الساعة السابعة مساءً بتوقيت القاهرة. ننتظرك!";
                    dueDateStr = DateTime.UtcNow.AddHours(23).ToString("yyyy-MM-ddTHH:mm:ssZ");
                }
                else if (customerMessage.Contains("سعر") || customerMessage.Contains("بكام"))
                {
                    followUpNotes = "مرحباً يا فندم! كنا اتكلمنا بخصوص الأسعار، هل حابب تستفيد من الخصم المتاح اليوم؟";
                }
                else if (customerMessage.Contains("شحن") || customerMessage.Contains("توصيل"))
                {
                    followUpNotes = "يا فندم بخصوص الشحن، هل تحب نأكد الطلب للشحن غداً؟";
                }

                return $@"{{
  ""intent"": ""{intent}"",
  ""sentiment"": ""positive"",
  ""replyStyle"": ""Casual"",
  ""label"": ""{label}"",
  ""pipelineStage"": ""New"",
  ""entities"": {{
    ""city"": {(city == null ? "null" : $"\"{city}\"")},
    ""interests"": [],
    ""timeline"": null
  }},
  ""replyContent"": ""{replyContent}"",
  ""confidence"": 0.99,
  ""suggestedFollowUp"": {{
    ""needed"": {followUpNeeded.ToString().ToLower()},
    ""type"": ""{followUpType}"",
    ""appointmentTime"": {(followUpType == "AppointmentReminder" ? $"\"{apptTimeStr}\"" : "null")},
    ""dueDate"": ""{dueDateStr}"",
    ""notes"": ""{followUpNotes}""
  }}
}}";
            }

            return $"[Mock Gemini Reply] Re: {messageContent}";
        }

        public string GenerateMockReply(string messageContent, byte[] fileBytes, string mimeType, string? apiKey, string? model)
        {
            if (apiKey != null && apiKey.StartsWith("mock_json_"))
            {
                return apiKey.Substring("mock_json_".Length);
            }

            // Voice Note Transcription Mock Check
            if (mimeType.StartsWith("audio/") && (messageContent.Contains("Voice") || messageContent.Contains("voice") || messageContent.Contains("transcribe") || messageContent.Contains("Transcribe")))
            {
                return @"{
  ""intent"": ""inquiry"",
  ""sentiment"": ""neutral"",
  ""replyStyle"": ""Casual"",
  ""label"": ""استفسار"",
  ""pipelineStage"": ""Contacted"",
  ""entities"": {
    ""city"": null,
    ""interests"": [""كورس الذكاء الاصطناعي""],
    ""timeline"": null
  },
  ""replyContent"": ""أهلاً بك! سعر كورس الذكاء الاصطناعي هو 500 جنيه مصري وهناك خصم لفترة محدودة. هل تود حجز مقعدك؟"",
  ""confidence"": 0.95,
  ""transcription"": ""أنا مهتم بكورس الذكاء الاصطناعي وبدي أعرف السعر""
}";
            }

            // Image/Receipt Analysis Mock Check
            if (mimeType.StartsWith("image/"))
            {
                return @"{
  ""intent"": ""purchase"",
  ""sentiment"": ""positive"",
  ""replyStyle"": ""Sales"",
  ""label"": ""طلب شراء"",
  ""pipelineStage"": ""Qualified"",
  ""entities"": {
    ""city"": ""القاهرة"",
    ""budget"": 50,
    ""interests"": [],
    ""timeline"": null
  },
  ""replyContent"": ""شكراً لإرسال الإيصال! لقد تم استلام مبلغ 50 دولار وتحديث ميزانيتك إلى القاهرة. جاري مراجعة الطلب."",
  ""confidence"": 0.95
}";
            }

            // Default text fallback mock
            return GenerateMockReply(messageContent, apiKey);
        }
    }
}
