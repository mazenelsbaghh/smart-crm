import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:intl/intl.dart';

import '../../../core/theme/colors.dart';
import '../../../core/theme/typography.dart';
import '../../../core/widgets/async_state_view.dart';
import '../../auth/bloc/auth_bloc.dart';
import '../bloc/inbox_bloc.dart';
import '../data/models/chat_models.dart';
import 'chat_thread_screen.dart';

class InboxListScreen extends StatefulWidget {
  const InboxListScreen({super.key});

  @override
  State<InboxListScreen> createState() => _InboxListScreenState();
}

class _InboxListScreenState extends State<InboxListScreen> {
  final _searchController = TextEditingController();
  String _selectedStatus = 'All';
  Timer? _searchDebounce;

  @override
  void initState() {
    super.initState();
    _fetchConversations();
  }

  @override
  void dispose() {
    _searchDebounce?.cancel();
    _searchController.dispose();
    super.dispose();
  }

  void _fetchConversations() {
    final authState = context.read<AuthBloc>().state;
    if (authState is AuthAuthenticated) {
      context.read<InboxBloc>().add(
        InboxConversationsFetchRequested(
          projectId: authState.activeProject.id,
          status: _selectedStatus,
          search: _searchController.text,
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: AppColors.background,
      appBar: AppBar(
        backgroundColor: AppColors.surface,
        elevation: 0,
        title: Text(
          'المحادثات الواردة',
          style: AppTypography.title.copyWith(fontWeight: FontWeight.bold),
        ),
        centerTitle: true,
        actions: [
          IconButton(
            tooltip: 'تحديث المحادثات',
            icon: const Icon(Icons.refresh, color: AppColors.primary),
            onPressed: _fetchConversations,
          ),
        ],
      ),
      body: Column(
        children: [
          _buildSearchAndFilters(),
          Expanded(
            child: BlocBuilder<InboxBloc, InboxState>(
              builder: (context, state) {
                if (state.conversations.isEmpty && state.loadingConvs) {
                  return const AppLoadingSkeleton(rows: 7);
                }

                if (state.conversations.isEmpty && state.error != null) {
                  return AppStateView(
                    icon: Icons.cloud_off_outlined,
                    title: 'تعذر تحميل المحادثات',
                    message: state.error!,
                    actionLabel: 'إعادة المحاولة',
                    onAction: _fetchConversations,
                  );
                }

                if (state.conversations.isEmpty) {
                  return AppStateView(
                    icon: Icons.chat_bubble_outline,
                    title: 'لا توجد محادثات مطابقة',
                    message:
                        _searchController.text.trim().isNotEmpty ||
                            _selectedStatus != 'All'
                        ? 'غيّر كلمة البحث أو الفلتر لعرض نتائج أخرى.'
                        : 'ستظهر المحادثات الجديدة هنا عند وصولها.',
                    actionLabel:
                        _searchController.text.trim().isNotEmpty ||
                            _selectedStatus != 'All'
                        ? 'مسح الفلاتر'
                        : 'تحديث',
                    onAction: () {
                      _searchController.clear();
                      setState(() => _selectedStatus = 'All');
                      _fetchConversations();
                    },
                  );
                }

                return RefreshIndicator(
                  onRefresh: () async => _fetchConversations(),
                  child: NotificationListener<ScrollNotification>(
                    onNotification: (ScrollNotification scrollInfo) {
                      if (scrollInfo.metrics.pixels >=
                          scrollInfo.metrics.maxScrollExtent - 160) {
                        final authState = context.read<AuthBloc>().state;
                        if (authState is AuthAuthenticated) {
                          context.read<InboxBloc>().add(
                            InboxConversationsLoadMoreRequested(
                              projectId: authState.activeProject.id,
                            ),
                          );
                        }
                      }
                      return false;
                    },
                    child: ListView.separated(
                      physics: const AlwaysScrollableScrollPhysics(),
                      itemCount:
                          state.conversations.length +
                          (state.loadingConvs ? 1 : 0),
                      separatorBuilder: (context, index) =>
                          const Divider(color: AppColors.border, height: 1),
                      itemBuilder: (context, index) {
                        if (index == state.conversations.length) {
                          return const Padding(
                            padding: EdgeInsets.all(16),
                            child: Center(
                              child: SizedBox.square(
                                dimension: 24,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                ),
                              ),
                            ),
                          );
                        }
                        final conversation = state.conversations[index];
                        return _buildConversationCard(context, conversation);
                      },
                    ),
                  ),
                );
              },
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildSearchAndFilters() {
    return Container(
      color: AppColors.surface,
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
      child: Column(
        children: [
          TextField(
            controller: _searchController,
            style: AppTypography.body,
            textAlign: TextAlign.right,
            decoration: InputDecoration(
              labelText: 'البحث في المحادثات',
              hintText: 'ابحث عن عميل أو رقم هاتف...',
              hintStyle: AppTypography.bodyMuted,
              prefixIcon: const Icon(Icons.search, color: AppColors.textMuted),
              filled: true,
              fillColor: AppColors.background,
              contentPadding: const EdgeInsets.symmetric(vertical: 8),
              border: OutlineInputBorder(
                borderRadius: BorderRadius.circular(8),
                borderSide: const BorderSide(color: AppColors.border),
              ),
              enabledBorder: OutlineInputBorder(
                borderRadius: BorderRadius.circular(8),
                borderSide: const BorderSide(color: AppColors.border),
              ),
              focusedBorder: OutlineInputBorder(
                borderRadius: BorderRadius.circular(8),
                borderSide: const BorderSide(color: AppColors.primary),
              ),
            ),
            onChanged: (_) {
              _searchDebounce?.cancel();
              _searchDebounce = Timer(
                const Duration(milliseconds: 350),
                _fetchConversations,
              );
            },
          ),
          const SizedBox(height: 12),
          SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            child: Row(
              children: [
                _buildFilterTab('All', 'الكل'),
                const SizedBox(width: 8),
                _buildFilterTab('Open', 'مفتوحة'),
                const SizedBox(width: 8),
                _buildFilterTab('Pending', 'متابعة'),
                const SizedBox(width: 8),
                _buildFilterTab('Resolved', 'محلولة'),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildFilterTab(String status, String label) {
    final isActive = _selectedStatus == status;
    return ChoiceChip(
      label: Text(
        label,
        style: AppTypography.label.copyWith(
          color: isActive ? AppColors.background : AppColors.text,
          fontWeight: isActive ? FontWeight.bold : FontWeight.normal,
        ),
      ),
      selected: isActive,
      onSelected: (selected) {
        if (selected) {
          setState(() {
            _selectedStatus = status;
          });
          _fetchConversations();
        }
      },
      selectedColor: AppColors.primary,
      backgroundColor: AppColors.background,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(20),
        side: BorderSide(
          color: isActive ? AppColors.primary : AppColors.border,
        ),
      ),
    );
  }

  Widget _buildConversationCard(BuildContext context, Conversation conv) {
    final format = DateFormat('hh:mm a', 'ar');
    final timeStr = format.format(conv.lastMessageAt.toLocal());

    return InkWell(
      onTap: () {
        context.read<InboxBloc>().add(InboxActiveConversationSelected(conv));
        final reduceMotion = MediaQuery.disableAnimationsOf(context);
        Navigator.of(context).push(
          PageRouteBuilder<void>(
            pageBuilder: (context, animation, secondaryAnimation) =>
                const ChatThreadScreen(),
            transitionDuration: reduceMotion
                ? Duration.zero
                : const Duration(milliseconds: 180),
            reverseTransitionDuration: reduceMotion
                ? Duration.zero
                : const Duration(milliseconds: 150),
            transitionsBuilder:
                (context, animation, secondaryAnimation, child) =>
                    FadeTransition(opacity: animation, child: child),
          ),
        );
      },
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 16),
        child: Row(
          children: [
            Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(timeStr, style: AppTypography.mono),
                const SizedBox(height: 8),
                if (conv.unreadCount > 0)
                  Container(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 8,
                      vertical: 4,
                    ),
                    decoration: BoxDecoration(
                      color: AppColors.secondary,
                      borderRadius: BorderRadius.circular(10),
                    ),
                    child: Text(
                      '${conv.unreadCount}',
                      style: AppTypography.label.copyWith(
                        color: AppColors.text,
                        fontWeight: FontWeight.bold,
                      ),
                    ),
                  ),
              ],
            ),
            const Spacer(),
            Column(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    if (conv.customer.label != null) ...[
                      Container(
                        padding: const EdgeInsets.symmetric(
                          horizontal: 6,
                          vertical: 2,
                        ),
                        decoration: BoxDecoration(
                          color: AppColors.primary.withValues(alpha: 0.12),
                          border: Border.all(
                            color: AppColors.primary.withValues(alpha: 0.3),
                          ),
                          borderRadius: BorderRadius.circular(4),
                        ),
                        child: Text(
                          conv.customer.label!,
                          style: AppTypography.label.copyWith(
                            color: AppColors.primary,
                            fontSize: 12,
                          ),
                        ),
                      ),
                      const SizedBox(width: 8),
                    ],
                    Text(
                      conv.customer.name.isEmpty
                          ? 'عميل بلا اسم'
                          : conv.customer.name,
                      style: AppTypography.title.copyWith(
                        fontWeight: FontWeight.bold,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 6),
                Text(
                  conv.customer.phone,
                  style: AppTypography.bodyMuted.copyWith(fontSize: 12),
                ),
                if (conv.whatsAppAccountName != null &&
                    conv.whatsAppAccountName!.trim().isNotEmpty) ...[
                  const SizedBox(height: 3),
                  Text(
                    'عبر ${conv.whatsAppAccountName}',
                    style: AppTypography.label.copyWith(
                      color: AppColors.primary,
                      fontSize: 11,
                    ),
                  ),
                ],
              ],
            ),
            const SizedBox(width: 16),
            Container(
              width: 44,
              height: 44,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: AppColors.surface,
                border: Border.all(color: AppColors.border),
              ),
              child: Center(
                child: Text(
                  conv.customer.name.trim().isEmpty
                      ? 'ع'
                      : conv.customer.name
                            .trim()
                            .characters
                            .first
                            .toUpperCase(),
                  style: AppTypography.title.copyWith(
                    color: AppColors.primary,
                    fontWeight: FontWeight.bold,
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
