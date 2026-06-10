import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:intl/intl.dart';
import '../../../core/theme/colors.dart';
import '../../../core/theme/typography.dart';
import '../bloc/inbox_bloc.dart';
import '../data/models/chat_models.dart';
import 'conversation_detail_sheet.dart';

class ChatThreadScreen extends StatefulWidget {
  const ChatThreadScreen({Key? key}) : super(key: key);

  @override
  State<ChatThreadScreen> createState() => _ChatThreadScreenState();
}

class _ChatThreadScreenState extends State<ChatThreadScreen> {
  final _messageController = TextEditingController();
  final _scrollController = ScrollController();

  @override
  void dispose() {
    _messageController.dispose();
    _scrollController.dispose();
    super.dispose();
  }

  void _scrollBottom() {
    if (_scrollController.hasClients) {
      _scrollController.animateTo(
        _scrollController.position.maxScrollExtent,
        duration: const Duration(milliseconds: 300),
        curve: Curves.easeOutQuart,
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return BlocConsumer<InboxBloc, InboxState>(
      listener: (context, state) {
        WidgetsBinding.instance.addPostFrameCallback((_) => _scrollBottom());
      },
      builder: (context, state) {
        final conv = state.activeConv;
        if (conv == null) {
          return const Scaffold(
            backgroundColor: AppColors.background,
            body: Center(
              child: Text('يرجى اختيار محادثة أولاً'),
            ),
          );
        }

        return Scaffold(
          backgroundColor: AppColors.background,
          appBar: AppBar(
            backgroundColor: AppColors.surface,
            elevation: 0,
            leading: IconButton(
              icon: const Icon(Icons.arrow_back, color: AppColors.text),
              onPressed: () => Navigator.of(context).pop(),
            ),
            title: Column(
              crossAxisAlignment: CrossAxisAlignment.center,
              children: [
                Text(
                  conv.customer.name,
                  style: AppTypography.title.copyWith(fontWeight: FontWeight.bold),
                ),
                Text(
                  conv.customer.phone,
                  style: AppTypography.mono.copyWith(fontSize: 10),
                ),
              ],
            ),
            actions: [
              IconButton(
                icon: const Icon(Icons.info_outline, color: AppColors.primary),
                onPressed: () {
                  showModalBottomSheet(
                    context: context,
                    isScrollControlled: true,
                    backgroundColor: Colors.transparent,
                    builder: (_) => const ConversationDetailSheet(),
                  );
                },
              ),
            ],
          ),
          body: Column(
            children: [
              _buildStatusSelector(context, conv),
              Expanded(
                child: state.loadingMessages
                    ? const Center(
                        child: CircularProgressIndicator(
                          valueColor: AlwaysStoppedAnimation(AppColors.primary),
                        ),
                      )
                    : ListView.builder(
                        controller: _scrollController,
                        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 16),
                        itemCount: state.messages.length,
                        itemBuilder: (context, index) {
                          final msg = state.messages[index];
                          return _buildMessageBubble(msg);
                        },
                      ),
              ),
              if (state.aiSuggestion != null) _buildSuggestionBox(context, state.aiSuggestion!),
              _buildComposer(context),
            ],
          ),
        );
      },
    );
  }

  Widget _buildStatusSelector(BuildContext context, Conversation conv) {
    return Container(
      color: AppColors.surface,
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
            decoration: BoxDecoration(
              color: AppColors.background,
              borderRadius: BorderRadius.circular(4),
              border: Border.all(color: AppColors.border),
            ),
            child: DropdownButtonHideUnderline(
              child: DropdownButton<String>(
                value: conv.status.name,
                dropdownColor: AppColors.surface,
                style: AppTypography.label.copyWith(color: AppColors.text),
                items: ['Open', 'Pending', 'Resolved', 'Closed'].map((st) {
                  String label = 'مفتوحة';
                  if (st == 'Pending') label = 'قيد المتابعة';
                  if (st == 'Resolved') label = 'تم حلها';
                  if (st == 'Closed') label = 'مغلقة';

                  return DropdownMenuItem(
                    value: st,
                    child: Text(label),
                  );
                }).toList(),
                onChanged: (val) {
                  if (val != null) {
                    context.read<InboxBloc>().add(
                          InboxConversationStatusChanged(
                            conversationId: conv.id,
                            status: val,
                          ),
                        );
                  }
                },
              ),
            ),
          ),
          Text(
            'حالة المحادثة',
            style: AppTypography.label,
          ),
        ],
      ),
    );
  }

  Widget _buildMessageBubble(Message msg) {
    final format = DateFormat('hh:mm a');
    final timeStr = format.format(msg.createdAt);

    final isCustomer = msg.senderType == SenderType.Customer;
    final isAI = msg.senderType == SenderType.AI;

    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Row(
        mainAxisAlignment: isCustomer ? MainAxisAlignment.start : MainAxisAlignment.end,
        children: [
          Container(
            constraints: const BoxConstraints(maxWidth: 280),
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
            decoration: BoxDecoration(
              color: isCustomer
                  ? AppColors.surface
                  : isAI
                      ? AppColors.secondary.withOpacity(0.12)
                      : AppColors.primary.withOpacity(0.12),
              border: Border.all(
                color: isCustomer
                    ? AppColors.border
                    : isAI
                        ? AppColors.secondary.withOpacity(0.4)
                        : AppColors.primary.withOpacity(0.4),
              ),
              borderRadius: BorderRadius.only(
                topLeft: const Radius.circular(12),
                topRight: const Radius.circular(12),
                bottomLeft: Radius.circular(isCustomer ? 0 : 12),
                bottomRight: Radius.circular(isCustomer ? 12 : 0),
              ),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                if (isAI) ...[
                  Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      const Icon(Icons.auto_awesome, color: AppColors.secondary, size: 12),
                      const SizedBox(width: 4),
                      Text(
                        'مساعد الذكاء الاصطناعي',
                        style: AppTypography.label.copyWith(
                          color: AppColors.secondary,
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 6),
                ],
                Text(
                  msg.content,
                  style: AppTypography.body,
                  textAlign: TextAlign.right,
                ),
                const SizedBox(height: 4),
                Text(
                  timeStr,
                  style: AppTypography.mono.copyWith(fontSize: 9),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildSuggestionBox(BuildContext context, AISuggestion suggestion) {
    return Container(
      margin: const EdgeInsets.all(16),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.secondary.withOpacity(0.4)),
        boxShadow: [AppColors.neonGlow(color: AppColors.secondary)],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                decoration: BoxDecoration(
                  color: AppColors.secondary.withOpacity(0.12),
                  borderRadius: BorderRadius.circular(4),
                ),
                child: Text(
                  'تطابق ${(suggestion.confidenceScore * 100).toStringAsFixed(0)}%',
                  style: AppTypography.label.copyWith(color: AppColors.secondary),
                ),
              ),
              Row(
                children: [
                  Text(
                    'اقتراح رد من Gemini',
                    style: AppTypography.title.copyWith(color: AppColors.secondary),
                  ),
                  const SizedBox(width: 8),
                  const Icon(Icons.auto_awesome, color: AppColors.secondary, size: 16),
                ],
              ),
            ],
          ),
          const SizedBox(height: 12),
          Text(
            suggestion.suggestionText,
            style: AppTypography.body,
            textAlign: TextAlign.right,
          ),
          const SizedBox(height: 16),
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              ElevatedButton(
                onPressed: () {
                  _messageController.text = suggestion.suggestionText;
                },
                style: ElevatedButton.styleFrom(
                  backgroundColor: AppColors.secondary,
                  foregroundColor: AppColors.text,
                  padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(8),
                  ),
                ),
                child: const Text('استخدام الاقتراح'),
              ),
              Expanded(
                child: Padding(
                  padding: const EdgeInsets.only(left: 16),
                  child: Text(
                    suggestion.reasoning,
                    style: AppTypography.mono.copyWith(fontSize: 10),
                    overflow: TextOverflow.ellipsis,
                    maxLines: 2,
                    textAlign: TextAlign.right,
                  ),
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _buildComposer(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
      decoration: const BoxDecoration(
        color: AppColors.surface,
        border: Border(
          top: BorderSide(color: AppColors.border),
        ),
      ),
      child: SafeArea(
        child: Row(
          children: [
            IconButton(
              icon: const Icon(Icons.send, color: AppColors.primary),
              onPressed: () {
                if (_messageController.text.trim().isNotEmpty) {
                  context.read<InboxBloc>().add(
                        InboxMessageSent(_messageController.text.trim()),
                      );
                  _messageController.clear();
                }
              },
            ),
            Expanded(
              child: TextField(
                controller: _messageController,
                style: AppTypography.body,
                textAlign: TextAlign.right,
                decoration: InputDecoration(
                  hintText: 'اكتب رسالة...',
                  hintStyle: AppTypography.bodyMuted,
                  filled: true,
                  fillColor: AppColors.background,
                  contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
                  border: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(20),
                    borderSide: const BorderSide(color: AppColors.border),
                  ),
                  enabledBorder: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(20),
                    borderSide: const BorderSide(color: AppColors.border),
                  ),
                  focusedBorder: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(20),
                    borderSide: const BorderSide(color: AppColors.primary),
                  ),
                ),
              ),
            ),
            const SizedBox(width: 8),
            IconButton(
              icon: const Icon(Icons.attach_file, color: AppColors.textMuted),
              onPressed: () {},
            ),
          ],
        ),
      ),
    );
  }
}
