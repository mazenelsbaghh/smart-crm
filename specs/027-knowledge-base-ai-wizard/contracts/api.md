# API Interface Contracts: Knowledge Base AI Wizard

We define two new API endpoints under `KnowledgeBaseController` to handle text analysis and Q&A generation.

## 1. Analyze Text for Clarification Questions

- **Endpoint**: `POST /api/projects/{projectId}/knowledge/wizard/analyze`
- **Authentication**: Bearer Token required.
- **Content-Type**: `application/json`

### Request Payload

```json
{
  "rawText": "نحن محل ملابس شيك ستور في المعادي، نقدم ملابس كاجوال رجالي. الشحن متاح داخل القاهرة فقط."
}
```

### Success Response

- **Status Code**: `200 OK`
- **Body**:

```json
[
  {
    "question": "ما هي أسعار وتكلفة الشحن داخل القاهرة؟",
    "options": [
      "سعر الشحن موحد 50 جنيه",
      "الشحن مجاني لجميع الطلبات",
      "يتم احتساب الشحن حسب المنطقة (مثلا 30 إلى 60 جنيه)"
    ]
  },
  {
    "question": "هل يتوفر لديكم خيار الدفع عند الاستلام؟",
    "options": [
      "نعم، الدفع عند الاستلام هو الطريقة الأساسية",
      "لا، نقبل الدفع المسبق فقط عبر فودافون كاش أو بطاقة الائتمان",
      "نقبل الدفع عند الاستلام بالإضافة للمحافظ الإلكترونية"
    ]
  },
  {
    "question": "ما هي سياسة الاستبدال والاسترجاع للملابس؟",
    "options": [
      "الاستبدال والاسترجاع مجاني خلال 14 يوماً من الاستلام",
      "الاسترجاع غير متاح، الاستبدال فقط خلال 3 أيام",
      "الاستبدال والاسترجاع متاح بشرط تحمل العميل لمصاريف الشحن"
    ]
  }
]
```

## 2. Generate Final Q&As

- **Endpoint**: `POST /api/projects/{projectId}/knowledge/wizard/generate`
- **Authentication**: Bearer Token required.
- **Content-Type**: `application/json`

### Request Payload

```json
{
  "rawText": "نحن محل ملابس شيك ستور في المعادي، نقدم ملابس كاجوال رجالي. الشحن متاح داخل القاهرة فقط.",
  "answers": [
    {
      "question": "ما هي أسعار وتكلفة الشحن داخل القاهرة؟",
      "answer": "سعر الشحن موحد 50 جنيه"
    },
    {
      "question": "هل يتوفر لديكم خيار الدفع عند الاستلام؟",
      "answer": "نعم، الدفع عند الاستلام هو الطريقة الأساسية"
    },
    {
      "question": "ما هي سياسة الاستبدال والاسترجاع للملابس؟",
      "answer": "الاستبدال والاسترجاع مجاني خلال 14 يوماً من الاستلام"
    }
  ]
}
```

### Success Response

- **Status Code**: `200 OK`
- **Body**:

```json
[
  {
    "question": "ما هي أسعار الشحن وتفاصيل التوصيل؟",
    "answer": "تكلفة الشحن موحدة بقيمة 50 جنيه لجميع المناطق داخل القاهرة."
  },
  {
    "question": "هل يمكنني الدفع عند الاستلام؟",
    "answer": "نعم، الدفع عند الاستلام هو وسيلة الدفع الأساسية المتاحة لعملائنا."
  },
  {
    "question": "ما هي سياسة الاستبدال والاسترجاع لديكم؟",
    "answer": "نوفر إمكانية الاستبدال والاسترجاع المجاني خلال 14 يوماً من تاريخ استلام الطلب."
  }
]
```
