# Smart Customer Core — Full Detailed Architecture & Feature Specification

## 1. تعريف النظام

Smart Customer Core هو نظام داخلي موحد لإدارة خدمة العملاء والمبيعات والمتابعات والـCRM والذكاء الاصطناعي والأتمتة والتحليلات، ويتم دمجه داخل مشاريعك المختلفة عن طريق APIs خاصة بكل مشروع.

النظام ليس SaaS للبيع، بل Core داخلي يخدم مشاريعك أنت، وكل مشروع له بيئة مستقلة داخل نفس الـBackend.

---

## 2. القرارات المعمارية المعتمدة

| البند | القرار |
|---|---|
| نوع النظام | Modular Monolith |
| Backend | ASP.NET Core |
| WhatsApp | Node.js + Baileys |
| AI Model | Gemini 3.5 Flash |
| OCR منفصل | لا |
| Voice-to-text منفصل | لا |
| الصور والفويس | يرسلون مباشرة إلى Gemini 3.5 Flash |
| كل مشروع | بيئة مستقلة |
| الموظف | مربوط بمشروع واحد |
| AI | يرد تلقائيًا |
| AI Actions | مسموح له ينفذ، مع نظام Risk & Approval |
| Workflow | AI ينشئه ويقترحه وينفذه |
| البداية | سيرفر واحد |
| المستقبل | قابل للتوسع لاحقًا |
| الاستخدام | داخلي لمشاريعك |
| التطبيق | Web أولًا، Mobile لاحقًا |

---

## 3. الفلسفة العامة للنظام

كل رسالة لا تعتبر مجرد Message، بل تتحول إلى دورة تشغيل كاملة:

```text
Incoming Message
      ↓
Message Aggregation
      ↓
AI Understanding
      ↓
Customer Intent
      ↓
CRM Update
      ↓
Workflow Decision
      ↓
Follow-up Decision
      ↓
Assignment Decision
      ↓
Reply Strategy
      ↓
Marketing Response
      ↓
Analytics Update
      ↓
Reports & Insights
```

---

## 4. High-Level System Diagram

```text
                                   ┌────────────────────────────┐
                                   │        Admin / Agents       │
                                   │      Web Dashboard          │
                                   └──────────────┬─────────────┘
                                                  │
                                                  ▼
                                   ┌────────────────────────────┐
                                   │       Frontend App          │
                                   │     React / Next.js         │
                                   └──────────────┬─────────────┘
                                                  │
                                                  ▼
┌────────────────────┐             ┌────────────────────────────┐
│ WhatsApp Customers │ ──────────► │     Baileys Gateway         │
└────────────────────┘             │       Node.js Service       │
                                   └──────────────┬─────────────┘
                                                  │ Webhooks
                                                  ▼
                                   ┌────────────────────────────┐
                                   │     ASP.NET Core Backend    │
                                   │      Modular Monolith       │
                                   └──────────────┬─────────────┘
                                                  │
       ┌──────────────────────────────────────────┼──────────────────────────────────────────┐
       ▼                                          ▼                                          ▼
┌──────────────┐                         ┌────────────────┐                         ┌──────────────┐
│ PostgreSQL   │                         │    RabbitMQ    │                         │    Redis     │
└──────────────┘                         └────────────────┘                         └──────────────┘
       │                                          │                                          │
       ▼                                          ▼                                          ▼
┌──────────────┐                         ┌────────────────┐                         ┌──────────────┐
│ Elasticsearch│                         │ Worker Services│                         │ SignalR      │
└──────────────┘                         └────────────────┘                         └──────────────┘
       │                                          │
       ▼                                          ▼
┌──────────────┐                         ┌────────────────┐
│ Vector DB    │                         │ Gemini 3.5 Flash│
└──────────────┘                         └────────────────┘
```

---

## 5. Single Server Infrastructure

```text
Ubuntu Server
│
├── Nginx Reverse Proxy
├── ASP.NET Core API
├── ASP.NET Core Workers
├── Node.js Baileys Service
├── PostgreSQL
├── Redis
├── RabbitMQ
├── Elasticsearch
├── Vector DB / pgvector
├── Object Storage / Local S3 Compatible Storage
├── SignalR
├── Hangfire Dashboard
├── Monitoring Stack
└── Docker / Docker Compose
```

---

## 6. Modular Monolith Structure

```text
SmartCustomerCore.Backend
│
├── Modules
│   ├── Auth
│   ├── Projects
│   ├── Users
│   ├── RolesPermissions
│   ├── WhatsApp
│   ├── Conversations
│   ├── Messages
│   ├── AI
│   ├── AICompanyBrain
│   ├── AIMarketingBrain
│   ├── CRM
│   ├── FollowUps
│   ├── Assignment
│   ├── Workflows
│   ├── Scheduler
│   ├── Campaigns
│   ├── KnowledgeBase
│   ├── Media
│   ├── Analytics
│   ├── Reports
│   ├── Notifications
│   ├── Approvals
│   ├── Integrations
│   ├── Audit
│   └── SystemHealth
│
├── Shared
│   ├── Domain
│   ├── Application
│   ├── Infrastructure
│   ├── Events
│   ├── Queue
│   ├── Security
│   └── Common
│
└── Workers
    ├── AIWorker
    ├── CRMWorker
    ├── AnalyticsWorker
    ├── CampaignWorker
    ├── NotificationWorker
    ├── SchedulerWorker
    ├── MediaWorker
    └── IntegrationWorker
```

---

## 7. Project Isolation

كل مشروع له بيئة مستقلة داخل نفس النظام.

```text
Project
│
├── Project Settings
├── WhatsApp Number
├── Users
├── Roles
├── Customers
├── Conversations
├── CRM Fields
├── Tags
├── Pipelines
├── AI Personality
├── AI Rules
├── Knowledge Base
├── APIs
├── Workflows
├── Campaigns
├── Reports
├── Follow-up Rules
└── Approval Rules
```

### قواعد العزل

