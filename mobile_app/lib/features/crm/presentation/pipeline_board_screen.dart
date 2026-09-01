import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:intl/intl.dart';

import '../../../core/theme/colors.dart';
import '../../../core/theme/typography.dart';
import '../../../core/widgets/async_state_view.dart';
import '../../auth/bloc/auth_bloc.dart';
import '../bloc/crm_bloc.dart';
import '../data/models/crm_models.dart';

class PipelineBoardScreen extends StatefulWidget {
  const PipelineBoardScreen({super.key});

  @override
  State<PipelineBoardScreen> createState() => _PipelineBoardScreenState();
}

class _PipelineBoardScreenState extends State<PipelineBoardScreen> {
  @override
  void initState() {
    super.initState();
    _fetchPipeline();
  }

  void _fetchPipeline() {
    final authState = context.read<AuthBloc>().state;
    if (authState is AuthAuthenticated) {
      context.read<CrmBloc>().add(
        CrmPipelineFetchRequested(authState.activeProject.id),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final pipelineBusy = context.select(
      (CrmBloc bloc) =>
          bloc.state.loadingPipeline ||
          bloc.state.dealMutationsInProgress.isNotEmpty,
    );
    return BlocListener<CrmBloc, CrmState>(
      listenWhen: (previous, current) =>
          previous.dealMutationError != current.dealMutationError &&
          current.dealMutationError != null,
      listener: (context, state) => ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(state.dealMutationError!),
          backgroundColor: AppColors.error,
        ),
      ),
      child: Scaffold(
        backgroundColor: AppColors.background,
        appBar: AppBar(
          backgroundColor: AppColors.surface,
          elevation: 0,
          title: Text(
            'مراحل المبيعات',
            style: AppTypography.title.copyWith(fontWeight: FontWeight.bold),
          ),
          centerTitle: true,
          actions: [
            IconButton(
              tooltip: 'تحديث مراحل المبيعات',
              icon: const Icon(Icons.refresh, color: AppColors.primary),
              onPressed: pipelineBusy ? null : _fetchPipeline,
            ),
          ],
        ),
        body: BlocBuilder<CrmBloc, CrmState>(
          builder: (context, state) {
            if (state.loadingPipeline && state.stages.isEmpty) {
              return const AppLoadingSkeleton(rows: 5);
            }

            if (state.error != null && state.stages.isEmpty) {
              return AppStateView(
                icon: Icons.cloud_off_outlined,
                title: 'تعذر تحميل مراحل المبيعات',
                message: state.error!,
                actionLabel: 'إعادة المحاولة',
                onAction: _fetchPipeline,
              );
            }

            if (state.stages.isEmpty) {
              return const AppStateView(
                icon: Icons.account_tree_outlined,
                title: 'لم يتم تهيئة المراحل بعد',
                message: 'أضف مراحل المبيعات من لوحة الويب ثم حدّث هذه الصفحة.',
              );
            }

            return ListView.builder(
              scrollDirection: Axis.horizontal,
              padding: const EdgeInsets.all(16),
              itemCount: state.stages.length,
              itemBuilder: (context, index) {
                final stage = state.stages[index];
                final stageDeals = state.deals
                    .where((d) => d.pipelineStageId == stage.id)
                    .toList();
                return _buildStageColumn(
                  context,
                  stage,
                  stageDeals,
                  state.stages,
                  state.dealMutationsInProgress,
                  pipelineBusy,
                );
              },
            );
          },
        ),
      ),
    );
  }

