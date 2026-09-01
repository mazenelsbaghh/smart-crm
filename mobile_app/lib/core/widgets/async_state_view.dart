import 'package:flutter/material.dart';

import '../theme/colors.dart';
import '../theme/typography.dart';

class AppLoadingSkeleton extends StatelessWidget {
  const AppLoadingSkeleton({super.key, this.rows = 5});

  final int rows;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      label: 'جارٍ تحميل البيانات',
      liveRegion: true,
      child: ListView.separated(
        padding: const EdgeInsets.all(16),
        itemCount: rows,
        separatorBuilder: (context, index) => const SizedBox(height: 12),
        itemBuilder: (_, index) => Container(
          height: index == 0 ? 88 : 68,
          decoration: BoxDecoration(
            color: AppColors.surface,
            border: Border.all(color: AppColors.border),
            borderRadius: BorderRadius.circular(12),
          ),
        ),
      ),
    );
  }
}

class AppStateView extends StatelessWidget {
  const AppStateView({
    super.key,
    required this.icon,
    required this.title,
    required this.message,
    this.actionLabel,
    this.onAction,
  });

  final IconData icon;
  final String title;
  final String message;
  final String? actionLabel;
  final VoidCallback? onAction;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      liveRegion: true,
      child: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 480),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Icon(icon, size: 44, color: AppColors.textMuted),
                const SizedBox(height: 16),
                Text(
                  title,
                  style: AppTypography.title,
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: 8),
                Text(
                  message,
                  style: AppTypography.bodyMuted,
                  textAlign: TextAlign.center,
                ),
                if (onAction != null && actionLabel != null) ...[
                  const SizedBox(height: 20),
                  OutlinedButton.icon(
                    onPressed: onAction,
                    icon: const Icon(Icons.refresh),
                    label: Text(actionLabel!),
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}