- كل Customer مربوط بـProjectId.
- كل Conversation مربوط بـProjectId.
- كل User مربوط بمشروع واحد.
- كل AI Context يتم بناؤه حسب المشروع.
- كل Knowledge Base مستقلة حسب المشروع.
- كل Workflow مستقل حسب المشروع.
- كل Report مستقل حسب المشروع.
- كل API Connector مستقل حسب المشروع.

---

## 8. User Roles & Permissions

```text
Owner
│
├── Admin
│   ├── Supervisor
│   │   ├── Agent
│   │   └── AI Reviewer
│   ├── Analyst
│   └── Campaign Manager
```

### الصلاحيات

#### Owner
- إدارة كل المشاريع.
- إدارة الإعدادات العامة.
- مشاهدة كل التقارير.
- إدارة البنية والـAPI Keys.

#### Admin
- إدارة مشروع محدد.
- قبول أو رفض AI Approvals.
- إدارة المستخدمين.
- إدارة الـWorkflows.
- إدارة الـKnowledge Base.

#### Supervisor
- متابعة المحادثات.
- إعادة توزيع الشغل.
- مراجعة أداء الموظفين.
- التعامل مع الشكاوى.

#### Agent
- الرد على العملاء.
- مشاهدة العملاء المخصصين له.
- تنفيذ المهام والمتابعات.

#### AI Reviewer
- مراجعة ردود AI الحساسة.
- مراجعة Knowledge المقترحة.
- مراجعة CRM Updates المهمة.

#### Analyst
- مشاهدة التقارير والتحليلات فقط.

---

## 9. WhatsApp Gateway Architecture

```text
WhatsApp
   ↓
Baileys Session
   ↓
Message Receiver
   ↓
Media Downloader
   ↓
Webhook Sender
   ↓
ASP.NET Core / WhatsApp Module
   ↓
Message Aggregator
   ↓
Conversation Engine
```

### مكونات WhatsApp Gateway

- Session Manager
- QR Login Manager
- Auto Reconnect
- Message Receiver
- Message Sender
- Media Handler
- Delivery Status Listener
- Read Receipt Listener
- Number Health Monitor
- Rate Limiter
- Anti-Ban Delay Controller

### مميزات WhatsApp

- تشغيل رقم واحد بالبداية.
- دعم عدة أرقام لاحقًا.
- QR Login.
- Session Backup.
- Auto Reconnect.
- إرسال واستقبال نصوص.
- إرسال واستقبال صور.
- إرسال واستقبال فويس.
- إرسال واستقبال ملفات.
- Tracking للرسائل.
- Retry للإرسال الفاشل.
- Smart Sending Delay.
- Anti-Ban Throttling.
- Number Health Score.

---

## 10. Message Aggregator

### الهدف

لو العميل بعت كذا رسالة ورا بعض، النظام لا يرد على كل رسالة لوحدها.

النظام ينتظر فترة قصيرة ذكية، يجمع الرسائل، يفهمها كرسالة واحدة، ثم يرد عليها كلها معًا.

```text
Message 1: السلام عليكم
Message 2: عايز السعر
Message 3: وفيه تقسيط؟
Message 4: والتسليم إمتى؟
        ↓
Aggregator Window
        ↓
Unified Customer Intent
        ↓
Single Smart Response or Multi-Part Response
```

### قواعد التجميع

- ينتظر من 3 إلى 10 ثواني حسب سرعة العميل.
- لو العميل يكتب بسرعة، يزيد الانتظار قليلًا.
- لو الرسالة طارئة، يرد أسرع.
- لو العميل بعت Media، ينتظر تحميلها وتحليلها.
- لو العميل توقف، يبدأ الرد.

### المخرجات

- Customer Intent
- Questions List
- Required Data
- Reply Strategy
- Number of Reply Messages
- CRM Updates
- Follow-up Actions

---

## 11. Smart Human-Like Messaging Engine

### الهدف

الـAI يرد كإنسان فاهم ومبيعاته قوية، وليس كبوت جامد.

```text
Customer Intent
     ↓
Marketing Brain
     ↓
Reply Strategy
     ↓
Chunking Decision
     ↓
Smart Delay
     ↓
Message 1
     ↓
Message 2
     ↓
Message 3
```

### وظائفه

- تحديد هل الرد يكون رسالة واحدة أو أكثر.
- تقسيم الرد الطويل لرسائل طبيعية.
- تحديد توقيت كل رسالة.
- استخدام أسلوب مبيعات ذكي.
- بناء ثقة.
- خلق فضول.
- التعامل مع الاعتراض.
- اختيار CTA مناسب.
- عدم إغراق العميل برسائل كثيرة.

### Modes

- Fast Reply
- Casual Human Reply
- Sales Mode
- Support Mode
- VIP Mode
- Complaint Mode
- Follow-up Mode

---

## 12. AI Marketing Brain

```text
AI Marketing Brain
│
├── Customer Psychology Analyzer
├── Buyer Intent Analyzer
├── Trust Builder
├── Urgency Engine
├── Curiosity Engine
├── Objection Handler
├── CTA Optimizer
├── Conversion Optimizer
├── Engagement Analyzer
├── Soft Selling Engine
└── Reply Style Selector
```

### مميزاته

- يفهم العميل مهتم ولا بارد.
- يعرف هل يضغط للبيع أم يهدأ.
- يقترح ردود مقنعة.
- يعالج اعتراض السعر.
- يخلق ثقة قبل عرض السعر.
- يحدد هل يرسل عرض أم ينتظر.
- يختار أفضل Call To Action.
- يحلل هل العميل يحتاج متابعة.
- يحدد أفضل وقت للمتابعة.

---

## 13. AI Engine Architecture

```text
AI Engine
│
├── Context Builder
├── Gemini Connector
├── Reply Generator
├── Intent Detector
├── Sentiment Analyzer
├── Entity Extractor
├── Customer Classifier
├── Lead Scorer
├── CRM Update Generator
├── Follow-up Generator
├── Workflow Generator
├── Analytics Generator
├── Marketing Brain
├── Human Messaging Engine
├── AI Memory Manager
├── Company Brain Retriever
├── Risk Analyzer
├── Approval Router
└── Action Executor
```

---

## 14. Gemini 3.5 Flash Usage

