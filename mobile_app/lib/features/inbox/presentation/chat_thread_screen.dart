import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:intl/intl.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../../core/theme/colors.dart';
import '../../../core/theme/typography.dart';
import '../../../core/widgets/async_state_view.dart';
import '../bloc/inbox_bloc.dart';
import '../data/models/chat_models.dart';
import 'conversation_detail_sheet.dart';

class ChatThreadScreen extends StatefulWidget {
  const ChatThreadScreen({super.key});

  @override
  State<ChatThreadScreen> createState() => _ChatThreadScreenState();
}

class _ChatThreadScreenState extends State<ChatThreadScreen> {
  final _messageController = TextEditingController();
  final _scrollController = ScrollController();

  int _handledSendRevision = 0;
  String? _newestMessageId;
  String? _lastMessageError;
  String? _lastStatusError;
  String? _lastGeneralError;

  @override
  void dispose() {
    _messageController.dispose();
    _scrollController.dispose();
    super.dispose();
  }

  void _scrollBottom() {
    if (!_scrollController.hasClients) return;
    if (MediaQuery.disableAnimationsOf(context)) {
      _scrollController.jumpTo(_scrollController.position.maxScrollExtent);
      return;
    }
    _scrollController.animateTo(
      _scrollController.position.maxScrollExtent,
      duration: const Duration(milliseconds: 220),
      curve: Curves.easeOutQuart,
    );
  }

  void _handleState(InboxState state) {
    if (state.messageSendRevision > _handledSendRevision) {
      _handledSendRevision = state.messageSendRevision;
      if (_messageController.text.trim() == state.lastSentContent) {
        _messageController.clear();
      }
    }

    _announceError(
      state.messageSendError,
      previous: _lastMessageError,
      prefix: 'لم يتم إرسال الرسالة. ',
    );
    _lastMessageError = state.messageSendError;

    _announceError(
      state.statusUpdateError,
      previous: _lastStatusError,
      prefix: 'لم يتم حفظ حالة المحادثة. ',
    );
    _lastStatusError = state.statusUpdateError;

    if (!state.loadingMessages && !state.loadingMoreMessages) {
      _announceError(state.error, previous: _lastGeneralError);
      _lastGeneralError = state.error;
    }

    final newestId = state.messages.isEmpty ? null : state.messages.last.id;
    if (newestId != null && newestId != _newestMessageId) {
      _newestMessageId = newestId;
      WidgetsBinding.instance.addPostFrameCallback((_) => _scrollBottom());
    }
  }

