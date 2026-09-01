import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/colors.dart';
import '../../../core/theme/typography.dart';

class RegisterScreen extends StatelessWidget {
  const RegisterScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: AppColors.background,
      appBar: AppBar(
        leading: IconButton(
          tooltip: 'رجوع إلى تسجيل الدخول',
          onPressed: () => context.go('/'),
          icon: const Icon(Icons.arrow_forward),
        ),
      ),
      body: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 520),
            child: Container(
              padding: const EdgeInsets.all(24),
              decoration: BoxDecoration(
                color: AppColors.surface,
                border: Border.all(color: AppColors.border),
                borderRadius: BorderRadius.circular(16),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const Icon(
                    Icons.mark_email_read_outlined,
                    size: 52,
                    color: AppColors.primary,
                  ),
                  const SizedBox(height: 20),
                  Text(
                    'الانضمام يتم بدعوة',
                    style: AppTypography.headline,
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 12),
                  Text(
                    'إنشاء الحسابات الجديدة متاح من خلال مدير '
                    'النظام فقط. اطلب دعوة إلى مساحة العمل، ثم '
                    'استخدم بيانات الدخول التي تصلك.',
                    style: AppTypography.bodyMuted,
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 28),
                  ElevatedButton.icon(
                    onPressed: () => context.go('/'),
                    icon: const Icon(Icons.login),
                    label: const Text('العودة لتسجيل الدخول'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