### يستخدم في

- فهم الرسائل النصية.
- فهم الصور.
- فهم الفويس مباشرة.
- تحليل نية العميل.
- تصنيف العميل.
- توليد الرد.
- تحديد عدد الرسائل.
- تحديث CRM.
- إنشاء Follow-up.
- إنشاء Workflow.
- تحليل الشكاوى.
- استخراج Insights.
- قراءة الملفات والصور والفواتير والعقود إذا أرسلت كصورة أو ملف مدعوم.

### مرفوض في النظام

- OCR Engine منفصل.
- Whisper.
- Speech-to-text منفصل.

### Media Flow

```text
Image / Voice / File
        ↓
Media Gateway
        ↓
Gemini 3.5 Flash
        ↓
Understanding
        ↓
AI Decision
        ↓
CRM / Reply / Workflow / Report
```

---

## 15. AI Company Brain

كل مشروع له عقل AI خاص به.

```text
Project APIs
     ↓
Knowledge Sync
     ↓
Company Memory
     ↓
Semantic Graph
     ↓
AI Retrieval Layer
     ↓
Gemini 3.5 Flash
```

### مصادر البيانات

- APIs المشروع.
- أسعار الخدمات.
- الخدمات.
- الطلبات.
- العملاء.
- الـCRM.
- المستندات.
- الـDashboards.
- المحادثات.
- التقارير.
- الحملات.
- قواعد التشغيل.

### وظائف Company Brain

- فهم خدمات المشروع.
- فهم الأسعار.
- فهم سياسة المشروع.
- فهم دورة البيع.
- فهم العملاء.
- فهم المشاكل المتكررة.
- فهم الموظفين.
- الرد بناءً على بيانات حقيقية.
- منع الهبد في الأسعار والخدمات.

---

## 16. Knowledge Approval System

### الفكرة

الـAI يقدر يقترح Knowledge جديدة من المحادثات أو APIs، لكن الأدمن يراجع قبل اعتمادها إذا كانت مهمة.

```text
AI Extracts Knowledge
        ↓
Pending Knowledge Queue
        ↓
Admin Review
        ↓
Approve / Edit / Reject
        ↓
Published Knowledge Base
```

### أمثلة Knowledge

- سؤال متكرر جديد.
- اعتراض متكرر.
- مشكلة متكررة.
- سياسة غير موثقة.
- خدمة يسأل عنها العملاء.
- معلومة سعرية تحتاج مراجعة.
- رد جاهز مقترح.

---

## 17. AI Action & Approval System

رغم أن الـAI مسموح له ينفذ، لازم فيه حماية ذكية حسب درجة الخطورة.

```text
AI Action
   ↓
Risk Analyzer
   ↓
Low Risk       → Execute Immediately
Medium Risk    → Execute + Audit Log
High Risk      → Admin Approval
Critical Risk  → Block + Notify Admin
```

### Low Risk

- إضافة Tag.
- إنشاء Note.
- تلخيص محادثة.
- إنشاء Follow-up عادي.
- تصنيف عميل.
- تحديث Lead Score.

### Medium Risk

- تحديث CRM Field.
- إعادة توزيع محادثة.
- إنشاء Task لموظف.
- تعديل Pipeline Stage.
- اقتراح Workflow.

### High Risk

- إرسال حملة.
- تغيير سعر.
- إعطاء خصم.
- تعديل Knowledge رسمي.
- إنشاء Workflow يعمل تلقائيًا على عدد كبير.
- إرسال رسالة حساسة.

### Critical Risk

- حذف بيانات.
- إرسال جماعي كبير بدون مراجعة.
- تغيير صلاحيات.
- تعديل بيانات مالية.
- أي Action يخالف قواعد المشروع.

---

## 18. CRM Engine

```text
CRM Engine
│
├── Customer Profile Manager
├── Dynamic Fields Manager
├── Customer Timeline
├── Tags Engine
├── Lead Score Engine
├── Health Score Engine
├── Customer Memory
├── Opportunity Manager
├── Deal Manager
├── CRM Auto Update Engine
├── Customer Segmentation
├── Relationship Graph
└── CRM Sync
```

### CRM Features

- ملف عميل كامل.
- بيانات العميل.
- كل المحادثات.
- كل الملاحظات.
- كل الـTags.
- كل المتابعات.
- كل الحملات المرسلة.
- كل الشكاوى.
- كل الفرص البيعية.
- Lead Score.
- Health Score.
- Customer Status.
- Customer Timeline.
- Auto Enrichment.
- Dynamic Fields.
- CRM Mapping لكل مشروع.
- تحديث تلقائي من الرسائل.
- ذاكرة طويلة المدى لكل عميل.

### أمثلة تحديث CRM

```text
رسالة: أنا من طنطا
→ City = طنطا

رسالة: الميزانية ٢ مليون
→ Budget = 2,000,000

رسالة: عايز تقسيط
→ InterestedInInstallments = true

رسالة: كلمني آخر الشهر
→ Follow-up Date = آخر الشهر
```

---

## 19. Customer Memory

```text
Customer Memory
│
├── Preferences
├── Important Facts
├── Objections
├── Purchase Intent
├── Previous Interests
├── Follow-up History
├── Conversation Summaries
├── Marketing Notes
├── Risk Notes
└── Long-Term Summary
```

### الهدف

الـAI والموظف يعرفون العميل بدون قراءة كل المحادثات القديمة.

---

## 20. Follow-up Engine

```text
Follow-up Engine
│
├── Auto Follow-up Creator
├── Smart Reminder System
├── AI Follow-up Message Generator
├── Follow-up Scheduler
├── Pipeline Follow-up Rules
├── Missed Follow-up Detector
├── Customer Revival Engine
├── Re-engagement Engine
└── Follow-up Analytics
```

### مميزات المتابعة

- Follow-up تلقائي لكل عميل محتاج متابعة.
- تذكير الموظف.
- إرسال متابعة تلقائية.
- تحديد أفضل وقت للمتابعة.
- اكتشاف العميل الذي اختفى.
- إعادة فتح محادثة مغلقة.
- متابعة العملاء الباردين.
- Follow-up حسب المرحلة.
- Follow-up حسب نية العميل.
- Follow-up حسب تاريخ آخر رد.