  void _announceError(
    String? error, {
    required String? previous,
    String prefix = '',
  }) {
    if (error == null || error == previous || !mounted) return;
    final messenger = ScaffoldMessenger.of(context);
    messenger
      ..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          content: Text('$prefix$error'),
          backgroundColor: AppColors.error,
          action: SnackBarAction(
            label: 'إغلاق',
            textColor: AppColors.background,
            onPressed: messenger.hideCurrentSnackBar,
          ),
        ),
      );
  }

  void _sendDraft(InboxState state) {
    final content = _messageController.text.trim();
    if (content.isEmpty || state.sendingMessage) return;
    context.read<InboxBloc>().add(InboxMessageSent(content));
  }

  @override
  Widget build(BuildContext context) {
    return BlocConsumer<InboxBloc, InboxState>(
      listener: (_, state) => _handleState(state),
      builder: (context, state) {
        final conversation = state.activeConv;
        if (conversation == null) {
          return const Scaffold(
            body: AppStateView(
              icon: Icons.forum_outlined,
              title: 'لا توجد محادثة مفتوحة',
              message: 'اختر محادثة من القائمة لعرض الرسائل.',
            ),
          );
        }

        return Scaffold(
          appBar: AppBar(
            leading: IconButton(
              tooltip: 'رجوع',
              icon: const Icon(Icons.arrow_forward),
              onPressed: () => Navigator.of(context).pop(),
            ),
            title: Column(
              children: [
                Text(
                  conversation.customer.name.isEmpty
                      ? 'عميل بلا اسم'
                      : conversation.customer.name,
                  style: AppTypography.title,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
                Text(
                  conversation.customer.phone,
                  style: AppTypography.mono,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
              ],
            ),
            actions: [
              IconButton(
                tooltip: 'بيانات العميل',
                icon: const Icon(Icons.info_outline, color: AppColors.primary),
                onPressed: () => showModalBottomSheet<void>(
                  context: context,
                  isScrollControlled: true,
                  isDismissible: false,
                  enableDrag: false,
                  useSafeArea: true,
                  backgroundColor: Colors.transparent,
                  builder: (_) => const ConversationDetailSheet(),
                ),
              ),
            ],
          ),
          body: Column(
            children: [
              _buildStatusSelector(context, state, conversation),
              Expanded(child: _buildMessages(context, state, conversation)),
              if (state.aiTypingConversations[conversation.id] == true)
                _buildAiTypingIndicator(),
              if (state.aiSuggestion != null)
                _buildSuggestionBox(state.aiSuggestion!),
              _buildComposer(state),
            ],
          ),
        );
      },
    );
  }

  Widget _buildMessages(
    BuildContext context,
    InboxState state,
    Conversation conversation,
  ) {
    if (state.loadingMessages) return const AppLoadingSkeleton(rows: 6);
    if (state.messages.isEmpty && state.error != null) {
      return AppStateView(
        icon: Icons.cloud_off_outlined,
        title: 'تعذر تحميل الرسائل',
        message: state.error!,
        actionLabel: 'إعادة المحاولة',
        onAction: () => context.read<InboxBloc>().add(
          InboxActiveConversationSelected(conversation),
        ),
      );
    }
    if (state.messages.isEmpty) {
      return const AppStateView(
        icon: Icons.mark_chat_unread_outlined,
        title: 'ابدأ المحادثة',
        message: 'لم يتم إرسال أي رسائل بعد.',
      );
    }

    return ListView.builder(
      controller: _scrollController,
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 16),
      itemCount: state.messages.length + (state.hasMoreMessages ? 1 : 0),
      itemBuilder: (context, index) {
        if (state.hasMoreMessages && index == 0) {
          return Padding(
            padding: const EdgeInsets.only(bottom: 16),
            child: Center(
              child: OutlinedButton.icon(
                onPressed: state.loadingMoreMessages
                    ? null
                    : () => context.read<InboxBloc>().add(
                        const InboxMessagesLoadMoreRequested(),
                      ),
                icon: state.loadingMoreMessages
                    ? const SizedBox.square(
                        dimension: 18,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.history),
                label: const Text('تحميل رسائل أقدم'),
              ),
            ),
          );
        }
        final messageIndex = state.hasMoreMessages ? index - 1 : index;
        return _buildMessageBubble(context, state.messages[messageIndex]);
      },
    );
  }

  Widget _buildStatusSelector(
    BuildContext context,
    InboxState state,
    Conversation conversation,
  ) {
    final updating = state.statusUpdatesInProgress.contains(conversation.id);
    return Semantics(
      label: 'حالة المحادثة',
      child: Container(
        color: AppColors.surface,
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
        child: LayoutBuilder(
          builder: (context, constraints) {
            final selectorWidth = constraints.maxWidth < 360
                ? constraints.maxWidth
                : 200.0;
            return Wrap(
              alignment: WrapAlignment.spaceBetween,
              crossAxisAlignment: WrapCrossAlignment.center,
              spacing: 12,
              runSpacing: 8,
              children: [
                Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text('حالة المحادثة', style: AppTypography.label),
                    if (updating) ...[
                      const SizedBox(width: 8),
                      const SizedBox.square(
                        dimension: 18,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      ),
                    ],
                  ],
                ),
                SizedBox(
                  width: selectorWidth,
                  child: DropdownButtonFormField<String>(
                    key: ValueKey(
                      '${conversation.id}:${conversation.status.apiValue}',
                    ),
                    initialValue: conversation.status.apiValue,
                    items: const [
                      DropdownMenuItem(value: 'Open', child: Text('مفتوحة')),
                      DropdownMenuItem(
                        value: 'Pending',
                        child: Text('قيد المتابعة'),
                      ),
                      DropdownMenuItem(
                        value: 'Resolved',
                        child: Text('تم حلها'),
                      ),
                      DropdownMenuItem(value: 'Closed', child: Text('مغلقة')),
                    ],
                    onChanged: updating
                        ? null
                        : (value) {
                            if (value == null) return;
                            context.read<InboxBloc>().add(
                              InboxConversationStatusUpdateRequested(
                                conversationId: conversation.id,
                                status: value,
                              ),
                            );
                          },
                  ),
                ),
              ],
            );
          },
        ),
      ),
    );
  }

  Widget _buildMessageBubble(BuildContext context, Message message) {
    final time = DateFormat(
      'hh:mm a',
      'ar',
    ).format(message.createdAt.toLocal());
    final isCustomer = message.senderType == SenderType.customer;
    final isAi = message.senderType == SenderType.ai;

    if (message.senderType == SenderType.system) {
      return Semantics(
        label: 'رسالة نظام: ${message.content}',
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 10),
          child: Text(
            message.content,
            style: AppTypography.label,
            textAlign: TextAlign.center,
          ),
        ),
      );
    }

    final maxWidth = (MediaQuery.sizeOf(context).width * 0.78).clamp(
      240.0,
      520.0,
    );
    final senderLabel = isCustomer
        ? 'العميل'
        : isAi
        ? 'المساعد الذكي'
        : 'الفريق';
    final deliveryLabel = isCustomer
        ? null
        : _messageDeliveryLabel(message.status);

    return Semantics(
      container: true,
      label:
          'رسالة من $senderLabel، $time${deliveryLabel == null ? '' : '، $deliveryLabel'}',
      child: Padding(
        padding: const EdgeInsets.only(bottom: 12),
        child: Row(
          mainAxisAlignment: isCustomer
              ? MainAxisAlignment.start
              : MainAxisAlignment.end,
          children: [
            Container(
              constraints: BoxConstraints(maxWidth: maxWidth),
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
              decoration: BoxDecoration(
                color: isCustomer
                    ? AppColors.surface
                    : isAi
                    ? AppColors.secondary.withValues(alpha: 0.12)
                    : AppColors.primary.withValues(alpha: 0.12),
                border: Border.all(
                  color: isCustomer
                      ? AppColors.border
                      : isAi
                      ? AppColors.secondary.withValues(alpha: 0.4)
                      : AppColors.primary.withValues(alpha: 0.4),
                ),
                borderRadius: BorderRadius.circular(12),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  if (isAi) ...[
                    Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        const Icon(
                          Icons.auto_awesome,
                          color: AppColors.secondary,
                          size: 16,
                        ),
                        const SizedBox(width: 6),
                        Text(
                          'مساعد الذكاء الاصطناعي',
                          style: AppTypography.label.copyWith(
                            color: AppColors.secondary,
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 8),
                  ],
                  if (message.mediaType != null) _buildMediaContent(message),
                  if (message.content.trim().isNotEmpty) ...[
                    if (message.mediaType != null) const SizedBox(height: 8),
                    Text(message.content, style: AppTypography.body),
                  ],
                  const SizedBox(height: 6),
                  Wrap(
                    spacing: 8,
                    runSpacing: 4,
                    children: [
                      Text(time, style: AppTypography.mono),
                      if (deliveryLabel != null)
                        Text(
                          deliveryLabel,
                          style: AppTypography.label.copyWith(
                            color: message.status.toLowerCase() == 'failed'
                                ? AppColors.error
                                : AppColors.textMuted,
                          ),
                        ),
                    ],
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildMediaContent(Message message) {
    switch (message.mediaType) {
      case MediaType.image:
        if (message.mediaUrl == null || message.mediaUrl!.isEmpty) {
          return const _MediaSummary(label: 'صورة غير متاحة');
        }
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            ClipRRect(
              borderRadius: BorderRadius.circular(8),
              child: Image.network(
                message.mediaUrl!,
                width: 280,
                height: 180,
                cacheWidth: 560,
                fit: BoxFit.cover,
                filterQuality: FilterQuality.low,
                semanticLabel: 'صورة مرفقة',
                errorBuilder: (context, error, stackTrace) =>
                    const _MediaSummary(label: 'تعذر عرض الصورة'),
              ),
            ),
            TextButton.icon(
              onPressed: () => _openMedia(message.mediaUrl),
              icon: const Icon(Icons.open_in_new, size: 18),
              label: const Text('فتح الصورة'),
            ),
          ],
        );
      case MediaType.voice:
        return _MediaSummary(
          icon: Icons.graphic_eq,
          label: message.transcription?.trim().isNotEmpty == true
              ? 'تفريغ الرسالة الصوتية: ${message.transcription}'
              : 'رسالة صوتية',
          actionLabel: 'فتح الرسالة الصوتية',
          onTap: message.mediaUrl?.isNotEmpty == true
              ? () => _openMedia(message.mediaUrl)
              : null,
        );
      case MediaType.document:
        return _MediaSummary(
          icon: Icons.description_outlined,
          label: 'مستند مرفق',
          actionLabel: 'فتح المستند',
          onTap: message.mediaUrl?.isNotEmpty == true
              ? () => _openMedia(message.mediaUrl)
              : null,
        );
      case null:
        return const SizedBox.shrink();
    }
  }

  String? _messageDeliveryLabel(String status) {
    return switch (status.trim().toLowerCase()) {
      'pending' || 'sending' => 'جارٍ الإرسال',
      'sent' => 'أُرسلت',
      'delivered' => 'تم التسليم',
      'read' => 'مقروءة',
      'failed' => 'فشل التسليم',
      _ => null,
    };
  }

  Future<void> _openMedia(String? rawUrl) async {
    final uri = Uri.tryParse(rawUrl ?? '');
    if (uri == null || (uri.scheme != 'https' && uri.scheme != 'http')) {
      _showMediaError('رابط الملف غير صالح.');
      return;
    }
    try {
      final opened = await launchUrl(uri, mode: LaunchMode.externalApplication);
      if (!opened && mounted) {
        _showMediaError('تعذر فتح الملف على هذا الجهاز.');
      }
    } catch (_) {
      if (mounted) _showMediaError('تعذر فتح الملف على هذا الجهاز.');
    }
  }

  void _showMediaError(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(message), backgroundColor: AppColors.error),
    );
  }

  Widget _buildSuggestionBox(AISuggestion suggestion) {
    return Container(
      margin: const EdgeInsets.fromLTRB(16, 8, 16, 8),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.secondary.withValues(alpha: 0.45)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Wrap(
            alignment: WrapAlignment.spaceBetween,
            crossAxisAlignment: WrapCrossAlignment.center,
            spacing: 8,
            runSpacing: 8,
            children: [
              Text(
                'اقتراح رد من Gemini',
                style: AppTypography.title.copyWith(color: AppColors.secondary),
              ),
              Text(
                'ثقة ${(suggestion.confidenceScore * 100).clamp(0, 100).toStringAsFixed(0)}%',
                style: AppTypography.label.copyWith(color: AppColors.secondary),
              ),
            ],
          ),
          const SizedBox(height: 12),
          Text(suggestion.suggestionText, style: AppTypography.body),
          if (suggestion.reasoning.trim().isNotEmpty) ...[
            const SizedBox(height: 8),
            Text(suggestion.reasoning, style: AppTypography.bodyMuted),
          ],
          const SizedBox(height: 12),
          OutlinedButton.icon(
            onPressed: () {
              _messageController.text = suggestion.suggestionText;
              _messageController.selection = TextSelection.collapsed(
                offset: _messageController.text.length,
              );
            },
            icon: const Icon(Icons.edit_outlined),
            label: const Text('نسخ الاقتراح للمحرر'),
          ),
        ],
      ),
    );
  }

  Widget _buildAiTypingIndicator() {
    return Semantics(
      liveRegion: true,
      label: 'المساعد الذكي يجهّز ردًا',
      child: Container(
        width: double.infinity,
        padding: const EdgeInsetsDirectional.fromSTEB(16, 10, 16, 10),
        decoration: const BoxDecoration(
          color: AppColors.surface,
          border: Border(top: BorderSide(color: AppColors.border)),
        ),
        child: Row(
          children: [
            const Icon(
              Icons.auto_awesome_outlined,
              color: AppColors.secondary,
              size: 18,
            ),
            const SizedBox(width: 8),
            Text('المساعد الذكي يجهّز ردًا…', style: AppTypography.bodyMuted),
          ],
        ),
      ),
    );
  }

  Widget _buildComposer(InboxState state) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
      decoration: const BoxDecoration(
        color: AppColors.surface,
        border: Border(top: BorderSide(color: AppColors.border)),
      ),
      child: SafeArea(
        top: false,
        child: ValueListenableBuilder<TextEditingValue>(
          valueListenable: _messageController,
          builder: (context, draft, _) => Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Expanded(
                child: CallbackShortcuts(
                  bindings: {
                    const SingleActivator(
                      LogicalKeyboardKey.enter,
                      meta: true,
                    ): () =>
                        _sendDraft(state),
                    const SingleActivator(
                      LogicalKeyboardKey.enter,
                      control: true,
                    ): () =>
                        _sendDraft(state),
                  },
                  child: TextField(
                    controller: _messageController,
                    style: AppTypography.body,
                    minLines: 1,
                    maxLines: 5,
                    keyboardType: TextInputType.multiline,
                    decoration: const InputDecoration(
                      labelText: 'الرسالة',
                      hintText: 'اكتب رسالة…',
                      fillColor: AppColors.background,
                    ),
                  ),
                ),
              ),
              const SizedBox(width: 8),
              IconButton.filled(
                tooltip: state.sendingMessage
                    ? 'جارٍ الإرسال'
                    : 'إرسال الرسالة',
                onPressed: state.sendingMessage || draft.text.trim().isEmpty
                    ? null
                    : () => _sendDraft(state),
                icon: state.sendingMessage
                    ? const SizedBox.square(
                        dimension: 20,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.send),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _MediaSummary extends StatelessWidget {
  const _MediaSummary({
    required this.label,
    this.icon = Icons.broken_image_outlined,
    this.actionLabel,
    this.onTap,
  });

  final String label;
  final IconData icon;
  final String? actionLabel;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      label: actionLabel == null ? label : '$label، $actionLabel',
      link: onTap != null,
      child: Container(
        constraints: const BoxConstraints(minHeight: 56),
        padding: const EdgeInsets.all(12),
        decoration: BoxDecoration(
          color: AppColors.background,
          borderRadius: BorderRadius.circular(8),
          border: Border.all(color: AppColors.border),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(icon, color: AppColors.textMuted),
                const SizedBox(width: 8),
                Flexible(child: Text(label, style: AppTypography.bodyMuted)),
              ],
            ),
            if (onTap != null && actionLabel != null) ...[
              const SizedBox(height: 6),
              TextButton.icon(
                onPressed: onTap,
                icon: const Icon(Icons.open_in_new, size: 18),
                label: Text(actionLabel!),
              ),
            ],
          ],
        ),
      ),
    );
  }
}
