import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import '../../../core/theme/colors.dart';
import '../../../core/theme/typography.dart';
import '../bloc/auth_bloc.dart';
import '../data/models/user_model.dart';

class ProjectSelectScreen extends StatelessWidget {
  const ProjectSelectScreen({Key? key}) : super(key: key);

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: AppColors.background,
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 32),
          child: BlocConsumer<AuthBloc, AuthState>(
            listener: (context, state) {
              if (state is AuthAuthenticated) {
                context.go('/dashboard');
              } else if (state is AuthFailure) {
                ScaffoldMessenger.of(context).showSnackBar(
                  SnackBar(
                    content: Text(state.error),
                    backgroundColor: AppColors.error,
                  ),
                );
              }
            },
            builder: (context, state) {
              if (state is AuthProjectSelectionRequired) {
                return _buildSelectionList(context, state.projects);
              }
              return const Center(
                child: CircularProgressIndicator(
                  valueColor: AlwaysStoppedAnimation(AppColors.primary),
                ),
              );
            },
          ),
        ),
      ),
    );
  }

  Widget _buildSelectionList(BuildContext context, List<Project> projects) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Center(
          child: Text(
            'اختر المشروع',
            style: AppTypography.display.copyWith(color: AppColors.primary),
          ),
        ),
        const SizedBox(height: 8),
        Center(
          child: Text(
            'يرجى تحديد المشروع النشط للعمل عليه',
            style: AppTypography.bodyMuted,
          ),
        ),
        const SizedBox(height: 48),
        Expanded(
          child: ListView.separated(
            itemCount: projects.length,
            separatorBuilder: (context, index) => const SizedBox(height: 16),
            itemBuilder: (context, index) {
              final project = projects[index];
              return InkWell(
                onTap: () {
                  context.read<AuthBloc>().add(AuthProjectSelected(project));
                },
                borderRadius: BorderRadius.circular(12),
                child: Container(
                  padding: const EdgeInsets.all(24),
                  decoration: BoxDecoration(
                    color: AppColors.surface,
                    borderRadius: BorderRadius.circular(12),
                    border: Border.all(color: AppColors.border, width: 1),
                  ),
                  child: Row(
                    children: [
                      const Icon(
                        Icons.chevron_left,
                        color: AppColors.primary,
                      ),
                      const Spacer(),
                      Column(
                        crossAxisAlignment: CrossAxisAlignment.end,
                        children: [
                          Text(
                            project.name,
                            style: AppTypography.title.copyWith(
                              fontWeight: FontWeight.bold,
                            ),
                          ),
                          const SizedBox(height: 8),
                          Row(
                            children: [
                              Text(
                                project.settings.whatsappConnected
                                    ? 'متصل بالواتساب'
                                    : 'غير متصل بالواتساب',
                                style: AppTypography.label.copyWith(
                                  color: project.settings.whatsappConnected
                                      ? AppColors.success
                                      : AppColors.textMuted,
                                ),
                              ),
                              const SizedBox(width: 8),
                              Container(
                                width: 8,
                                height: 8,
                                decoration: BoxDecoration(
                                  shape: BoxShape.circle,
                                  color: project.settings.whatsappConnected
                                      ? AppColors.success
                                      : AppColors.textMuted,
                                ),
                              ),
                            ],
                          ),
                        ],
                      ),
                      const SizedBox(width: 16),
                      Container(
                        padding: const EdgeInsets.all(12),
                        decoration: BoxDecoration(
                          color: AppColors.background,
                          borderRadius: BorderRadius.circular(8),
                          border: Border.all(color: AppColors.border),
                        ),
                        child: const Icon(
                          Icons.workspaces_outline,
                          color: AppColors.primary,
                        ),
                      ),
                    ],
                  ),
                ),
              );
            },
          ),
        ),
      ],
    );
  }
}