---

## 21. Assignment Engine

```text
Assignment Engine
│
├── Agent Availability
├── Workload Analyzer
├── Load Balancer
├── Project Queue
├── Priority Router
├── Complaint Router
├── VIP Router
├── Skill Router
├── Escalation Manager
├── Reassignment Engine
└── Idle Detector
```

### قواعد التوزيع

- كل موظف لمشروعه فقط.
- المحادثة تذهب لأقل موظف ضغطًا.
- العميل VIP يذهب لمشرف أو موظف محدد.
- الشكوى تذهب لمشرف.
- العميل الجاهز للشراء يذهب لمبيعات.
- لو الموظف Offline يعاد التوزيع.
- لو الموظف تأخر، تصعيد تلقائي.

---

## 22. Workflow Engine

```text
Workflow Engine
│
├── AI Workflow Builder
├── Trigger Engine
├── Condition Engine
├── Action Executor
├── Delay Engine
├── Workflow Versioning
├── Workflow Analytics
├── Workflow Safety
└── Workflow Approval
```

### الفكرة

أنت لا تريد بناء Workflows يدويًا؛ الـAI ينشئها من سلوك العملاء.

### أمثلة

```text
لو العميل سأل عن السعر
→ أضف Tag: Interested In Price
→ ارفع Lead Score
→ لو لم يرد خلال 24 ساعة اعمل Follow-up

لو العميل غاضب
→ أوقف AI Reply
→ حول لمشرف
→ أرسل Alert

لو العميل طلب تقسيط
→ حدّث CRM
→ أرسل شروط التقسيط
→ اعمل Follow-up بعد يوم
```

---

## 23. Scheduler Engine

```text
Scheduler Engine
│
├── Cron Jobs
├── Delayed Jobs
├── Recurring Jobs
├── Retry Manager
├── Dead Letter Handler
├── Queue Dispatcher
├── Monitoring Jobs
├── Cleanup Jobs
├── Report Jobs
├── AI Re-analysis Jobs
├── CRM Maintenance Jobs
└── API Sync Jobs
```

### Jobs أساسية

- تقارير يومية.
- تقارير أسبوعية.
- إعادة حساب Lead Score.
- إعادة حساب Health Score.
- تنفيذ Follow-ups.
- إعادة تصنيف العملاء.
- تحليل الشكاوى.
- Sync مع APIs المشاريع.
- مراقبة واتساب.
- مراقبة Queues.
- تنظيف Sessions.
- تنظيف Cache.
- تحليل العملاء الصامتين.
- تحليل العملاء المعرضين للخسارة.

---

## 24. Campaign Engine

```text
Campaign Engine
│
├── Audience Builder
├── Segment Selector
├── Campaign Scheduler
├── Message Generator
├── Anti-Ban Sender
├── Delivery Tracker
├── Response Tracker
├── Campaign Analytics
├── A/B Testing
└── Re-engagement Campaigns
```

### مميزاته

- إرسال حسب Tag.
- إرسال حسب Segment.
- إرسال حسب مشروع.
- إرسال حسب حالة العميل.
- جدولة حملة.
- Smart Sending.
- منع الحظر.
- تتبع الردود.
- تتبع المبيعات الناتجة.
- AI يكتب الرسائل.
- AI يختار الجمهور.
- AI يحلل النتائج.

---

## 25. Analytics Engine

```text
Analytics Engine
│
├── Customer Analytics
├── Sales Analytics
├── Complaint Analytics
├── Team Analytics
├── AI Analytics
├── Campaign Analytics
├── Follow-up Analytics
├── SLA Analytics
├── Funnel Analytics
├── Retention Analytics
├── Revenue Analytics
├── Predictive Analytics
└── Executive Insights
```

### تقارير مهمة

- ليه العميل لم يرد؟
- أكثر شكاوى متكررة.
- أكثر خدمة مطلوبة.
- العملاء الساخنين.
- العملاء الباردين.
- العملاء المعرضين للفقد.
- أداء كل موظف.
- متوسط سرعة الرد.
- فرص البيع الضائعة.
- الحملات الناجحة.
- أسباب فشل الحملات.
- أسباب خسارة العملاء.
- أوقات الضغط.
- أداء الـAI.
- أكثر Workflows فعالية.
- تقارير يومية وشهرية.

---

## 26. Reports System

### أنواع التقارير

#### Daily Operations Report
- عدد الرسائل.
- عدد العملاء الجدد.
- المحادثات المفتوحة.
- المحادثات المغلقة.
- العملاء بدون متابعة.
- الشكاوى.
- أداء الموظفين.
- أداء الـAI.

#### Complaint Report
- نوع الشكوى.
- عدد العملاء المتأثرين.
- متى بدأت.
- المشروع المتأثر.
- الموظفين المرتبطين.
- توصيات الحل.

#### Lost Customers Report
- العملاء الذين اختفوا.
- آخر تفاعل.
- سبب متوقع.
- إجراء مقترح.

#### Follow-up Report
- المتابعات المطلوبة.
- المتابعات المتأخرة.
- المتابعات الناجحة.
- الموظفين المتأخرين.

#### AI Report
- عدد ردود AI.
- معدل النجاح.
- معدل التحويل لبشري.
- أكثر أسباب عدم الثقة.
- أخطاء محتملة.

---

## 27. Notifications Engine

```text
Notifications
│
├── Realtime Alerts
├── Follow-up Alerts
├── Complaint Alerts
├── SLA Alerts
├── VIP Alerts
├── WhatsApp Alerts
├── Campaign Alerts
├── AI Risk Alerts
├── System Alerts
└── Admin Approval Alerts
```

---

## 28. Knowledge Base Engine

```text
Knowledge Base
│
├── Documents
├── FAQs
├── Services
├── Pricing Rules
├── Policies
├── Objection Handling
├── Approved Replies
├── Project Knowledge
├── Embeddings
├── Semantic Search
├── Knowledge Approval
└── Knowledge Versioning
```

### مميزاته

