import 'package:flutter/material.dart';

import 'colors.dart';

class AppTypography {
  AppTypography._();

  static TextStyle get display => const TextStyle(
    fontSize: 32,
    fontWeight: FontWeight.w800,
    color: AppColors.text,
    height: 1.1,
    letterSpacing: -0.02,
  );

  static TextStyle get headline => const TextStyle(
    fontSize: 24,
    fontWeight: FontWeight.bold,
    color: AppColors.text,
    height: 1.2,
  );

  static TextStyle get title => const TextStyle(
    fontSize: 16,
    fontWeight: FontWeight.w600,
    color: AppColors.text,
    height: 1.4,
  );

  static TextStyle get body => const TextStyle(
    fontSize: 14,
    fontWeight: FontWeight.normal,
    color: AppColors.text,
    height: 1.6,
  );

  static TextStyle get bodyMuted => const TextStyle(
    fontSize: 14,
    fontWeight: FontWeight.normal,
    color: AppColors.textMuted,
    height: 1.6,
  );

  static TextStyle get label => const TextStyle(
    fontSize: 12,
    fontWeight: FontWeight.w500,
    color: AppColors.textMuted,
    letterSpacing: 0.05,
  );

  static TextStyle get labelUppercase => const TextStyle(
    fontSize: 12,
    fontWeight: FontWeight.w500,
    color: AppColors.textMuted,
    letterSpacing: 0.05,
  );

  static TextStyle get mono => const TextStyle(
    fontSize: 12,
    fontWeight: FontWeight.normal,
    color: AppColors.textMuted,
    fontFeatures: [FontFeature.tabularFigures()],
  );
}
