import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import '../../../core/theme/colors.dart';
import '../../../core/theme/typography.dart';
import '../../auth/bloc/auth_bloc.dart';
import '../bloc/crm_bloc.dart';
import '../data/models/crm_models.dart';

class PipelineBoardScreen extends StatefulWidget {
  const PipelineBoardScreen({Key? key}) : super(key: key);

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
      context.read<CrmBloc>().add(CrmPipelineFetchRequested(authState.activeProject.id));
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
          'خط المبيعات (Pipeline)',
          style: AppTypography.title.copyWith(fontWeight: FontWeight.bold),
        ),
        centerTitle: true,
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh, color: AppColors.primary),
            onPressed: _fetchPipeline,
          ),
        ],
      ),
      body: BlocBuilder<CrmBloc, CrmState>(
        builder: (context, state) {
          if (state.loadingPipeline) {
            return const Center(
              child: CircularProgressIndicator(
                valueColor: AlwaysStoppedAnimation(AppColors.primary),
              ),
            );
          }

          if (state.stages.isEmpty) {
            return Center(
              child: Text(
                'لم يتم تهيئة مراحل المبيعات بعد',
                style: AppTypography.bodyMuted,
              ),
            );
          }

          return ListView.builder(
            scrollDirection: Axis.horizontal,
            padding: const EdgeInsets.all(16),
            itemCount: state.stages.length,
            itemBuilder: (context, index) {
              final stage = state.stages[index];
              final stageDeals = state.deals.where((d) => d.pipelineStageId == stage.id).toList();
              return _buildStageColumn(context, stage, stageDeals, state.stages);
            },
          );
        },
      ),
    );
  }

  Widget _buildStageColumn(
    BuildContext context,
    PipelineStage stage,
    List<Deal> deals,
    List<PipelineStage> allStages,
  ) {
    return Container(
      width: 280,
      margin: const EdgeInsets.only(right: 16),
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
              border: Border(
                bottom: BorderSide(color: AppColors.border),
              ),
            ),
            child: Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                  decoration: BoxDecoration(
                    color: AppColors.primary.withOpacity(0.12),
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
                Text(
                  stage.name,
                  style: AppTypography.title.copyWith(fontWeight: FontWeight.bold),
                ),
              ],
            ),
          ),
          Expanded(
            child: ListView.separated(
              padding: const EdgeInsets.all(12),
              itemCount: deals.length,
              separatorBuilder: (context, index) => const SizedBox(height: 12),
              itemBuilder: (context, index) {
                final deal = deals[index];
                return _buildDealCard(context, deal, allStages);
              },
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildDealCard(BuildContext context, Deal deal, List<PipelineStage> allStages) {
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
            style: AppTypography.title.copyWith(fontSize: 14, fontWeight: FontWeight.bold),
            textAlign: TextAlign.right,
          ),
          const SizedBox(height: 8),
          Text(
            '${deal.amount.toStringAsFixed(0)} ج.م',
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
                icon: const Icon(Icons.swap_horiz, size: 18, color: AppColors.textMuted),
                onPressed: () => _showStagePicker(context, deal, allStages),
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
        color: color.withOpacity(0.12),
        border: Border.all(color: color.withOpacity(0.3)),
        borderRadius: BorderRadius.circular(4),
      ),
      child: Text(
        label,
        style: AppTypography.label.copyWith(color: color, fontSize: 10, fontWeight: FontWeight.bold),
      ),
    );
  }

  void _showStagePicker(BuildContext context, Deal deal, List<PipelineStage> stages) {
    showModalBottomSheet(
      context: context,
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
                  separatorBuilder: (context, index) => const Divider(color: AppColors.border),
                  itemBuilder: (context, index) {
                    final stage = stages[index];
                    final isCurrent = stage.id == deal.pipelineStageId;

                    return ListTile(
                      title: Text(
                        stage.name,
                        style: AppTypography.body.copyWith(
                          color: isCurrent ? AppColors.primary : AppColors.text,
                          fontWeight: isCurrent ? FontWeight.bold : FontWeight.normal,
                        ),
                        textAlign: TextAlign.right,
                      ),
                      trailing: isCurrent ? const Icon(Icons.check, color: AppColors.primary) : null,
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