- Knowledge لكل مشروع.
- استيراد من APIs.
- استيراد من ملفات.
- اقتراح Knowledge من المحادثات.
- موافقة الأدمن.
- Versioning.
- Semantic Search.
- RAG.
- كشف المعرفة الناقصة.

---

## 29. Integration Layer

```text
Integration Layer
│
├── Project API Connector
├── Auth Token Manager
├── Customer Sync
├── Services Sync
├── Pricing Sync
├── Orders Sync
├── Appointments Sync
├── CRM Field Mapping
├── Webhook Dispatcher
├── External Event Listener
└── Sync Scheduler
```

### لكل مشروع

- API Base URL.
- Auth Token.
- Services Endpoint.
- Prices Endpoint.
- Customers Endpoint.
- Orders Endpoint.
- Appointments Endpoint.
- CRM Mapping.
- AI Permissions.
- Sync Rules.

---

## 30. Event-Driven Architecture

كل Action مهم يتحول إلى Event.

```text
Events
│
├── MessageReceived
├── MessageAggregated
├── AIAnalyzed
├── AIReplyGenerated
├── AIActionRequested
├── CRMUpdateSuggested
├── CRMUpdated
├── FollowUpCreated
├── FollowUpExecuted
├── WorkflowCreated
├── WorkflowTriggered
├── CampaignScheduled
├── CampaignSent
├── CustomerClassified
├── ComplaintDetected
├── AgentAssigned
├── ApprovalRequested
├── ApprovalApproved
├── ApprovalRejected
├── ReportGenerated
└── NotificationSent
```

---

## 31. Queue Architecture

```text
RabbitMQ
│
├── incoming-message-queue
├── message-aggregation-queue
├── ai-processing-queue
├── ai-reply-queue
├── crm-update-queue
├── follow-up-queue
├── workflow-queue
├── campaign-queue
├── analytics-queue
├── reports-queue
├── notification-queue
├── media-processing-queue
├── integration-sync-queue
├── approval-queue
├── retry-queue
└── dead-letter-queue
```

### لماذا Queue مهمة؟

- تمنع تهنيج النظام.
- تفصل استقبال الرسائل عن AI.
- تسمح بإعادة المحاولة.
- تحافظ على النظام لو Gemini تأخر.
- تجعل الحملات والمتابعات آمنة.

---

## 32. Database Design

### Core Tables

```text
Projects
ProjectSettings
ProjectApiConnectors
ProjectKnowledgeSettings

Users
Roles
Permissions
UserProjects
UserPresence

WhatsAppSessions
WhatsAppNumbers
WhatsAppMessagesStatus

Customers
CustomerProfiles
CustomerDynamicFields
CustomerMemories
CustomerScores
CustomerTags
CustomerTimeline
CustomerRelationships

Conversations
ConversationParticipants
ConversationAssignments
ConversationStates

Messages
MessageMedia
MessageAggregations
MessageAIAnalysis

CRMUpdates
CRMUpdateHistory

FollowUps
Tasks
Pipelines
PipelineStages

Workflows
WorkflowRules
WorkflowExecutions
WorkflowActions

Campaigns
CampaignAudiences
CampaignMessages
CampaignResults

AIRequests
AIResponses
AIInsights
AIActions
AIApprovals
AIMemories

KnowledgeDocuments
KnowledgeChunks
KnowledgeApprovals
Embeddings

Reports
ReportItems
AnalyticsSnapshots

Notifications
NotificationSettings

AuditLogs
SystemLogs
ErrorLogs
```

---

## 33. Redis Usage

```text
Redis
│
├── User Sessions
├── Agent Presence
├── Rate Limits
├── Temporary Message Aggregation
├── Temporary AI Context
├── Cache
├── SignalR Scaleout
├── WhatsApp Session Metadata
└── Fast Counters
```

---

## 34. Elasticsearch Usage

```text
Elasticsearch
│
├── Conversation Search
├── Message Search
├── Customer Search
├── Notes Search
├── Reports Search
├── Audit Search
└── Analytics Exploration
```

---

## 35. Vector Database Usage

```text
Vector DB / pgvector
│
├── Knowledge Embeddings
├── Customer Memory Embeddings
├── Similar Conversations
├── RAG Context
├── Similar Complaints
├── Similar Leads
└── Semantic Search
```

---

## 36. File Storage

```text
Object Storage
│
├── Images
├── Voice Notes
├── Documents
├── PDFs
├── Campaign Attachments
├── Reports
├── Backups
└── Exported Data
```

---

## 37. Security Architecture

```text
Security
│
├── JWT Auth
├── Refresh Tokens
├── Role-Based Access
├── Project-Based Access
├── Action Permissions
├── Field Permissions
├── API Keys
├── IP Restrictions
├── 2FA
├── Encryption At Rest
├── Encryption In Transit
├── Audit Logs
└── Sensitive Action Approval
```

---

## 38. Realtime Architecture

```text
SignalR
│
├── Live Inbox
├── New Message Alerts
├── Typing Indicators
├── Agent Presence
├── Live Dashboard
├── Queue Status
├── SLA Alerts
├── Approval Alerts
├── Campaign Progress
└── System Health
```

---

## 39. Monitoring & Observability

```text
Monitoring
│
├── API Health Checks
├── Worker Health Checks
├── RabbitMQ Monitoring
├── Redis Monitoring
├── PostgreSQL Monitoring
├── WhatsApp Session Monitoring
├── Gemini Latency Monitoring
├── AI Cost Monitoring
├── Error Logs
├── Audit Logs
├── Performance Metrics
└── Alerts
```

### أدوات مقترحة

- Serilog
- OpenTelemetry لاحقًا
- Grafana لاحقًا
- Prometheus لاحقًا
- Loki لاحقًا
- Seq كبداية ممتازة

---

## 40. Frontend Main Screens

```text
Dashboard
Inbox
Customers CRM
Customer Profile
Follow-ups
Tasks
Campaigns
Reports
AI Insights
Approvals
Knowledge Base
Workflows
Project Settings
Users & Roles
WhatsApp Sessions
System Health
Audit Logs
```

### Inbox Layout