  Widget _buildStageColumn(
    BuildContext context,
    PipelineStage stage,
    List<Deal> deals,
    List<PipelineStage> allStages,
    Set<String> mutationsInProgress,
    bool pipelineBusy,
  ) {
    return Container(
      width: 280,
      margin: const EdgeInsetsDirectional.only(end: 16),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
            decoration: const BoxDecoration(
              border: Border(bottom: BorderSide(color: AppColors.border)),
            ),
            child: Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 8,
                    vertical: 2,
                  ),
                  decoration: BoxDecoration(
                    color: AppColors.primary.withValues(alpha: 0.12),
                    borderRadius: BorderRadius.circular(10),
                  ),
                  child: Text(
                    '${deals.length}',
                    style: AppTypography.label.copyWith(
                      color: AppColors.primary,
                      fontWeight: FontWeight.bold,
                    ),
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    stage.name,
                    style: AppTypography.title.copyWith(
                      fontWeight: FontWeight.bold,
                    ),
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
              ],
            ),
          ),
          Expanded(
            child: ListView.separated(
              padding: const EdgeInsets.all(12),
              itemCount: deals.isEmpty ? 1 : deals.length,
              separatorBuilder: (context, index) => const SizedBox(height: 12),
              itemBuilder: (context, index) {
                if (deals.isEmpty) {
                  return Padding(
                    padding: const EdgeInsets.all(12),
                    child: Text(
                      'لا توجد صفقات في هذه المرحلة.',
                      style: AppTypography.bodyMuted,
                      textAlign: TextAlign.center,
                    ),
                  );
                }
                final deal = deals[index];
                return _buildDealCard(
                  context,
                  deal,
                  allStages,
                  busy: pipelineBusy || mutationsInProgress.contains(deal.id),
                );
              },
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildDealCard(
    BuildContext context,
    Deal deal,
    List<PipelineStage> allStages, {
    required bool busy,
  }) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppColors.background,
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.end,
        children: [
          Text(
            deal.title,
            style: AppTypography.title.copyWith(
              fontSize: 14,
              fontWeight: FontWeight.bold,
            ),
            textAlign: TextAlign.right,
          ),
          const SizedBox(height: 8),
          Text(
            NumberFormat.currency(
              locale: 'ar_EG',
              symbol: 'ج.م',
              decimalDigits: 0,
            ).format(deal.amount),
            style: AppTypography.mono.copyWith(
              color: AppColors.primary,
              fontWeight: FontWeight.bold,
              fontSize: 13,
            ),
          ),
          const SizedBox(height: 12),
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              IconButton(
                tooltip: 'نقل الصفقة لمرحلة أخرى',
                icon: busy
                    ? const SizedBox.square(
                        dimension: 18,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.swap_horiz, size: 20),
                onPressed: busy
                    ? null
                    : () => _showStagePicker(context, deal, allStages),
              ),
              _buildStatusBadge(deal.status),
            ],
          ),
        ],
      ),
    );
  }

  Widget _buildStatusBadge(int status) {
    String label = 'مفتوح';
    Color color = AppColors.primary;
    if (status == 1) {
      label = 'ناجحة';
      color = AppColors.success;
    } else if (status == 2) {
      label = 'خاسرة';
      color = AppColors.error;
    }

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.12),
        border: Border.all(color: color.withValues(alpha: 0.3)),
        borderRadius: BorderRadius.circular(4),
      ),
      child: Text(
        label,
        style: AppTypography.label.copyWith(
          color: color,
          fontSize: 12,
          fontWeight: FontWeight.bold,
        ),
      ),
    );
  }

  void _showStagePicker(
    BuildContext context,
    Deal deal,
    List<PipelineStage> stages,
  ) {
    showModalBottomSheet<void>(
      context: context,
      useSafeArea: true,
      backgroundColor: AppColors.surface,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(16)),
      ),
      builder: (_) {
        return Container(
          padding: const EdgeInsets.all(20),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(
                'نقل الصفقة إلى مرحلة أخرى',
                style: AppTypography.headline.copyWith(fontSize: 18),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 20),
              Flexible(
                child: ListView.separated(
                  shrinkWrap: true,
                  itemCount: stages.length,
                  separatorBuilder: (context, index) =>
                      const Divider(color: AppColors.border),
                  itemBuilder: (context, index) {
                    final stage = stages[index];
                    final isCurrent = stage.id == deal.pipelineStageId;

                    return ListTile(
                      title: Text(
                        stage.name,
                        style: AppTypography.body.copyWith(
                          color: isCurrent ? AppColors.primary : AppColors.text,
                          fontWeight: isCurrent
                              ? FontWeight.bold
                              : FontWeight.normal,
                        ),
                        textAlign: TextAlign.right,
                      ),
                      trailing: isCurrent
                          ? const Icon(Icons.check, color: AppColors.primary)
                          : null,
                      onTap: () {
                        context.read<CrmBloc>().add(
                          CrmDealStageUpdateRequested(
                            dealId: deal.id,
                            pipelineStageId: stage.id,
                          ),
                        );
                        Navigator.of(context).pop();
                      },
                    );
                  },
                ),
              ),
            ],
          ),
        );
      },
    );
  }
}