```text
┌──────────────────────┬────────────────────────────┬──────────────────────┐
│ Conversation List    │ Chat Window                 │ Customer Panel        │
│                      │                            │                      │
│ Filters              │ Messages                    │ CRM Info              │
│ Search               │ AI Suggestions              │ Tags                  │
│ Queues               │ Notes                       │ Follow-ups            │
│ Priorities           │ Media                       │ Timeline              │
└──────────────────────┴────────────────────────────┴──────────────────────┘
```

---

## 41. Full Message Flow

```text
Customer Sends WhatsApp Message
        ↓
Baileys Receives Message
        ↓
Webhook to Backend
        ↓
Save Raw Message
        ↓
Message Aggregator Waits
        ↓
Build Full Customer Context
        ↓
Retrieve Company Brain Context
        ↓
Send Text/Image/Voice to Gemini 3.5 Flash
        ↓
AI Understands Intent + Entities + Sentiment
        ↓
AI Generates CRM Updates + Actions + Reply Strategy
        ↓
Risk Analyzer Checks Actions
        ↓
Execute Low Risk / Queue Approval for High Risk
        ↓
Smart Human Messaging Engine Splits Reply
        ↓
Smart Delay Engine Sends Messages Naturally
        ↓
Save Reply
        ↓
Update CRM
        ↓
Create Follow-ups
        ↓
Trigger Workflows
        ↓
Update Analytics
        ↓
Notify Dashboard
```

---

## 42. AI Reply Strategy Flow

```text
AI Receives Context
        ↓
Detect Customer Type
        ↓
Detect Intent
        ↓
Detect Urgency
        ↓
Detect Sales Stage
        ↓
Choose Reply Mode
        ↓
Choose Number of Messages
        ↓
Choose Tone
        ↓
Choose CTA
        ↓
Choose Delay
        ↓
Send Reply
```

---

## 43. AI Workflow Creation Flow

```text
Repeated Pattern Detected
        ↓
AI Suggests Workflow
        ↓
Risk Analyzer
        ↓
Admin Approval if Needed
        ↓
Workflow Published
        ↓
Workflow Runs Automatically
        ↓
Analytics Measure Impact
```

---

## 44. CRM Update Flow

```text
Customer Message
        ↓
AI Extracts Data
        ↓
CRM Update Proposal
        ↓
Risk Check
        ↓
Apply or Approval
        ↓
CRM History Saved
        ↓
Timeline Updated
```

---

## 45. Follow-up Flow

```text
Customer Intent Detected
        ↓
AI Decides Follow-up Needed
        ↓
Follow-up Created
        ↓
Scheduler Waits Until Due
        ↓
Send Reminder / Auto Message
        ↓
Track Response
        ↓
Update Customer Status
```

---

## 46. Campaign Flow

```text
Campaign Created
        ↓
Audience Selected
        ↓
AI Optimizes Message
        ↓
Schedule
        ↓
Anti-Ban Sending
        ↓
Track Delivery
        ↓
Track Replies
        ↓
Update CRM
        ↓
Generate Report
```

---

## 47. Approval Flow

```text
AI Suggests Sensitive Action
        ↓
Create Approval Request
        ↓
Admin Notification
        ↓
Admin Opens Queue
        ↓
Approve / Edit / Reject
        ↓
Execute Approved Action
        ↓
Audit Log
```

---

## 48. Scheduler Flow

```text
Cron Job Trigger
        ↓
Load Due Jobs
        ↓
Push to RabbitMQ
        ↓
Worker Executes
        ↓
Success → Save Result
        ↓
Failure → Retry
        ↓
Repeated Failure → Dead Letter Queue
```

---

## 49. Feature List — Complete

### Messaging
- WhatsApp via Baileys.
- Multi-number later.
- Text messages.
- Images.
- Voice notes.
- Documents.
- Delivery tracking.
- Read tracking.
- Retry.
- Session recovery.
- Auto reconnect.
- Anti-ban sending.
- Smart delays.

### Conversation
- Unified inbox.
- Conversation states.
- Priorities.
- Notes.
- Drafts.
- Search.
- Filters.
- Locking.
- Assignment.
- Escalation.
- SLA.
- Merge/Split.
- Auto reopen.

### AI
- Auto reply.
- Multi-message aggregation.
- Smart message chunking.
- Marketing brain.
- Human-like pacing.
- Gemini 3.5 Flash.
- Image understanding.
- Voice understanding.
- Intent detection.
- Sentiment.
- Entity extraction.
- Classification.
- Lead scoring.
- CRM updates.
- Follow-up creation.
- Workflow generation.
- Analytics.
- Reports.
- Safety layer.
- Approval layer.
- Memory.
- Company Brain.

### CRM
- Dynamic profiles.
- Custom fields.
- Auto update.
- Timeline.
- Tags.
- Scores.
- Memory.
- Opportunities.
- Deals.
- Segments.
- Relationship graph.
- Sync with project APIs.

### Follow-up
- Auto follow-ups.
- Reminders.
- Scheduler.
- Customer revival.
- Missed follow-up detection.
- Smart timing.
- Re-engagement.

### Assignment
- Project-based agents.
- Load balancing.
- Workload monitoring.
- Complaint routing.
- VIP routing.
- Priority routing.
- Escalation.
- Reassignment.

### Workflow
- AI-generated workflows.
- Trigger rules.
- Conditions.
- Delays.
- Actions.
- Approval.
- Versioning.
- Analytics.

### Campaigns
- Broadcast.
- Segments.
- Scheduling.
- AI message optimization.
- Smart sending.
- Anti-ban.
- Tracking.
- A/B testing.
- Campaign reports.

### Analytics
- Customer analytics.
- Complaint analytics.
- AI analytics.
- Team analytics.
- Funnel analytics.
- Revenue analytics.
- Follow-up analytics.
- Predictive insights.
- Executive reports.

### Scheduler
- Cron jobs.
- Delayed jobs.
- Recurring jobs.
- Retry.
- Dead letters.
- Reports.
- Re-analysis.
- Sync jobs.
- Cleanup jobs.

### Knowledge
- Project knowledge.
- Approved knowledge.
- Suggested knowledge.
- Versioning.
- RAG.
- Semantic search.
- Knowledge gaps.

### Security
- JWT.
- Roles.
- Permissions.
- Project isolation.
- 2FA.
- Audit logs.
- Encryption.
- Approval for sensitive actions.

### Monitoring
- Logs.
- Metrics.
- Health checks.
- Queue monitoring.
- Worker monitoring.
- WhatsApp monitoring.
- AI latency.
- Error alerts.

---

## 50. Final Architecture Summary

```text
Smart Customer Core
│
├── Web Dashboard
├── ASP.NET Core Modular Backend
├── Baileys WhatsApp Gateway
├── Gemini 3.5 Flash AI Layer
├── Company AI Brain
├── AI Marketing Brain
├── CRM Engine
├── Follow-up Engine
├── Workflow Engine
├── Scheduler Engine
├── Campaign Engine
├── Analytics Engine
├── Approval Engine
├── RabbitMQ Queues
├── PostgreSQL Database
├── Redis Cache
├── Elasticsearch Search
├── Vector DB
├── SignalR Realtime
└── Monitoring & Audit
```

---

## 51. MVP Development Order

### Phase 1
- Auth
- Projects
- Users/Roles
- WhatsApp Gateway
- Conversations
- Basic CRM
- Gemini reply
- Message Aggregator
- Basic Follow-up

### Phase 2
- AI CRM Updates
- AI Marketing Brain
- Smart Chunking
- Assignment Engine
- Scheduler
- Notifications
- Reports

### Phase 3
- Company Brain
- Knowledge Base
- Workflows
- Campaigns
- Approval System
- Analytics

### Phase 4
- Advanced AI Insights
- Predictive Analytics
- Mobile API
- Scaling
- Advanced Monitoring

---

## 52. Important Production Notes

- لا تجعل AI يرد قبل تجميع رسائل العميل.
- لا تجعل AI يخترع أسعار.
- السعر يأتي من API المشروع أو Knowledge معتمدة.
- كل AI Action يتسجل في Audit.
- كل CRM Update يتسجل في History.
- كل Follow-up له حالة واضحة.
- كل Workflow له Version.
- كل Campaign لها Tracking.
- كل Project له إعداداته المستقلة.
- كل رسالة Media تحفظ قبل إرسالها للـAI.
- لا تعتمد على OCR أو Whisper منفصلين.
- Gemini 3.5 Flash هو محرك الفهم للصور والفويس والنص.
- السيرفر الواحد مناسب للبداية، لكن التصميم يسمح بالتوسع.

---

# End of Document


---

# 53. Updated Deployment Philosophy (Per-Project Deployment)

## التعديل النهائي المهم

النظام لم يعد Central Platform تخدم كل المشاريع من سيرفر واحد.

بل أصبح:

# Reusable Smart Customer Core

يتم رفعه على سيرفر مستقل لكل مشروع.

---

## الشكل النهائي الصحيح

```text
Project A Server
│
├── Frontend
├── ASP.NET Core Backend
├── Smart Customer Core Modules
├── Baileys Service
├── PostgreSQL
├── Redis
├── RabbitMQ
├── Vector DB
├── File Storage
├── Workers
├── Scheduler
└── Monitoring

Project B Server
│
├── نسخة مستقلة بالكامل
```

---

# لماذا هذا أفضل لك؟

لأنك:
- لا تبني SaaS عامة.
- كل مشروع عندك مستقل.
- كل مشروع له بياناته.
- كل مشروع له واتساب خاص.
- كل مشروع له AI خاص.
- كل مشروع له CRM خاص.
- كل مشروع له APIs مختلفة.

---

# النتيجة

كل مشروع يصبح:

```text
Self-Contained AI Operations System
```

---

# Updated Isolation Model

العزل لم يعد داخل نفس النظام فقط.

بل:

## Isolation على مستوى السيرفر بالكامل

يعني:
- Database مستقلة.
- Redis مستقلة.
- RabbitMQ مستقلة.
- WhatsApp Session مستقلة.
- File Storage مستقل.
- AI Settings مستقلة.
- Knowledge Base مستقلة.
- Monitoring مستقل.
- Logs مستقلة.

---

# Updated Deployment Architecture

```text
                    ┌────────────────────┐
                    │   Project Server   │
                    └─────────┬──────────┘
                              │
      ┌───────────────────────┼───────────────────────┐
      ▼                       ▼                       ▼

┌──────────────┐     ┌──────────────┐      ┌──────────────┐
│ Frontend     │     │ ASP.NET Core │      │ Baileys      │
│ React/Next   │     │ Backend      │      │ Node.js       │
└──────────────┘     └──────────────┘      └──────────────┘

                              │
                              ▼

                     ┌────────────────┐
                     │    RabbitMQ    │
                     └────────────────┘

                              │
      ┌───────────────────────┼───────────────────────┐
      ▼                       ▼                       ▼

┌──────────────┐     ┌──────────────┐      ┌──────────────┐
│ PostgreSQL   │     │ Redis        │      │ Vector DB    │
└──────────────┘     └──────────────┘      └──────────────┘

                              │
                              ▼

                     ┌────────────────┐
                     │ File Storage   │
                     └────────────────┘
```

---

# Deployment Strategy

## كل مشروع له:

- Docker Compose مستقل.
- Environment Variables مستقلة.
- Domain مستقل.
- SSL مستقل.
- API Keys مستقلة.
- Gemini API Key مستقلة.
- WhatsApp Session مستقلة.
- Cron Jobs مستقلة.
- Workers مستقلة.

---

# Advantages

## 1. أمان أعلى
أي اختراق لا يؤثر على باقي المشاريع.

---

## 2. استقرار أعلى
لو مشروع وقع لا يسقط باقي المشاريع.

---

## 3. سهولة تخصيص
كل مشروع يمكن تعديله براحة.

---

## 4. سهولة نقل المشروع
يمكن نقل أي مشروع لسيرفر جديد بسهولة.

---

## 5. سهولة Scaling لاحقًا
كل مشروع يكبر حسب احتياجه.

---

# Updated Infrastructure Recommendation

## البداية

```text
Single Ubuntu VPS Per Project
```

### يحتوي على:
- Docker
- PostgreSQL
- Redis
- RabbitMQ
- ASP.NET Core
- Baileys
- Vector DB
- Nginx

---

# Future Scaling

لو مشروع كبر:

```text
API Server
Separate Worker Server
Separate Database Server
Separate AI Worker Server
Separate Elasticsearch Server
```

لكن البداية:
- VPS واحدة ممتازة.

---

# Updated Monitoring

كل مشروع له:
- Logs مستقلة.
- Metrics مستقلة.
- Monitoring مستقل.
- Alerts مستقلة.
- Backups مستقلة.

---

# Updated Backup Strategy

لكل مشروع:
- Database Backup.
- File Backup.
- WhatsApp Session Backup.
- Redis Snapshot.
- Configuration Backup.

---

# Final Architecture Philosophy

أنت لا تبني:

- CRM فقط
- WhatsApp Bot فقط
- Helpdesk فقط

بل تبني:

```text
Deployable AI Customer Operations Core
```

يتم تركيبه داخل أي مشروع لديك ويعمل كعقل تشغيلي كامل للمشروع.


---

# 54. Shared Central Assets & Media Architecture

## الفكرة

أنت لا تريد:
- نسخ الملفات داخل كل منصة.
- تكرار الصور والفيديوهات والملفات.
- تخزين نفس الـAssets أكثر من مرة.

بل تريد:

# Central Shared Assets Storage

يعني:
كل الملفات تكون محفوظة مرة واحدة فقط على الهارد أو Storage مركزي.

وكل المنصات:
- تسحب منها
- تعرضها بطريقتها
- بدون نسخ جديدة

---

# الشكل المعماري

```text
                     Shared Assets Storage
┌────────────────────────────────────────────────────┐
│                                                    │
│ Images                                             │
│ Videos                                             │
│ PDFs                                               │
│ Voice Notes                                        │
│ Documents                                          │
│ Campaign Assets                                    │
│ Branding Assets                                    │
│ Product Media                                      │
│                                                    │
└────────────────────────────────────────────────────┘
                ▲                 ▲
                │                 │
      ┌─────────┘                 └─────────┐
      ▼                                     ▼

┌──────────────┐                   ┌──────────────┐
│ Web Platform │                   │ Mobile App   │
└──────────────┘                   └──────────────┘

      ▲                                     ▲
      │                                     │

┌──────────────┐                   ┌──────────────┐
│ Dashboard UI │                   │ WhatsApp AI  │
└──────────────┘                   └──────────────┘
```

---

# Shared Media Philosophy

الملف لا يتم نسخه لكل منصة.

بل:
- يتم حفظه مرة واحدة.
- ويتم الوصول له عبر:
  - File Path
  - Asset ID
  - CDN URL
  - Secure Media URL

---

# Asset Management System

```text
Asset System
│
├── Asset Registry
├── Asset Metadata
├── File References
├── Secure URLs
├── Asset Permissions
├── Asset Versioning
├── Media Optimization
├── Thumbnail Generator
├── Preview Generator
└── Media Cache
```

---

# Database Structure

## Assets Table

```text
Assets
│
├── Id
├── FileName
├── FilePath
├── MimeType
├── FileSize
├── Width
├── Height
├── Duration
├── ProjectId
├── CreatedBy
├── StorageProvider
├── Hash
├── Tags
├── Metadata
├── IsShared
└── CreatedAt
```

---

# Asset References

بدل ما المنصة تخزن الملف نفسه:

```text
ConversationMessage
│
├── AssetId
└── AssetReference
```

---

# مثال عملي

## صورة منتج

تتحفظ مرة واحدة:

```text
/storage/products/product1.jpg
```

## WhatsApp
يرسل منها مباشرة.

## Dashboard
يعرض Preview منها.

## Mobile App
يسحبها بنفس الـAsset ID.

## Campaign System
يستخدم نفس الملف.

بدون نسخ جديدة.

---

# Platform-Specific Rendering

رغم أن الملف واحد،
لكن كل منصة تعرضه بشكل مختلف.

---

# Example

## نفس الـAsset

```text
product_video.mp4
```

### WhatsApp
- يتم ضغطه.
- تحويله لصيغة مناسبة.
- Thumbnail صغيرة.

### Dashboard
- Full Preview.
- Metadata كاملة.
- Controls كاملة.

### Mobile App
- Streaming.
- Adaptive quality.

---

# Smart Media Adapter Layer

```text
Asset
   ↓
Media Adapter
   ↓

WhatsApp Format
Dashboard Format
Mobile Format
Campaign Format
API Format
```

---

# Media Transformation Engine

مسؤول عن:
- Resize
- Compression
- Thumbnail generation
- Format conversion
- Streaming optimization
- WhatsApp optimization
- Mobile optimization

---

# Supported Storage Types

## Local Storage
للبداية ممتاز.

```text
/storage
```

---

## S3 Compatible Storage
لاحقًا.

مثل:
- MinIO
- AWS S3
- Cloudflare R2

---

# Shared Asset Features

- Single file storage
- Asset references
- Smart caching
- Media optimization
- Multi-platform rendering
- Asset versioning
- Secure URLs
- Shared usage tracking
- AI media tagging
- AI media classification

---

# AI Media Understanding

الـAI يستطيع:
- فهم الصور
- فهم الفيديوهات
- فهم الفويس
- تصنيف الـAssets
- استخراج معلومات منها

بدون نسخ إضافية.

---

# Updated File Architecture

```text
Storage
│
├── projects
│   ├── project-a
│   │   ├── conversations
│   │   ├── products
│   │   ├── campaigns
│   │   ├── reports
│   │   └── branding
│   │
│   └── project-b
│
├── shared-assets
├── ai-generated
├── temporary
├── thumbnails
└── backups
```

---

# Final Shared Storage Philosophy

النظام لا يعمل:
- File Duplication

بل يعمل:
- Central Shared Assets
- Reference-Based Media System
- Platform-Specific Rendering
- Smart Media Delivery
